#!/usr/bin/env bash
# =============================================================================
# MediCore — provision the production Kafka VM and its private network
#
#   medicore-vnet 10.20.0.0/16
#     kafka-subnet   10.20.1.0/24  → VM medicore-kafka, private IP 10.20.1.10
#     appsvc-subnet  10.20.2.0/26  → delegated to App Service (VNet integration)
#   medicore-kafka-nsg on kafka-subnet:
#     allow  TCP 9092 from appsvc-subnet
#     deny   TCP 9092 from anywhere else
#     no SSH rule: the VM is managed with `az vm run-command`
#
# The VM's public IP exists only to give it outbound internet access (apt,
# Docker Hub); the NSG admits nothing inbound from the internet.
#
# Idempotent: every step skips a resource that already exists. App settings
# are not touched here — the cutover is a separate, deliberate step
# (see README.md, "Cutover").
#
# Usage (Git Bash or Linux, logged in with `az login`):
#   infra/azure/kafka-vm/provision.sh
# =============================================================================
set -euo pipefail

RG="${RG:-medicore-rg}"
LOCATION="${LOCATION:-centralindia}"
VNET="medicore-vnet"
KAFKA_SUBNET="kafka-subnet"
APPSVC_SUBNET="appsvc-subnet"
NSG="medicore-kafka-nsg"
VM="medicore-kafka"
# 2 vCPU / 4 GB B-series; the first one with capacity in the region is used
VM_SIZES="${VM_SIZES:-Standard_B2als_v2 Standard_B2ls_v2 Standard_B2s}"
KAFKA_IP="10.20.1.10"
APPSVC_PREFIX="10.20.2.0/26"
APPS="${APPS:-medicore-identity medicore-patient medicore-appointment medicore-billing}"

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$HERE/../../.." && pwd)"

exists() { "$@" -o none > /dev/null 2>&1; }

echo "▶ Network"
if ! exists az network vnet show -g "$RG" -n "$VNET"; then
  az network vnet create -g "$RG" -n "$VNET" -l "$LOCATION" \
    --address-prefixes 10.20.0.0/16 \
    --subnet-name "$KAFKA_SUBNET" --subnet-prefixes 10.20.1.0/24 -o none
fi
if ! exists az network vnet subnet show -g "$RG" --vnet-name "$VNET" -n "$APPSVC_SUBNET"; then
  az network vnet subnet create -g "$RG" --vnet-name "$VNET" -n "$APPSVC_SUBNET" \
    --address-prefixes "$APPSVC_PREFIX" --delegations Microsoft.Web/serverFarms -o none
fi

echo "▶ NSG"
if ! exists az network nsg show -g "$RG" -n "$NSG"; then
  az network nsg create -g "$RG" -n "$NSG" -l "$LOCATION" -o none
  az network nsg rule create -g "$RG" --nsg-name "$NSG" -n allow-kafka-from-appsvc \
    --priority 100 --direction Inbound --access Allow --protocol Tcp \
    --source-address-prefixes "$APPSVC_PREFIX" --destination-port-ranges 9092 -o none
  az network nsg rule create -g "$RG" --nsg-name "$NSG" -n deny-kafka-other \
    --priority 200 --direction Inbound --access Deny --protocol Tcp \
    --source-address-prefixes '*' --destination-port-ranges 9092 -o none
fi
az network vnet subnet update -g "$RG" --vnet-name "$VNET" -n "$KAFKA_SUBNET" \
  --network-security-group "$NSG" -o none

echo "▶ VM"
if ! exists az vm show -g "$RG" -n "$VM"; then
  CLOUD_INIT="$(mktemp)"
  trap 'rm -f "$CLOUD_INIT"' EXIT
  b64() { base64 -w0 < "$1"; }
  cat > "$CLOUD_INIT" <<EOF
#cloud-config
package_update: true
packages:
  - docker.io
  - docker-compose-v2
write_files:
  - path: /opt/medicore-kafka/docker-compose.yml
    encoding: b64
    content: $(b64 "$HERE/docker-compose.yml")
  - path: /opt/medicore-kafka/create-topics.sh
    encoding: b64
    permissions: '0755'
    content: $(b64 "$REPO_ROOT/infra/kafka/create-topics.sh")
  - path: /opt/medicore-kafka/.env
    content: |
      KAFKA_ADVERTISED_HOST=${KAFKA_IP}
runcmd:
  - systemctl enable --now docker
  - cd /opt/medicore-kafka && docker compose up -d
EOF
  created=""
  for size in $VM_SIZES; do
    echo "  trying $size"
    if out=$(az vm create -g "$RG" -n "$VM" -l "$LOCATION" \
        --image Ubuntu2404 --size "$size" \
        --vnet-name "$VNET" --subnet "$KAFKA_SUBNET" --private-ip-address "$KAFKA_IP" \
        --public-ip-address "${VM}-pip" --public-ip-sku Standard \
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

echo "▶ App Service VNet integration"
for app in $APPS; do
  current=$(az webapp vnet-integration list -g "$RG" -n "$app" --query "[0].vnetResourceId" -o tsv 2>/dev/null | tr -d '\r')
  if [[ "$current" != *"/virtualNetworks/$VNET/subnets/$APPSVC_SUBNET" ]]; then
    az webapp vnet-integration add -g "$RG" -n "$app" --vnet "$VNET" --subnet "$APPSVC_SUBNET" -o none
    echo "  $app integrated"
  else
    echo "  $app already integrated"
  fi
done

echo "✅ Done. Kafka will be at ${KAFKA_IP}:9092 once cloud-init finishes (2–5 min)."
echo "   Check it:  infra/azure/kafka-vm/check.sh"
