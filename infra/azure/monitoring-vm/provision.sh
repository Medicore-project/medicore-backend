#!/usr/bin/env bash
# =============================================================================
# MediCore — provision the production monitoring VM (Prometheus + Grafana)
#
#   medicore-vnet 10.20.0.0/16           (created by ../kafka-vm/provision.sh)
#     monitoring-subnet 10.20.3.0/24 → VM medicore-monitoring, private IP 10.20.3.10
#   medicore-monitoring-nsg on monitoring-subnet:
#     allow  TCP 80, 443 from $GRAFANA_SOURCES (default: Internet)
#     no SSH rule: the VM is managed with `az vm run-command`
#   medicore-kafka-nsg (existing) gains:
#     allow  TCP 9092 from monitoring-subnet   (kafka-exporter → broker)
#
# The public IP carries the DNS name Caddy gets the Let's Encrypt certificate
# for: <DNS_LABEL>.<region>.cloudapp.azure.com. Ports 80/443 must be reachable
# by Let's Encrypt, so narrowing GRAFANA_SOURCES breaks certificate issuance
# and renewal (README.md, "Security").
#
# Idempotent: every step skips a resource that already exists. It does not
# touch the App Service apps or their settings — scraping needs no change on
# their side. Ends by running deploy.sh, which starts the stack.
#
# Usage (Git Bash or Linux, logged in with `az login`):
#   infra/azure/monitoring-vm/provision.sh
# =============================================================================
set -euo pipefail

RG="${RG:-medicore-rg}"
LOCATION="${LOCATION:-centralindia}"
VNET="medicore-vnet"
SUBNET="monitoring-subnet"
SUBNET_PREFIX="10.20.3.0/24"
NSG="medicore-monitoring-nsg"
KAFKA_NSG="medicore-kafka-nsg"
VM="medicore-monitoring"
VM_IP="10.20.3.10"
DNS_LABEL="${DNS_LABEL:-medicore-monitoring}"
GRAFANA_SOURCES="${GRAFANA_SOURCES:-Internet}"
# 2 vCPU B-series, smallest first: 1 GB plus the 2 GB swap file cloud-init
# adds is enough for this stack; the 4 GB size is the fallback.
VM_SIZES="${VM_SIZES:-Standard_B2ats_v2 Standard_B2als_v2 Standard_B2ls_v2}"

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

exists() { "$@" -o none > /dev/null 2>&1; }

echo "▶ Network"
exists az network vnet show -g "$RG" -n "$VNET" \
  || { echo "✖ $VNET not found — run infra/azure/kafka-vm/provision.sh first" >&2; exit 1; }
if ! exists az network vnet subnet show -g "$RG" --vnet-name "$VNET" -n "$SUBNET"; then
  az network vnet subnet create -g "$RG" --vnet-name "$VNET" -n "$SUBNET" \
    --address-prefixes "$SUBNET_PREFIX" -o none
fi

echo "▶ NSG"
if ! exists az network nsg show -g "$RG" -n "$NSG"; then
  az network nsg create -g "$RG" -n "$NSG" -l "$LOCATION" -o none
  az network nsg rule create -g "$RG" --nsg-name "$NSG" -n allow-https-grafana \
    --priority 100 --direction Inbound --access Allow --protocol Tcp \
    --source-address-prefixes "$GRAFANA_SOURCES" --destination-port-ranges 80 443 -o none
fi
az network vnet subnet update -g "$RG" --vnet-name "$VNET" -n "$SUBNET" \
  --network-security-group "$NSG" -o none

echo "▶ Kafka NSG: admit kafka-exporter"
if ! exists az network nsg rule show -g "$RG" --nsg-name "$KAFKA_NSG" -n allow-kafka-from-monitoring; then
  # 110: after allow-kafka-from-appsvc (100), before deny-kafka-other (200)
  az network nsg rule create -g "$RG" --nsg-name "$KAFKA_NSG" -n allow-kafka-from-monitoring \
    --priority 110 --direction Inbound --access Allow --protocol Tcp \
    --source-address-prefixes "$SUBNET_PREFIX" --destination-port-ranges 9092 -o none
fi

echo "▶ VM"
if ! exists az vm show -g "$RG" -n "$VM"; then
  CLOUD_INIT="$(mktemp)"
  trap 'rm -f "$CLOUD_INIT"' EXIT
  # Docker and swap only; deploy.sh ships the stack itself, so the first
  # deployment and every later update take the same path.
  cat > "$CLOUD_INIT" <<'EOF'
#cloud-config
package_update: true
packages:
  - docker.io
  - docker-compose-v2
swap:
  filename: /swapfile
  size: 2G
  maxsize: 2G
runcmd:
  - systemctl enable --now docker
  - mkdir -p /opt/medicore-monitoring
EOF
  created=""
  for size in $VM_SIZES; do
    echo "  trying $size"
    if out=$(az vm create -g "$RG" -n "$VM" -l "$LOCATION" \
        --image Ubuntu2404 --size "$size" \
        --vnet-name "$VNET" --subnet "$SUBNET" --private-ip-address "$VM_IP" \
        --public-ip-address "${VM}-pip" --public-ip-sku Standard \
        --public-ip-address-dns-name "$DNS_LABEL" \
        --nsg "" \
        --storage-sku StandardSSD_LRS \
        --admin-username medicoreops --generate-ssh-keys \
        --custom-data "$CLOUD_INIT" -o none 2>&1); then
      created="$size"
      break
    fi
    if grep -q "SkuNotAvailable" <<< "$out"; then
      echo "  $size has no capacity in $LOCATION right now"
      continue
    fi
    echo "$out" >&2
    exit 1
  done
  [[ -n "$created" ]] || { echo "✖ none of [$VM_SIZES] has capacity in $LOCATION" >&2; exit 1; }
  echo "  created $VM as $created"
fi

echo "▶ Waiting for cloud-init (Docker install, 2–5 min)"
az vm run-command invoke -g "$RG" -n "$VM" --command-id RunShellScript \
  --scripts "cloud-init status --wait > /dev/null; cloud-init status; docker --version; docker compose version" \
  --query "value[0].message" -o tsv | tr -d '\r'

echo "▶ Deploying the stack"
"$HERE/deploy.sh"
