# MediCore — Backend

The backend for **MediCore**, a hospital management system built for the SE3022 group project. It's a set of .NET 8 microservices behind a YARP API gateway. The services share one PostgreSQL instance, each in its own schema, and talk to each other through Kafka events.

The React frontend lives in a separate repository, [`medicore-frontend`](../medicore-frontend). It calls this backend only through the gateway.

---

## Services

| Service | What it owns | `dotnet run` port | Gateway prefix | DB schema |
|---|---|---|---|---|
| **Gateway** (`src/gateway`) | Routing, CORS, rate limiting, security headers | 5000 | — | — |
| **Identity** | Staff, roles, departments, specializations, login and JWTs, audit reports | 5267 | `/identity`, `/auth`, `/api/staff`, … | `medicore_identity` |
| **Patient** | Patients, medical records, prescriptions, allergies, demographics reports | 5002 | `/patient` | `medicore_patient` |
| **Appointment** | Doctor schedules, slots, holidays, leave, bookings, waitlist, utilisation reports | 5003 | `/appointment` | `medicore_appointment` |
| **Billing** | Service tariffs, invoices, notifications, revenue and outstanding reports | 5004 | `/billing` | `medicore_billing` |

The gateway **strips the prefix** before forwarding. For example, a controller at `api/patients` is reached at `/patient/api/patients`. The gateway proxies and rate-limits, but it does **not** check tokens: each service validates JWTs itself, so an `[AllowAnonymous]` endpoint is public through the gateway.

```
                    ┌──────────────────────────────┐
  Browser ─────────►│  Gateway (YARP)  :5000        │
                    └──┬────────┬────────┬────────┬┘
                       ▼        ▼        ▼        ▼
                   identity  patient  appointment billing
                       │        │        │        │
                       ├────────┴────────┴────────┤
                       ▼                          ▼
                 PostgreSQL 16              Kafka 3.8 (KRaft)
          (one schema + role per service)   staff/patient/appointment/billing-events
```

## Tech stack

- **.NET 8** (`net8.0`) and ASP.NET Core Web API, with EF Core 8 and Npgsql
- **YARP** for the gateway
- **PostgreSQL 16**
- **Apache Kafka 3.8** in KRaft mode, accessed with Confluent.Kafka
- **FluentValidation**, **Serilog** → Seq, **prometheus-net** → Prometheus/Grafana
- **xUnit** for tests

---

## Getting started

### Prerequisites

- .NET 8 SDK. A newer SDK also works, but every project pins `net8.0`.
- Docker Desktop
- `dotnet-ef` 8.x for migrations: `dotnet tool install --global dotnet-ef --version 8.*`

### 1. Configure the environment

```bash
cp .env.example .env
```

The defaults work for local development. **Never commit `.env`.**

### 2. Start everything in Docker

```bash
docker compose up -d --build
```

This starts the infrastructure and all five apps. On the first start, `infra/postgres/init/01-init-db.sql` creates the schemas and login roles, and the `kafka-init` container creates the topics. Each service applies its own EF migrations when it starts.

Only the gateway is published to the host, at **http://localhost:5000**. The services themselves are reachable only inside the Docker network.

Always pass `--build` after pulling new code. A container running an old image is the most common cause of a confusing local error.

### 3. Or run a single service from source

Start the infrastructure in Docker, then run the service you are working on:

```bash
docker compose up -d postgres kafka kafka-init seq mailhog
dotnet run --project src/services/patient/MediCore.Patient.Api/Api.csproj
```

Swagger is at `http://localhost:<port>/swagger`, using the ports in the table above.

### Local tools

| Tool | URL |
|---|---|
| Gateway | http://localhost:5000 |
| Kafka UI | http://localhost:8080 |
| Seq (logs) | http://localhost:5341 |
| Prometheus | http://localhost:9090 |
| Grafana | http://localhost:3000 (`admin` / `admin` by default) |
| MailHog (outgoing email) | http://localhost:8025 |

### First login

Migrations seed the five roles: `Admin`, `Doctor`, `Nurse`, `Receptionist` and `Patient`. **No users are seeded**, so a fresh database needs an Admin account before anyone can sign in. Once one exists, `scripts/seed-local-demo.ps1` fills the local stack with demo data through the gateway:

```powershell
./scripts/seed-local-demo.ps1 -AdminEmail admin@medicore.local -AdminPassword '<password>'
```

---

## Common commands

```bash
dotnet build MediCore.slnx

# Unit tests only: no database or Kafka needed
dotnet test MediCore.slnx --filter "Category!=Integration"

# Everything, including integration tests (needs `docker compose up`)
dotnet test MediCore.slnx

# One test class
dotnet test src/services/patient/MediCore.Patient.Tests/Tests.csproj \
  --filter "FullyQualifiedName~PatientRegistrationServiceTests"
```

