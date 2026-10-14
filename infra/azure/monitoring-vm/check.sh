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
# curl verifies the certificate, so any status line here means TLS is valid.
# The status comes from the headers, not -w: Git for Windows' curl 8.8
# (Schannel) fails any -w on this host with error 43.
status=$(curl -sS -m 30 -o /dev/null -D - "https://$DOMAIN/api/health" 2>/dev/null | head -n 1 | tr -d '\r' || true)
if [[ -z "$status" ]]; then
  echo "  /api/health -> no HTTPS response (on the first run, Caddy may still be getting the certificate)"
else
  echo "  /api/health -> $status, certificate valid"
fi
