# SCRUM-152 — Production topics (Azure Event Hubs)

| | |
|---|---|
| Date | 2026-10-10 |
| Broker | Azure Event Hubs namespace `medicore-eventhub` (Standard, 1 TU, Kafka endpoint, SASL_SSL) in `medicore-rg` |
| Approved by | Basidh (DevOps), option A in [`00-azure-recon.md`](00-azure-recon.md) §4 |

## Why the layout differs from local compose

Locally, `infra/kafka/create-topics.sh` creates 12 topics: 4 main topics, each with a `.retry` and a `.dlt` companion. Event Hubs Standard allows **at most 10 event hubs per namespace** ([Microsoft quotas](https://learn.microsoft.com/en-us/azure/event-hubs/event-hubs-quotas)), so production holds the 10 topics the code actually uses:

- `staff-events.retry` and `staff-events.dlt` were **removed**. Nothing produces to or subscribes to them:
  - Appointment's staff consumer retries in place.
  - Billing's notification consumer covers only `patient-`, `appointment-` and `billing-events`.
- The five missing topics were **created**. Billing needs all of them, and Patient dead-letters to `appointment-events.dlt`.

## Changes applied

| Action | Event hub | Partitions | Retention |
|---|---|---|---|
| created | `appointment-events.retry` | 1 | 168 h |
| created | `appointment-events.dlt` | 1 | 168 h |
| created | `billing-events` | 3 | 168 h |
| deleted | `staff-events.retry` | — | — |
| deleted | `staff-events.dlt` | — | — |
| created | `billing-events.retry` | 1 | 168 h |
| created | `billing-events.dlt` | 1 | 168 h |

Commands used (`az` 2.91.0):

```bash
az eventhubs eventhub create -g medicore-rg --namespace-name medicore-eventhub \
  -n <name> --partition-count <1|3> --retention-time-in-hours 168 --cleanup-policy Delete
az eventhubs eventhub delete -g medicore-rg --namespace-name medicore-eventhub -n <name>
```

## Final state (`az eventhubs eventhub list`)

| Event hub | Partitions | Retention (h) | Producer | Consumers |
|---|---|---|---|---|
| `staff-events` | 3 | 7 | Identity | Appointment |
| `patient-events` | 3 | 7 | Patient | Billing notifications |
| `patient-events.retry` | 1 | 7 | Billing notifications | Billing notifications |
| `patient-events.dlt` | 1 | 7 | Billing notifications | — (alerting/evidence) |
| `appointment-events` | 1 | 168 | Appointment | Patient, Billing invoicing, Billing notifications |
| `appointment-events.retry` | 1 | 168 | Billing notifications | Billing notifications |
| `appointment-events.dlt` | 1 | 168 | Patient, Billing notifications | — |
| `billing-events` | 3 | 168 | Billing | Billing notifications |
| `billing-events.retry` | 1 | 168 | Billing notifications | Billing notifications |
| `billing-events.dlt` | 1 | 168 | Billing notifications | — |

## Accepted gaps

- **`appointment-events` has 1 partition, not 3.** Standard tier can't change a hub's partition count after creation. Per-appointment ordering still holds, because the key is the appointment ID; only consumer parallelism is lower. Recreating the hub would lose its retained events, so it was left as is.
- **The four older hubs keep events for 7 hours**, which looks like a days-vs-hours slip when they were created. Raising them to 168 h is a non-destructive `az eventhubs eventhub update --retention-time-in-hours 168` and is listed as a follow-up.
- **Local and production layouts differ** (12 vs 10 topics). That's harmless: the two extra local topics are unused.
