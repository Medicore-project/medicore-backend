# MediCore — Backend Monorepo

> **Sprint 1 · Scrum 18–20**  
> Local dev stack + API Gateway + CI/CD pipeline with security scanning and first Azure deployment.

[![CI — Identity](https://github.com/chamika-kasthuri09/medicore-backend/actions/workflows/ci-identity.yml/badge.svg)](https://github.com/chamika-kasthuri09/medicore-backend/actions/workflows/ci-identity.yml)
[![CI — Gateway](https://github.com/chamika-kasthuri09/medicore-backend/actions/workflows/ci-gateway.yml/badge.svg)](https://github.com/chamika-kasthuri09/medicore-backend/actions/workflows/ci-gateway.yml)
[![Security Scan](https://github.com/chamika-kasthuri09/medicore-backend/actions/workflows/security-full.yml/badge.svg)](https://github.com/chamika-kasthuri09/medicore-backend/actions/workflows/security-full.yml)

---

## Table of Contents

1. [Architecture Overview](#1-architecture-overview)
2. [Prerequisites](#2-prerequisites)
3. [Quick Start](#3-quick-start)
4. [Infrastructure Stack](#4-infrastructure-stack)
5. [Service URLs](#5-service-urls)
6. [API Gateway](#6-api-gateway)
7. [CI/CD Pipeline](#7-cicd-pipeline)
8. [Deployment](#8-deployment)
9. [Kafka Topics](#9-kafka-topics)
10. [PostgreSQL Schemas](#10-postgresql-schemas)
11. [Running a Service Locally](#11-running-a-service-locally)
12. [Useful Commands](#12-useful-commands)
13. [Project Structure](#13-project-structure)
14. [Contributing](#14-contributing)

---

## 1. Architecture Overview

```
┌──────────────────────────────────────────────────────────────────────────┐
│  Client (Browser / Mobile)                                               │
└──────────────────────────────┬───────────────────────────────────────────┘
                               │ :5000
┌──────────────────────────────▼───────────────────────────────────────────┐
│  API Gateway (YARP)  — medicore-gateway                                  │
│  • Rate limiting  • CORS allowlist  • Security headers  • HTTPS redirect │
│  • Aggregated /health/services endpoint  • /metrics (Prometheus)         │
└──┬──────────────┬──────────────┬──────────────┬────────────────────────┘
   │ /identity/** │ /patient/**  │/appointment/ │ /billing/**
   │ /auth/**     │              │**            │
```

**Design decisions:**

| Decision | Rationale |
|---|---|
| **Single Postgres instance, four schemas** | Mirrors a shared-DB model common in small teams; each service has an isolated schema + login role so no cross-service SQL is possible |
| **Kafka in KRaft mode** | No ZooKeeper dependency; simpler, faster startup, matches Kafka 3.x+ production guidance |
| **Outbox pattern** | Events never leave the DB transaction boundary; guarantees at-least-once delivery without a distributed transaction |
| **host.docker.internal** in Prometheus | Services run with `dotnet run` on the host; Prometheus inside Docker reaches them via this alias |

---

## 2. Prerequisites

| Tool | Minimum version | Install |
|---|---|---|
| Docker Desktop | 4.x | https://docs.docker.com/get-docker/ |
| Docker Compose | v2 (plugin) | bundled with Docker Desktop |
| .NET SDK | 8.0 | https://dotnet.microsoft.com/download |
| Git | any | https://git-scm.com |

> **Windows users:** Enable WSL 2 backend in Docker Desktop settings.  
> **macOS Apple Silicon:** all images are multi-arch; no extra steps required.

---

## 3. Quick Start

```bash
# 1. Clone the repo
git clone <repo-url>
cd medicore-backend

# 2. Copy and review the environment file
cp .env.example .env
# Open .env — the defaults work out-of-the-box for local dev.
# Change JWT_SECRET to a random string (≥ 32 chars) before sharing the stack.

# 3. Start the full infrastructure stack
docker compose up -d

# 4. Verify everything is healthy
docker compose ps
```

Expected output after ~60 s:

```
NAME                     STATUS          PORTS
medicore-postgres        healthy         0.0.0.0:5432->5432/tcp
medicore-kafka           healthy         0.0.0.0:29092->29092/tcp
medicore-kafka-init      exited (0)
medicore-kafka-ui        running         0.0.0.0:8080->8080/tcp
medicore-seq             healthy         0.0.0.0:5341->80/tcp
medicore-prometheus      healthy         0.0.0.0:9090->9090/tcp
medicore-grafana         healthy         0.0.0.0:3000->3000/tcp
medicore-mailhog         healthy         0.0.0.0:1025->1025/tcp, 0.0.0.0:8025->8025/tcp
```

> `kafka-init` exits with code 0 after creating all topics — this is expected.

---

## 4. Infrastructure Stack

| Container | Image | Purpose |
|---|---|---|
| `medicore-postgres` | `postgres:16-alpine` | Primary data store; four schemas, four roles |
| `medicore-kafka` | `apache/kafka:3.8.0` | Event broker (KRaft — no ZooKeeper) |
| `medicore-kafka-init` | `apache/kafka:3.8.0` | One-shot topic creation; exits after run |
| `medicore-kafka-ui` | `provectuslabs/kafka-ui` | Topic/consumer-group browser |
| `medicore-seq` | `datalust/seq` | Structured log ingestion and search |
| `medicore-prometheus` | `prom/prometheus` | Metrics scraper |
| `medicore-grafana` | `grafana/grafana-oss` | Dashboards and alerting |
| `medicore-mailhog` | `mailhog/mailhog` | Local SMTP server and email inspector |

### Healthchecks

Every container has a healthcheck. Dependent services (e.g. `kafka-init` waits for `kafka` to be `healthy`) will not start until their dependencies pass. This eliminates "race condition" startup failures.

---

## 5. Service URLs

| Service | URL | Credentials |
|---|---|---|
| **API Gateway** | http://localhost:5000 | — |
| **Kafka UI** | http://localhost:8080 | — |
| **Seq** (logs) | http://localhost:5341 | — (open in dev) |
| **Grafana** | http://localhost:3000 | admin / admin |
| **Prometheus** | http://localhost:9090 | — |
| **MailHog UI** | http://localhost:8025 | — |
| **MailHog SMTP** | localhost:1025 | — |
| **PostgreSQL** | localhost:5432 | see `.env` |

### dotnet service ports (run locally with `dotnet run`)

| Service | Direct Swagger | Via Gateway |
|---|---|---|
| Gateway | http://localhost:5000/health/services | — |
| Identity | http://localhost:5001/swagger | http://localhost:5000/identity/swagger |
| Patient | http://localhost:5002/swagger | http://localhost:5000/patient/swagger |
| Appointment | http://localhost:5003/swagger | http://localhost:5000/appointment/swagger |
| Billing | http://localhost:5004/swagger | http://localhost:5000/billing/swagger |

---

## 6. API Gateway

> **Source:** [`src/gateway/MediCore.Gateway/`](src/gateway/MediCore.Gateway/)
> **Port:** 5000 (HTTP) · 5443 (HTTPS)

The gateway is a YARP reverse proxy that is the **only** entry point for all client traffic. The frontend never calls a service directly.

### Routing

| Path prefix | Upstream service | Status |
|---|---|---|
| `/identity/**` | Identity (`:5001`) | ✅ Live |
| `/auth/**` | Identity (`:5001`) | ✅ Live |
| `/patient/**` | Patient (`:5002`) | ⏳ Stubbed (503 until implemented) |
| `/appointment/**` | Appointment (`:5003`) | ⏳ Stubbed |
| `/billing/**` | Billing (`:5004`) | ⏳ Stubbed |

YARP strips the prefix before forwarding — `GET /identity/api/staff` becomes `GET /api/staff` on the upstream.

### Rate Limiting

| Policy | Limit | Window | Applied to |
|---|---|---|---|
| `global` | 100 req | 60 s per IP | All routes |
| `auth-login` | 5 req | 60 s per IP | `POST /auth/login` only |

Over-limit requests receive `429 Too Many Requests` with a `Retry-After` header.

### CORS

Only origins in the `Cors:AllowedOrigins` config array are allowed. Any other origin receives no `Access-Control-Allow-Origin` header (the browser blocks the request).

Default allowlist (Development):
- `http://localhost:3000`
- `http://localhost:5173`

### Security Headers

Applied to **every** response by `SecurityHeadersMiddleware`:

| Header | Value |
|---|---|
| `X-Content-Type-Options` | `nosniff` |
| `X-Frame-Options` | `DENY` |
| `Content-Security-Policy` | `default-src 'self'; ...` |
| `Strict-Transport-Security` | `max-age=63072000; includeSubDomains; preload` (production only) |
| `Referrer-Policy` | `no-referrer` |
| `Permissions-Policy` | `camera=(), microphone=(), geolocation=(), payment=()` |

The `Server` and `X-Powered-By` headers are **stripped** to avoid version disclosure.

### Aggregated Health Endpoint

```bash
# Returns JSON with status of all four services
curl http://localhost:5000/health/services

# Gateway liveness (no upstream dependency)
curl http://localhost:5000/health/live
```

Stubbed services report `Degraded` (not `Unhealthy`) — the gateway stays `Healthy` even if Patient/Appointment/Billing are not yet running.

### QA Verification

```bash
# 1. Security headers present?
curl -sI http://localhost:5000/health/live | grep -E "X-Content|X-Frame|Content-Security|Referrer"

# 2. Blocked CORS origin (should see NO Access-Control-Allow-Origin)
curl -sI -H "Origin: http://evil.com" http://localhost:5000/health/live | grep -i access-control

# 3. Rate limit — fire 6 rapid requests to /auth/login (6th must return 429)
for i in {1..6}; do
  curl -s -o /dev/null -w "%{http_code}\n" -X POST http://localhost:5000/auth/login \
    -H "Content-Type: application/json" -d '{"email":"x","password":"y"}'
done

# 4. Prometheus metrics exposed?
curl -s http://localhost:5000/metrics | grep "http_requests_received_total" | head -3
```

### Running the Gateway Locally

```bash
# Ensure infrastructure is up first
docker compose up -d

# Start the gateway (reads appsettings.Development.json automatically)
dotnet run --project src/gateway/MediCore.Gateway/Gateway.csproj \
           --launch-profile Development
```

---

## 7. CI/CD Pipeline

The project uses GitHub Actions for CI/CD and security scanning. Workflows are path-filtered, meaning a change to the Billing service will not trigger the Identity service CI.

### Workflows

- **`ci-identity.yml`**: Builds, tests, security scans, pushes to GHCR, and deploys to Azure App Service (on `main`).
- **`ci-gateway.yml`**: Builds, tests, security scans, and pushes to GHCR (proves independent deployability).
- **`security-full.yml`**: Daily scheduled run covering Gitleaks, CodeQL, Trivy, and `dotnet list package --vulnerable` across the whole repository.

### GitHub Secrets

To deploy, the following secrets must be configured in the GitHub repository:

| Secret | Description |
|---|---|
| `AZURE_CREDENTIALS` | Service principal JSON for Azure deployment |
| `AZURE_WEBAPP_NAME` | The target App Service name (e.g., `medicore-identity`) |
| `AZURE_RESOURCE_GROUP` | The target resource group (e.g., `medicore-rg`) |
| `GHCR_PAT` | GitHub Personal Access Token (with `write:packages` scope) |

---

## 8. Deployment

The Identity service and API Gateway are packaged as Docker images and published to GitHub Container Registry (GHCR).

### Identity Service (Sprint 1)

The Identity service is currently deployed to Azure App Service (Basic B1 tier) with an Azure Database for PostgreSQL Flexible Server backend (see `docs/adr/ADR-001-postgres-hosting.md`).

- **Production URL**: `https://medicore-identity.azurewebsites.net`
- **Health Check**: `https://medicore-identity.azurewebsites.net/health/live`

### SecOps Evidence

Every CI run generates artifacts proving security compliance:
- Test coverage reports (Cobertura)
- CodeQL static analysis results (GitHub Security tab)
- Trivy container vulnerabilities (SARIF)
- Gitleaks secret scan reports

---

## 9. Kafka Topics

Topics are created automatically by `kafka-init` on first `docker compose up`. **Never create topics in application code.**

| Topic | Partitions | Publisher | Partition Key |
|---|---|---|---|
| `staff-events` | 3 | Identity | `staffId` |
| `patient-events` | 3 | Patient | `patientId` |
| `appointment-events` | 3 | Appointment | `appointmentId` |
| `billing-events` | 3 | Billing | `invoiceId` |

Each primary topic has two companions:

| Companion suffix | Partitions | Purpose |
|---|---|---|
| `.retry` | 1 | Transient-failure retry queue |
| `.dlt` | 1 | Dead-letter — messages that exhausted retries |

**Verify topics after startup:**

```bash
docker exec medicore-kafka kafka-topics.sh \
  --bootstrap-server localhost:9092 --list
```

---

## 10. PostgreSQL Schemas

A single Postgres instance (`medicore` database) hosts four isolated schemas. Each schema has a dedicated login role — cross-schema access is not granted.

| Service | Schema | Login role | Password (dev) |
|---|---|---|---|
| Identity | `medicore_identity` | `identity_svc` | `identity_pass` |
| Patient | `medicore_patient` | `patient_svc` | `patient_pass` |
| Appointment | `medicore_appointment` | `appointment_svc` | `appointment_pass` |
| Billing | `medicore_billing` | `billing_svc` | `billing_pass` |
| Tests | `medicore_test` | `identity_svc` | `identity_pass` |

**Connect via psql:**

```bash
# As admin
psql -h localhost -U medicore_admin -d medicore

# As a service role
psql -h localhost -U identity_svc -d medicore \
  --set=search_path=medicore_identity
```

**Inspect schemas:**

```bash
docker exec -it medicore-postgres \
  psql -U medicore_admin -d medicore -c '\dn'
```

---

## 11. Running a Service Locally

```bash
# 1. Ensure infrastructure is up
docker compose up -d

# 2. Apply EF Core migrations for the service
dotnet ef database update \
  --project src/services/identity/MediCore.Identity.Infrastructure/Infrastructure.csproj \
  --startup-project src/services/identity/MediCore.Identity.Api/Api.csproj \
  --context IdentityDbContext

# 3. Run the service (reads .env via launchSettings or dotnet-env)
dotnet run --project src/services/identity/MediCore.Identity.Api/Api.csproj \
  --launch-profile Development
```

> **Tip:** Configure `Properties/launchSettings.json` in each Api project to load the `.env` file automatically, or export variables from your shell:
> ```bash
> export $(grep -v '^#' .env | xargs)
> ```

---

## 12. Useful Commands

```bash
# ── Stack management ──────────────────────────────────────────────────
# Start all containers in the background
docker compose up -d

# Stop containers, keep data volumes
docker compose down

# Stop containers AND delete all data (clean slate)
docker compose down -v

# Rebuild after changing compose file
docker compose up -d --force-recreate

# ── Logs ─────────────────────────────────────────────────────────────
# Tail all logs
docker compose logs -f

# Tail a specific service
docker compose logs -f kafka

# ── Kafka ─────────────────────────────────────────────────────────────
# List topics
docker exec medicore-kafka kafka-topics.sh \
  --bootstrap-server localhost:9092 --list

# Consume from a topic (from the beginning)
docker exec -it medicore-kafka kafka-console-consumer.sh \
  --bootstrap-server localhost:9092 \
  --topic staff-events \
  --from-beginning

# Re-run topic creation (if kafka-init exited early)
docker compose run --rm kafka-init

# ── PostgreSQL ────────────────────────────────────────────────────────
# Open psql as admin
docker exec -it medicore-postgres \
  psql -U medicore_admin -d medicore

# Dump a schema
docker exec medicore-postgres \
  pg_dump -U medicore_admin -d medicore -n medicore_identity > identity-dump.sql

# ── Prometheus ────────────────────────────────────────────────────────
# Hot-reload config without restarting
curl -X POST http://localhost:9090/-/reload

# ── Tests ─────────────────────────────────────────────────────────────
# Unit tests only (no Docker required)
dotnet test --filter "Category!=Integration"

# All tests (requires docker compose up)
dotnet test

# Coverage report
dotnet test --collect:"XPlat Code Coverage"
reportgenerator -reports:"**/coverage.cobertura.xml" \
  -targetdir:"coverage-report" -reporttypes:Html
```

---

## 13. Project Structure

```
medicore-backend/
├── docker-compose.yml              ← Infrastructure stack (Scrum 18)
├── .env.example                    ← Copy to .env — never commit .env
├── MediCore.slnx                   ← Solution file
│
├── infra/                          ← Config files mounted into containers
│   ├── kafka/
│   │   └── create-topics.sh       ← Idempotent topic bootstrap script
│   ├── postgres/
│   │   └── init/
│   │       └── 01-init-db.sql     ← Schemas + roles created on first run
│   ├── prometheus/
│   │   └── prometheus.yml         ← Scrape targets for all four services
│   └── grafana/
│       └── provisioning/
│           ├── datasources/
│           │   └── prometheus.yml ← Auto-wires Prometheus datasource
│           └── dashboards/
│               └── provider.yml   ← Watches dashboards dir for JSON files
│
├── src/
│   ├── services/
│   │   └── identity/              ← Reference implementation
│   │       ├── Dockerfile
│   │       ├── MediCore.Identity.Api/
│   │       ├── MediCore.Identity.Application/
│   │       ├── MediCore.Identity.Infrastructure/
│   │       └── MediCore.Identity.Tests/
│   └── shared/
│       └── MediCore.Contracts/    ← Event DTOs only — no business logic
│
└── docs/
    └── service-template.md        ← How to create a new service
```

---

## 14. Contributing

1. **Never commit `.env`** — it is in `.gitignore`. Use `.env.example` for documentation.
2. **Never create Kafka topics in application code** — use `infra/kafka/create-topics.sh`.
3. **Never write cross-schema SQL** — each service talks only to its own schema via its own login role.
4. All PRs must pass `dotnet test --filter "Category!=Integration"` (unit tests only, no Docker required in CI).
5. Follow the patterns in `docs/service-template.md` exactly — the grader checks consistency.