The solution file is **`MediCore.slnx`** (the XML solution format), not a `.sln`.

### EF Core migrations

Migrations are per service and live in that service's Infrastructure project. Both `--context` and `--output-dir` are required:

```bash
dotnet ef migrations add <Name> \
  --project src/services/<service>/MediCore.<Service>.Infrastructure/Infrastructure.csproj \
  --startup-project src/services/<service>/MediCore.<Service>.Api/Api.csproj \
  --context <Service>DbContext \
  --output-dir Persistence/Migrations
```

---

## Repository layout

```
MediCore.slnx
docker-compose.yml             local stack: infrastructure + all five apps
docker-compose.loadtest.yml    SCRUM-35 load-test override only; never for development
.env.example                   copy to .env

src/
  gateway/MediCore.Gateway/    YARP routes in appsettings.json, CORS, rate limiting
  services/<service>/
    MediCore.<Service>.Api/             controllers, authorization policies, middleware, Program.cs
    MediCore.<Service>.Application/     entities, DTOs, interfaces, services, validators
    MediCore.<Service>.Infrastructure/  DbContext, EF configurations, repositories, migrations,
                                        Kafka messaging, ADO.NET report queries
    MediCore.<Service>.Tests/           xUnit unit and integration tests
    Dockerfile
  shared/MediCore.Contracts/   Kafka event DTOs — the only project shared between services

infra/
  postgres/init/               creates schemas and roles on first start
  kafka/create-topics.sh       topic bootstrap (also used in production)
  prometheus/, grafana/        local monitoring config, alert rules, dashboards
  azure/kafka-vm/              production Kafka VM: provision, cutover, health check
  azure/monitoring-vm/         production Prometheus + Grafana VM
  docker/                      shared Docker build helpers

scripts/
  seed-local-demo.ps1          demo data for the local stack
  demo/resilience-demo.ps1     Kafka retry / dead-letter demo
  verify/                      RBAC matrix and security-header checks

docs/
  service-template.md          architecture guide — read before adding a service
  adr/                         architecture decision records
  setup/CI-CD-SETUP.md         GitHub Actions and Azure setup
  demo/, evidence/             demo runbooks and sprint evidence
```

Two things surprise newcomers:

- **Entities live in `Application`, not `Infrastructure`.** The EF configurations in Infrastructure reference them.
- **Project files have short names**, like `Api.csproj` and `Tests.csproj`, while the assemblies and namespaces use the full `MediCore.<Service>.<Layer>`.

Services never reference each other's projects. If one service needs another's data, it subscribes to that service's events and keeps a local copy. Changing `MediCore.Contracts` affects every service, so treat it as a cross-team change.

---

## Architecture and conventions

[`docs/service-template.md`](docs/service-template.md) is the full architecture guide. The main points:

### Data

- Each service has one PostgreSQL schema and one login role, with no cross-schema access.
- Tables are `snake_case` plural, while columns stay PascalCase.
- **Rows are soft-deleted everywhere** (`IsDeleted` plus a global query filter). For that reason, unique indexes are partial (`WHERE "IsDeleted" = false`).
- Each entity has a surrogate `Id` and a separate business-key GUID. The API exposes only the business key.
- `CreatedAt`/`CreatedBy` and `UpdatedAt`/`UpdatedBy` are stamped in `SaveChangesAsync`.

### Events: the transactional outbox

HTTP requests never publish to Kafka directly. The service writes the business change and an `OutboxMessage` row in **one transaction**. A background `OutboxProcessor` then sends those rows to Kafka.

The event key is the aggregate ID, so all events for one entity land on the same partition, in order. Consumers deduplicate through a `ProcessedMessage` table. A failed message is retried through `<topic>.retry` and parked in `<topic>.dlt`.

Topics are created by `infra/kafka/create-topics.sh`. **Application code never creates topics.**

### Application code

- Status fields are `string` constants, not C# enums.
- Services return discriminated-union result records. Controllers only map each result to an HTTP status, so business logic never lives in controllers or repositories.
- Validation is explicit: inject `IValidator<T>` and return `ValidationProblemDetails`.
- Report endpoints use raw ADO.NET (`NpgsqlCommand`) with bound parameters, not EF LINQ. This is a project requirement.
- `TimeProvider` is injected, so tests can control "now".

### Tests

- One xUnit project per service. Integration tests carry `[Trait("Category", "Integration")]`.
- **No Moq, no FluentAssertions, no EF InMemory.** Collaborators are hand-written fakes.
- Test names are `Snake_case_sentences`.
- **Exception — SE3112 unit-testing suite:** `src/services/appointment/MediCore.Appointment.UnitTests` deliberately uses **Moq** for test doubles and xUnit `[Theory]` parameterization, because demonstrating mocking frameworks is the point of that assessment. It references only the Application layer. See its [README](src/services/appointment/MediCore.Appointment.UnitTests/README.md).

