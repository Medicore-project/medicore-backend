#!/usr/bin/env bash
# =============================================================================
# MediCore — ship the monitoring config to the VM and (re)start the stack
#
# Bundles, from this repo:
#   infra/azure/monitoring-vm/docker-compose.yml   → docker-compose.yml
#   infra/azure/monitoring-vm/prometheus.yml       → prometheus/prometheus.yml
#   infra/prometheus/rules/*.yml                   → prometheus/rules/   (shared with local)
#   infra/grafana/provisioning/                    → grafana/provisioning/ (shared with local)
#   infra/azure/monitoring-vm/Caddyfile            → caddy/Caddyfile
# and unpacks them into /opt/medicore-monitoring through `az vm run-command`
# (no SSH). Then `docker compose up -d`, a Prometheus config reload (SIGHUP)
# and a Caddy reload. Grafana picks up dashboard changes within 30 s by itself.
#
# Run it after any change to those files. provision.sh runs it once at the end.
# The Grafana admin password is generated on the VM on the first run and is
# never sent back here (README.md, "Grafana login").
#
# Usage:  infra/azure/monitoring-vm/deploy.sh
# =============================================================================
set -euo pipefail

RG="${RG:-medicore-rg}"
VM="${VM:-medicore-monitoring}"
PIP="${PIP:-${VM}-pip}"
KAFKA_BOOTSTRAP="${KAFKA_BOOTSTRAP:-10.20.1.10:9092}"

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$HERE/../../.." && pwd)"

# az on Windows is a native program and can't read Git Bash /tmp paths
native_path() { if command -v cygpath > /dev/null; then cygpath -w "$1"; else echo "$1"; fi; }

DOMAIN="${DOMAIN:-$(az network public-ip show -g "$RG" -n "$PIP" --query dnsSettings.fqdn -o tsv | tr -d '\r')}"
[[ -n "$DOMAIN" ]] || { echo "✖ $PIP has no DNS name — set DOMAIN or run provision.sh" >&2; exit 1; }

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
STAGE="$WORK/stage"
mkdir -p "$STAGE/prometheus/rules" "$STAGE/grafana" "$STAGE/caddy"
cp "$HERE/docker-compose.yml"                "$STAGE/docker-compose.yml"
cp "$HERE/prometheus.yml"                    "$STAGE/prometheus/prometheus.yml"
cp "$REPO_ROOT"/infra/prometheus/rules/*.yml "$STAGE/prometheus/rules/"
cp -r "$REPO_ROOT/infra/grafana/provisioning" "$STAGE/grafana/provisioning"
cp "$HERE/Caddyfile"                         "$STAGE/caddy/Caddyfile"
tar -czf "$WORK/bundle.tgz" -C "$STAGE" .
BUNDLE="$(base64 -w0 < "$WORK/bundle.tgz")"
echo "▶ Bundle: $(find "$STAGE" -type f | wc -l) files, $(wc -c < "$WORK/bundle.tgz") bytes → $VM ($DOMAIN)"

# The bundle goes in through a script file (--scripts @file): inline it would
# exceed the Windows command-line limit.
cat > "$WORK/remote.sh" <<EOF
#!/bin/bash
set -euo pipefail
D=/opt/medicore-monitoring
mkdir -p "\$D" && cd "\$D"
NEW=\$(mktemp -d)
echo '$BUNDLE' | base64 -d | tar -xz --no-same-owner -C "\$NEW"
# Replace directory contents in place, keeping the directories themselves:
# the containers bind-mount them, and a recreated directory would detach them.
for d in prometheus grafana caddy; do
  mkdir -p "\$d"
  find "\$d" -mindepth 1 -delete
  cp -a "\$NEW/\$d/." "\$d/"
done
cp "\$NEW/docker-compose.yml" docker-compose.yml
rm -rf "\$NEW"
chmod -R a+rX prometheus grafana caddy

touch .env && chmod 600 .env
set_env() { if grep -q "^\$1=" .env; then sed -i "s|^\$1=.*|\$1=\$2|" .env; else echo "\$1=\$2" >> .env; fi; }
set_env DOMAIN '$DOMAIN'
set_env KAFKA_BOOTSTRAP '$KAFKA_BOOTSTRAP'
grep -q '^GRAFANA_ADMIN_PASSWORD=' .env || echo "GRAFANA_ADMIN_PASSWORD=\$(openssl rand -base64 30 | tr -d '/+=')" >> .env

docker compose up -d --remove-orphans --quiet-pull 2>&1 | tail -n 20
docker kill -s HUP medicore-prometheus > /dev/null && echo "prometheus config reloaded"
docker exec medicore-caddy caddy reload --config /etc/caddy/Caddyfile > /dev/null 2>&1 \
  && echo "caddy config reloaded" || echo "caddy reload skipped (container still starting)"
docker compose ps --format '{{.Name}}  {{.Status}}'
EOF

az vm run-command invoke -g "$RG" -n "$VM" --command-id RunShellScript \
  --scripts @"$(native_path "$WORK/remote.sh")" \
  --query "value[0].message" -o tsv | tr -d '\r'

echo "✅ Grafana: https://$DOMAIN   (the certificate can take a minute on the first run)"
echo "   Check it:  infra/azure/monitoring-vm/check.sh"
