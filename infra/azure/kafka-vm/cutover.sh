#!/usr/bin/env bash
# =============================================================================
# MediCore — point the App Service apps at the Kafka VM
#
# For each app: Kafka__BootstrapServers → 10.20.1.10:9092, and the two SASL
# settings are removed. The services enable SASL_SSL only when a SASL username
# and password are set, so without them they connect over PLAINTEXT — which
# the broker accepts on the private network only (see provision.sh).
#
# Each settings change restarts the app (2–8 minutes on the B1 plan).
#
# Usage:  infra/azure/kafka-vm/cutover.sh
#         APPS="medicore-billing" infra/azure/kafka-vm/cutover.sh   # one app
# Rollback: README.md, "Rollback".
# =============================================================================
set -euo pipefail

RG="${RG:-medicore-rg}"
BOOTSTRAP="${BOOTSTRAP:-10.20.1.10:9092}"
APPS="${APPS:-medicore-identity medicore-patient medicore-appointment medicore-billing}"

for app in $APPS; do
  vnet=$(az webapp vnet-integration list -g "$RG" -n "$app" --query "[0].vnetResourceId" -o tsv | tr -d '\r')
  if [[ "$vnet" != *"/virtualNetworks/medicore-vnet/subnets/appsvc-subnet" ]]; then
    echo "✖ $app is not integrated with medicore-vnet — run provision.sh first. Skipped."
    continue
  fi
  az webapp config appsettings delete -g "$RG" -n "$app" \
    --setting-names Kafka__SaslUsername Kafka__SaslPassword -o none
  az webapp config appsettings set -g "$RG" -n "$app" \
    --settings "Kafka__BootstrapServers=$BOOTSTRAP" -o none
  echo "✔ $app → $BOOTSTRAP (restarting)"
done