### Operations

Every service exposes `/health`, `/health/live` (no database check) and `/metrics`. Each request carries an `X-Correlation-Id`, which flows into the logs in Seq.

---

## CI/CD

Each service has its own workflow in `.github/workflows/`: `ci-identity.yml`, `ci-patient.yml`, `ci-appointment.yml`, `ci-billing.yml` and `ci-gateway.yml`. Each one runs on pushes and pull requests to `main` and `develop` that change that service or `src/shared/`.

1. **Build and test**: Release build and the service's test project, with coverage
2. **Security scan**: Gitleaks, CodeQL and a NuGet vulnerability audit
3. **Docker publish** (pushes only): the image goes to `ghcr.io`, with an SBOM and a Trivy scan
4. **Deploy** (pushes to `main` only): the image is deployed to its Azure App Service

`security-full.yml` runs a scheduled full scan: Gitleaks, CodeQL, NuGet audit, Trivy on every image, and an OWASP ZAP scan.

A new service isn't built or tested in CI until it has its own workflow file.


### SE3112 testing pipeline

`.github/workflows/se3112-pipeline.yml` runs every stage below on pushes and pull requests to `main` and `develop`, and ends with one **Pipeline Report** table of all results on the run page.

| Stage | Owner | Workflow | Run locally |
|---|---|---|---|
| Unit testing & mocking (Appointment) | Nizam T.H.B | `unit-tests-appointment.yml` (also `workflow_call`) | `src/services/appointment/MediCore.Appointment.UnitTests/run-tests.ps1` |
| Code quality & metrics (Patient) | W.A.B.B.Wijesinghe | `code-quality-patient.yml` (also `workflow_call`) | `scripts/quality/scan-patient.ps1 -Token <sonarqube-token>` (or `-SkipSonar` without SonarQube) |
| Load & performance testing (k6) | Samarakoon S.M.T.C | `load-testing.yml` (also `workflow_call`) | `./tests/load/run-demo.sh public` |
| UI / E2E testing (Playwright) | Chamika Kasthuriarachchi | `ui-e2e-tests.yml` in [`medicore-frontend`](https://github.com/Medicore-project/medicore-frontend) (also `workflow_call`) | In `medicore-frontend/ui-tests`:see its [README](https://github.com/Medicore-project/medicore-frontend/blob/main/ui-tests/README.md)) |

## Production (Azure)

| Component | Where it runs |
|---|---|
| Gateway and four services | Azure App Service (Linux containers) on one B1 plan |
| PostgreSQL | Azure Database for PostgreSQL Flexible Server (see [ADR-001](docs/adr/ADR-001-postgres-hosting.md)) |
| Kafka | Self-hosted VM `medicore-kafka`, reached privately through VNet integration ([`infra/azure/kafka-vm`](infra/azure/kafka-vm/README.md)) |
| Prometheus + Grafana | Self-hosted VM `medicore-monitoring` behind Caddy and HTTPS ([`infra/azure/monitoring-vm`](infra/azure/monitoring-vm/README.md)) |
| Frontend | Azure Static Web Apps (deployed from the frontend repo) |

[`docs/setup/CI-CD-SETUP.md`](docs/setup/CI-CD-SETUP.md) lists the secrets and steps for setting up the deployment.

If the frontend shows **"Network Error"** in production, first check that the App Services are running: a stopped app answers `403 Site Disabled`, which the browser reports as a network failure.

---

## Troubleshooting

| Symptom | Cause |
|---|---|
| Migrations fail with `28P01: password authentication failed` | A locally installed PostgreSQL is using port 5432 instead of the Docker container. Stop the local one. |
| An endpoint you just added returns 404 in Docker | The container is running an old image. Run `docker compose up -d --build <service>-api`. |
| The booking page lists no doctors | The appointment service's doctor cache is empty. Call `POST /identity/api/staff/doctors/republish` once as Admin. |
| Browser calls fail a CORS preflight | The frontend is calling a service directly. Only the gateway (`:5000`) has CORS configured. |
| One test fails locally on Windows but passes in CI | `DemographicsReportQueryTests` compares SQL containing `\n`, while Windows generates `\r\n`. This is known and harmless. |

## Contributing

- Work happens on feature branches named after the Jira ticket. Changes reach `develop` and then `main` through pull requests.
- Commits follow Conventional Commits: `feat(SCRUM-32): …`, `fix: …`, `chore: …` and `refactor(<area>): …`.
