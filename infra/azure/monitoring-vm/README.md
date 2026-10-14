# Production monitoring on an Azure VM (Prometheus + Grafana)

Production metrics run on the VM `medicore-monitoring`. It's the same Prometheus alert rules and Grafana dashboard as the local `docker-compose.yml` (shipped from `infra/prometheus/rules/` and `infra/grafana/provisioning/`), with a production scrape config. Grafana is published over HTTPS; nothing else on the VM is reachable from the internet.

```
                               Internet
                                  │ 443 (80 → redirect, ACME)
                                  ▼
medicore-vnet 10.20.0.0/16    monitoring-subnet 10.20.3.0/24   medicore-monitoring-nsg: allow TCP 80, 443
                                VM medicore-monitoring  10.20.3.10  (Ubuntu 24.04, 2 vCPU B-series)
                                  caddy ──► grafana ──► prometheus ──► kafka-exporter
                                                           │                │
           https://medicore-{gateway,identity,patient,     │                │ 9092, private
           appointment,billing}.azurewebsites.net/metrics ◄┘                ▼
                                                            kafka-subnet 10.20.1.0/24
                                                              VM medicore-kafka 10.20.1.10
                                                              medicore-kafka-nsg: + allow 9092 from 10.20.3.0/24
```

| File | Purpose |
|---|---|
| `provision.sh` | Creates the subnet, NSG, Kafka NSG rule and VM (cloud-init installs Docker and a 2 GB swap file), then runs `deploy.sh`. Idempotent |
| `deploy.sh` | Bundles the config below, unpacks it into `/opt/medicore-monitoring` with `az vm run-command`, and (re)starts the stack. Run it after every config change |
| `docker-compose.yml` | The stack on the VM: Prometheus, kafka-exporter, Grafana, Caddy |
| `prometheus.yml` | Production scrape config: the 5 apps over HTTPS, and kafka-exporter |
| `Caddyfile` | TLS (Let's Encrypt) and security headers in front of Grafana |
| `check.sh` | Containers, scrape-target health and active alerts (via run-command), plus the public endpoint and its certificate |
| `../../prometheus/rules/*.yml` | `ServiceDown`, `KafkaDeadLetterQueueNotEmpty`, `KafkaConsumerLagHigh`, shared with local |
| `../../grafana/provisioning/` | The datasource and the *MediCore — Overview* dashboard, shared with local |

Job names match the local `infra/prometheus/prometheus.yml`, so the rules and dashboard work unchanged. Locally each job has two targets (container and `host.docker.internal`); here it has one. `max by (job)` and `max without (instance)` handle both.

## Security

- **Only Caddy listens publicly**, on 80 and 443. Prometheus (9090), kafka-exporter (9308) and Grafana (3000) publish no host port and are reachable only on the VM's Docker network.
- **HTTPS with a real certificate.** Caddy obtains and renews a Let's Encrypt certificate for the public IP's Azure DNS name (`medicore-monitoring.centralindia.cloudapp.azure.com`) and redirects HTTP to HTTPS. It sends HSTS, `nosniff` and a referrer policy, and drops the `Server` header.
- **Grafana login required.** Anonymous access and sign-up are off, cookies are `Secure`, and Grafana's built-in brute-force login protection is on (its default). The admin password is generated on the VM; see "Grafana login".
- **Why 80/443 are open to the whole internet:** Let's Encrypt validates from addresses that aren't published, so an IP allowlist breaks certificate issuance and renewal. `GRAFANA_SOURCES` can narrow the rule, but then the certificate must come from elsewhere.
- **No SSH.** The VM is managed with `az vm run-command`, the same as the Kafka VM.
- **Kafka stays private.** kafka-exporter reaches `10.20.1.10:9092` over the VNet. The Kafka NSG gains one rule, `allow-kafka-from-monitoring` (priority 110, source `10.20.3.0/24`), ahead of its `deny-kafka-other` (200).
- **Scrapes are read-only HTTPS GETs** to the apps' public hostnames. App Service VNet integration is outbound only, so there's no private inbound path to the apps on this plan.

## Provision

```bash
infra/azure/monitoring-vm/provision.sh   # ~5 min for the VM, 2–5 min for cloud-init, then deploy.sh
infra/azure/monitoring-vm/check.sh       # expect 4 containers Up, 7 targets up, no alerts firing
```

Then open `https://medicore-monitoring.centralindia.cloudapp.azure.com` and sign in (see "Grafana login"). The dashboard is under **Dashboards → MediCore — Overview**. Alert rules, firing or not, are under **Alerting → Alert rules → Data source-managed**.

Nothing changes on the App Service apps: they already expose `/metrics`.

## Update the config

Edit `prometheus.yml`, `Caddyfile`, `docker-compose.yml`, or the shared rules or dashboard, then:

```bash
infra/azure/monitoring-vm/deploy.sh
```

It replaces the config, runs `docker compose up -d`, reloads Prometheus (SIGHUP) and Caddy, and leaves the data volumes alone. Grafana re-reads dashboards within 30 s.

## Grafana login

User `admin`. The password is generated on the VM on the first `deploy.sh` and stored in `/opt/medicore-monitoring/.env` (mode 600). It's never printed by the scripts. Read it yourself; don't paste it into chats, screenshots or evidence files:

```bash
az vm run-command invoke -g medicore-rg -n medicore-monitoring --command-id RunShellScript \
  --scripts "grep GRAFANA_ADMIN_PASSWORD /opt/medicore-monitoring/.env" --query "value[0].message" -o tsv
```

Grafana applies that variable only when it first creates its database. After that, the password lives in the `grafana-data` volume, and editing `.env` does nothing. To reset it:

```bash
az vm run-command invoke -g medicore-rg -n medicore-monitoring --command-id RunShellScript \
  --scripts "docker exec medicore-grafana grafana cli admin reset-admin-password '<new password>'" \
  --query "value[0].message" -o tsv
```

## Operations

```bash
# restart the stack
az vm run-command invoke -g medicore-rg -n medicore-monitoring --command-id RunShellScript \
  --scripts "cd /opt/medicore-monitoring && docker compose restart" --query "value[0].message" -o tsv

# a container's log (last 100 lines)
az vm run-command invoke -g medicore-rg -n medicore-monitoring --command-id RunShellScript \
  --scripts "docker logs --tail 100 medicore-prometheus" --query "value[0].message" -o tsv

# stop the VM between demos (no compute cost while deallocated; metrics stop being collected)
az vm deallocate -g medicore-rg -n medicore-monitoring
az vm start      -g medicore-rg -n medicore-monitoring   # the stack starts on boot
```

Containers restart on boot (`restart: unless-stopped`, and Docker is enabled under systemd). Metrics (15 days or 8 GB), Grafana state and the certificates live in Docker volumes on the managed disk, so they survive restarts.

The public IP is Standard SKU and static, so the DNS name and certificate stay valid across deallocation.

## Limits (accepted)

- **No alert delivery.** There's no Alertmanager, locally or here. Alerts are evaluated and shown in Grafana (and by `check.sh`), but nobody is notified. Adding Alertmanager with an email or webhook receiver is the next step if that's wanted.
- **`/metrics` on the apps is public.** All five apps serve it unauthenticated on their `azurewebsites.net` hostnames. That was already true before this VM; restricting it (a scrape token, or App Service access restrictions plus private endpoints) needs an app-code or plan change.
- **Single VM.** If it's down, production keeps running unmonitored; nothing in the request path depends on it.
- **Scrape granularity** is 30 s with a 20 s timeout, because B1 apps take up to about 11 s to answer `/metrics` on a cold start. A side effect is that the apps never idle out.
- **Logs aren't collected.** Seq runs in the local stack only.
