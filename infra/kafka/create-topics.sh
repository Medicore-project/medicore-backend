#!/usr/bin/env bash
# =============================================================================
# MediCore — Kafka topic bootstrap script
# Runs once inside the kafka-init container and exits.
# Idempotent: existing topics are silently skipped.
# =============================================================================
set -euo pipefail

BOOTSTRAP="kafka:9092"

# ---------------------------------------------------------------------------
# Helper — create a topic if it does not already exist
# Args: <topic-name> <partitions> <replication-factor>
# ---------------------------------------------------------------------------
create_topic() {
  local topic="$1"
  local partitions="${2:-3}"
  local replication="${3:-1}"

  echo "→ Ensuring topic exists: ${topic} (partitions=${partitions})"
  /opt/kafka/bin/kafka-topics.sh \
    --bootstrap-server "${BOOTSTRAP}" \
    --create \
    --if-not-exists \
    --topic "${topic}" \
    --partitions "${partitions}" \
    --replication-factor "${replication}"
}

# ---------------------------------------------------------------------------
# Wait until the broker is fully up
# ---------------------------------------------------------------------------
echo "⏳  Waiting for Kafka broker at ${BOOTSTRAP} …"
until /opt/kafka/bin/kafka-broker-api-versions.sh --bootstrap-server "${BOOTSTRAP}" > /dev/null 2>&1; do
  echo "    broker not ready yet — retrying in 3 s"
  sleep 3
done
echo "✅  Kafka broker is up."

# ---------------------------------------------------------------------------
# Primary topics — 3 partitions each, keyed by aggregate ID
# ---------------------------------------------------------------------------
TOPICS=(
  "staff-events"
  "patient-events"
  "appointment-events"
  "billing-events"
)

for topic in "${TOPICS[@]}"; do
  create_topic "${topic}" 3 1

  # .retry companion — single partition (ordering matters for retry logic)
  create_topic "${topic}.retry" 1 1

  # .dlt (dead-letter topic) — single partition
  create_topic "${topic}.dlt"   1 1
done

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "  Kafka topics created / verified:"
/opt/kafka/bin/kafka-topics.sh --bootstrap-server "${BOOTSTRAP}" --list | sort | sed 's/^/    /'
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "✅  kafka-init complete."
