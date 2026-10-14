#!/usr/bin/env bash
# =============================================================================
# MediCore — health of the production monitoring VM, without SSH
#
# Runs on the VM through `az vm run-command` and prints:
#   - the containers
#   - every scrape job with its up/down state
#   - the alerts that are pending or firing
# then checks the public Grafana endpoint and its certificate from here.
#
# Usage:  infra/azure/monitoring-vm/check.sh
# =============================================================================
set -euo pipefail

RG="${RG:-medicore-rg}"
VM="${VM:-medicore-monitoring}"
PIP="${PIP:-${VM}-pip}"

native_path() { if command -v cygpath > /dev/null; then cygpath -w "$1"; else echo "$1"; fi; }

SCRIPT="$(mktemp)"
trap 'rm -f "$SCRIPT"' EXIT
cat > "$SCRIPT" <<'EOF'
api() { docker exec medicore-prometheus wget -qO- "http://localhost:9090/api/v1/$1"; }
echo "== containers"
docker ps -a --filter "name=medicore-" --format "{{.Names}}  {{.Status}}"
echo "== scrape targets"
api targets | python3 -c '
import json, sys
for t in sorted(json.load(sys.stdin)["data"]["activeTargets"], key=lambda t: t["labels"]["job"]):
    err = (t.get("lastError") or "")[:80]
    print("  %-16s %-5s %s  %s" % (t["labels"]["job"], t["health"], t["scrapeUrl"], err))'
echo "== alerts (pending/firing)"
api alerts | python3 -c '
import json, sys
alerts = json.load(sys.stdin)["data"]["alerts"]
for a in alerts:
    l = a["labels"]
    print("  %-8s %s  %s" % (a["state"], l["alertname"], l.get("job") or l.get("topic") or l.get("consumergroup") or ""))
if not alerts:
    print("  none")'
EOF

az vm run-command invoke -g "$RG" -n "$VM" --command-id RunShellScript \
  --scripts @"$(native_path "$SCRIPT")" \
  --query "value[0].message" -o tsv | tr -d '\r'

DOMAIN="${DOMAIN:-$(az network public-ip show -g "$RG" -n "$PIP" --query dnsSettings.fqdn -o tsv | tr -d '\r')}"
echo "== https://$DOMAIN"
curl -s -o /dev/null -m 30 -w "  /api/health → HTTP %{http_code}, TLS verify %{ssl_verify_result} (0 = valid)\n" \
  "https://$DOMAIN/api/health" || echo "  unreachable"
