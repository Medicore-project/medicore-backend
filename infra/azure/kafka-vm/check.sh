#!/usr/bin/env bash
# =============================================================================
# MediCore — health of the production Kafka VM, without SSH
#
# Runs on the VM through `az vm run-command` and prints:
#   - the broker containers
#   - the topics with partition counts
#   - every consumer group with its lag
#
# Usage:  infra/azure/kafka-vm/check.sh
# =============================================================================
set -euo pipefail

RG="${RG:-medicore-rg}"
VM="${VM:-medicore-kafka}"

az vm run-command invoke -g "$RG" -n "$VM" --command-id RunShellScript \
  --query "value[0].message" -o tsv --scripts '
K="docker exec medicore-kafka /opt/kafka/bin"
echo "== containers"; docker ps -a --format "{{.Names}}  {{.Status}}"
echo "== topics";     $K/kafka-topics.sh --bootstrap-server localhost:19092 --describe 2>/dev/null | grep -E "^Topic:" | awk "{print \$2, \$6}"
echo "== consumer groups"
for g in $($K/kafka-consumer-groups.sh --bootstrap-server localhost:19092 --list 2>/dev/null); do
  echo "-- $g"
  $K/kafka-consumer-groups.sh --bootstrap-server localhost:19092 --describe --group "$g" 2>/dev/null \
    | awk "NR>1 && NF>=6 && \$2 != \"TOPIC\" {print \"   \", \$2, \"p\"\$3, \"lag\", \$6}"
done
' | tr -d '\r'
