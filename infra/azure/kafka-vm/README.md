# Production Kafka on an Azure VM

Production runs Apache Kafka 3.8 (KRaft, single node) on the VM `medicore-kafka`. It's the same image, KRaft configuration and topic script as the local `docker-compose.yml`. The App Service apps reach it privately through VNet integration. The broker port isn't reachable from the internet.

```
medicore-plan (B1)                               medicore-vnet 10.20.0.0/16
 identity, patient, appointment, billing ──VNet──► appsvc-subnet 10.20.2.0/26 (delegated to App Service)
                                          integration       │
                                                            │  medicore-kafka-nsg:
                                                            │    allow TCP 9092 from 10.20.2.0/26
                                                            │    deny  TCP 9092 from anything else
                                                            ▼
                                             kafka-subnet 10.20.1.0/24
                                               VM medicore-kafka  10.20.1.10  (Ubuntu 24.04, 2 vCPU / 4 GB B-series)
                                                 Docker: apache/kafka:3.8.0 + kafka-init (12 topics)
```

| File | Purpose |
|---|---|
| `provision.sh` | Creates the VNet, subnets, NSG and VM (cloud-init installs Docker and starts the broker), and integrates the apps with the VNet. Idempotent |
| `docker-compose.yml` | The broker definition used on the VM (`/opt/medicore-kafka`) |
| `check.sh` | Containers, topics and consumer-group lag, via `az vm run-command` (no SSH) |
| `cutover.sh` | Points the apps at `10.20.1.10:9092` and removes the SASL settings |
| `../../kafka/create-topics.sh` | Topic layout, shared with local compose: 4 main topics × 3 partitions, plus `.retry`/`.dlt` × 1 partition, 7-day retention |

## Security

- **No public broker port.** The NSG admits 9092 only from the App Service integration subnet and, for kafka-exporter, the monitoring subnet `10.20.3.0/24` (rule `allow-kafka-from-monitoring`, added by `../monitoring-vm/provision.sh`). Azure's default `DenyAllInBound` blocks everything else from the internet, SSH included.
- **No SSH.** The VM is managed with `az vm run-command`, which needs Azure RBAC on the VM and opens no port. An SSH key is generated at creation only because Azure requires one for Linux VMs.
- **The public IP is for outbound only**: apt packages and the Docker Hub image pull. Nothing listens on it.
- **PLAINTEXT on the private network.** Traffic between the apps and the broker stays inside the VNet. The services switch to SASL_SSL automatically if `Kafka__SaslUsername`/`Kafka__SaslPassword` are set, so adding TLS later is a broker-side change plus two settings, with no code change.
- **Auto topic creation is off.** Only `kafka-init` creates topics.

## Provision

```bash
infra/azure/kafka-vm/provision.sh        # ~5 min; cloud-init then needs another 2–5 min
infra/azure/kafka-vm/check.sh            # expect medicore-kafka Up (healthy), kafka-init Exited (0), 12 topics
```

## Cutover

```bash
infra/azure/kafka-vm/cutover.sh          # all four services; each restarts (2–8 min)
for a in identity patient appointment billing; do echo "$a: $(curl -s -m 60 https://medicore-$a.azurewebsites.net/health)"; done
infra/azure/kafka-vm/check.sh            # consumer groups appear once the services are up
```

Expected consumer groups: `medicore-patient`, `medicore-appointment` (staff events), `medicore-billing` and `medicore-billing-notifications`.

End-to-end check: register a new patient, then book an appointment. `appointment-events` gains messages, and Billing creates a draft invoice.

Events that were still unread on the previous broker at cutover aren't carried over. The outbox pattern means any event not yet published goes to the new broker.

## Rollback (to the previous broker)

Settings only, no code. Restore the three Kafka settings that the services had before. Take the values from the previous broker's configuration, never from this file. Then restart.

## Operations

```bash
# restart the broker
az vm run-command invoke -g medicore-rg -n medicore-kafka --command-id RunShellScript \
  --scripts "cd /opt/medicore-kafka && docker compose restart kafka" --query "value[0].message" -o tsv

# broker log (last 100 lines)
az vm run-command invoke -g medicore-rg -n medicore-kafka --command-id RunShellScript \
  --scripts "docker logs --tail 100 medicore-kafka" --query "value[0].message" -o tsv
```

The VM restarts the containers on boot (`restart: unless-stopped`, and Docker is enabled under systemd). Data lives in the `kafka-data` Docker volume on the VM's managed disk, so it survives restarts.

Topic offsets, consumer lag and DLT depth are collected by kafka-exporter on the monitoring VM, and shown in production Grafana. See [`../monitoring-vm/`](../monitoring-vm/README.md).

## Limits (accepted)

- **Single node** (replication factor 1). A VM outage pauses messaging. Producers keep working, because events wait in each service's outbox and publish when the broker returns. That's the behaviour the resilience demo shows locally.
