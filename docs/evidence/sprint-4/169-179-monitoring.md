# SCRUM-169 / 179 — Billing monitoring, alerts and notifications

| | |
|---|---|
| Date | 2026-10-10 |
| Environment | Local docker compose stack, rebuilt with `docker compose up -d --build` from `develop` @ `8de968a` plus this branch's monitoring config |
| Scope | Monitoring is local only: Prometheus scrapes compose container names, and nothing in Azure is scraped (plan §1) |

## Changes

| File | Change | Why |
|---|---|---|
| `infra/prometheus/prometheus.yml` | `billing-api` job now targets `medicore-billing:8080` as well as `host.docker.internal:5004` | Before this, Billing was only scraped when run with `dotnet run`, so the compose container was invisible |
| `infra/prometheus/rules/service-alerts.yml` | New `ServiceDown`: `max by (job) (up{job=~"gateway\|identity-api\|patient-api\|appointment-api\|billing-api"}) == 0` for 1m, critical | Each job lists two targets and only one runs, so raw `up == 0` would fire forever. `max by (job)` fires only when **no** target answers |
| `infra/prometheus/rules/dlt-alerts.yml` | `KafkaDeadLetterQueueNotEmpty` and `KafkaConsumerLagHigh` now apply `max without (instance)` before `sum` | The exporter is scraped twice (container + the published `host.docker.internal:9308`), both `up`, so every Kafka value was **doubled**: a lag of 30 tripped the >50 alert |
| `infra/grafana/provisioning/dashboards/medicore-overview.json` | **F3:** removed the duplicated stale fragment that made the file invalid JSON. Panels 11/21 retitled "Patient — …". **Billing:** request-rate, 5xx and p50/p95/p99 panels (ids 13/23/33); each row resized to four 6-wide panels with no `gridPos` overlap; Billing tile changed from orange "NOT YET" to red "DOWN" like the others. **Kafka:** the three panels deduplicated the same way as the rules | The observability demo was broken on `develop`, and Billing had only an up/down tile |
| `monitoring/prometheus.yml` | **Deleted** | Stale, unused duplicate (F14); compose mounts `infra/prometheus/` |

## Verification

| Check | Result |
|---|---|
| `promtool check config` (prom/prometheus:latest) | `SUCCESS`: `dlt-alerts.yml` 2 rules, `service-alerts.yml` 1 rule |
| Prometheus `/api/v1/targets` | `billing-api medicore-billing:8080` **up**. Every service's container target up. The `host.docker.internal` targets for identity/patient/appointment/billing down, as expected. Both kafka-exporter instances up, which confirms the double count |
| Prometheus `/api/v1/rules` | `ServiceDown`, `KafkaDeadLetterQueueNotEmpty`, `KafkaConsumerLagHigh`: all loaded, `health=ok`, `inactive` |
| DLT query `sum by (topic) (max without (instance) (kafka_topic_partition_current_offset{topic=~".*\\.dlt"}))` | 4 series (`staff`, `patient`, `appointment`, `billing` `.dlt`), all 0 |
| Grafana `/api/dashboards/uid/medicore-overview` | Loaded, provisioned version 4, 25 panels including the 4 Billing panels, no provisioning errors |
| Kafka consumer groups (`kafka-consumer-groups.sh --describe --all-groups --state`) | `medicore-billing` **Stable** (1 member); `medicore-billing-notifications` **Stable** (2 members: main + retry consumer); `medicore-patient` and `medicore-appointment` Stable |
| SCRUM-179, MailHog | Registering a patient produced **"Welcome to MediCore, …"** in MailHog (`http://localhost:8025`) within a second. `patient.registered` → Billing notification consumer → SMTP |
| SCRUM-179, dead-letter alert | No new rule needed: `KafkaDeadLetterQueueNotEmpty` already matches `patient-events.dlt` and the other notification DLTs. Proven to fire in the resilience demo, step 4 (`scripts/demo/resilience-demo.ps1`) |

## Production SMTP

MailHog exists only in compose. Production Billing needs the `Smtp__*` settings for a Mailtrap test inbox (plan Appendix A.3). Without them, every notification fails, retries 3 times and lands on a `.dlt` topic; invoicing is unaffected. This is set in the end-of-sprint deploy window.

## Screenshots to take (local stack)

| File | What |
|---|---|
| `169-grafana-dashboard-loaded.png` | `http://localhost:3000` → *MediCore — Overview*, with all five health tiles UP and the Billing column visible |
| `169-prometheus-targets.png` | `http://localhost:9090/targets`, filtered to `billing-api` |
| `169-kafka-ui-billing-groups.png` | `http://localhost:8080` (Kafka UI) → Consumers: `medicore-billing` and `medicore-billing-notifications` both Stable |
| `169-grafana-lag-panel.png` | The *Kafka Consumer Lag per Group* panel |
| `169-servicedown-firing.png` | `http://localhost:9090/alerts` with `ServiceDown` **firing**. Produced by step 2 of the resilience demo, or `docker compose stop identity-api` and wait about 90 s |
| `179-mailhog-welcome-email.png` | `http://localhost:8025` showing a *Welcome to MediCore* email |
| `179-dlt-alert-firing.png` | `KafkaDeadLetterQueueNotEmpty` firing for `patient-events.dlt` (resilience demo, step 4) |
