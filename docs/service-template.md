# MediCore Service Template

A guide for creating a new microservice by cloning the Identity service structure. Follow this exactly so all services are consistent and the grader can see the same pattern across all four.

---

## 1. Project Structure

Every service follows the same four-project layout:

```
src/services/<service-name>/
├── MediCore.<Service>.Api/           # HTTP layer: controllers, middleware, Program.cs
├── MediCore.<Service>.Application/   # Business logic: entities, interfaces, services, validators
├── MediCore.<Service>.Infrastructure/# EF Core, Kafka, outbox, repositories, reporting
└── MediCore.<Service>.Tests/         # xUnit: unit + integration tests (same project, filtered by trait)
```

**Hard rules:**
- No project references between services
- No shared `DbContext`
- `MediCore.Contracts` is the only shared project — event DTOs only

---

## 2. Creating a New Service

```bash
# From repo root
cd src/services

# Create the four projects
dotnet new webapi  -n MediCore.<Service>.Api           -o <service>/MediCore.<Service>.Api
dotnet new classlib -n MediCore.<Service>.Application  -o <service>/MediCore.<Service>.Application
dotnet new classlib -n MediCore.<Service>.Infrastructure -o <service>/MediCore.<Service>.Infrastructure
dotnet new xunit   -n MediCore.<Service>.Tests         -o <service>/MediCore.<Service>.Tests

# Wire project references
dotnet add <service>/MediCore.<Service>.Api/Api.csproj           reference <service>/MediCore.<Service>.Application/Application.csproj
dotnet add <service>/MediCore.<Service>.Api/Api.csproj           reference <service>/MediCore.<Service>.Infrastructure/Infrastructure.csproj
dotnet add <service>/MediCore.<Service>.Infrastructure/Infrastructure.csproj reference <service>/MediCore.<Service>.Application/Application.csproj
dotnet add <service>/MediCore.<Service>.Tests/Tests.csproj       reference <service>/MediCore.<Service>.Application/Application.csproj

# Add to solution
dotnet sln ../../MediCore.slnx add <service>/MediCore.<Service>.Api/Api.csproj
dotnet sln ../../MediCore.slnx add <service>/MediCore.<Service>.Application/Application.csproj
dotnet sln ../../MediCore.slnx add <service>/MediCore.<Service>.Infrastructure/Infrastructure.csproj
dotnet sln ../../MediCore.slnx add <service>/MediCore.<Service>.Tests/Tests.csproj
```

---

## 3. Required NuGet Packages

### Api project
```bash
dotnet add package Serilog.AspNetCore
dotnet add package Serilog.Sinks.Seq
dotnet add package prometheus-net.AspNetCore
dotnet add package Microsoft.AspNetCore.Diagnostics.HealthChecks
dotnet add package AspNetCore.HealthChecks.NpgSql
dotnet add package Swashbuckle.AspNetCore
dotnet add package FluentValidation.AspNetCore
```

### Infrastructure project
```bash
dotnet add package Microsoft.EntityFrameworkCore
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add package Microsoft.EntityFrameworkCore.Design
dotnet add package Confluent.Kafka
```

### Tests project
```bash
dotnet add package coverlet.collector
dotnet add package Microsoft.NET.Test.Sdk
dotnet add package xunit
dotnet add package xunit.runner.visualstudio
```

---

## 4. Environment Variables / Configuration

Copy `.env.example` to `.env` (gitignored) and fill in your local values. Each service reads from `appsettings.json` and environment variables override at runtime.

Minimum required keys per service:

| Key | Description |
|---|---|
| `ConnectionStrings:<Service>Database` | PostgreSQL connection string for this service's schema |
| `Kafka:BootstrapServers` | `localhost:9092` locally |
| `Jwt:Secret` | Shared with the API Gateway (Identity only issues, gateway validates) |
| `Jwt:Issuer` | `medicore-identity` |
| `Jwt:Audience` | `medicore-api` |

---

## 5. PostgreSQL — One Instance, Four Schemas

The Docker Compose stack runs a **single** PostgreSQL instance. Each service owns one schema and one login role with no cross-schema grants:

| Service | Schema | Login role |
|---|---|---|
| Identity | `medicore_identity` | `identity_svc` |
| Patient | `medicore_patient` | `patient_svc` |
| Appointment | `medicore_appointment` | `appointment_svc` |
| Billing | `medicore_billing` | `billing_svc` |
| Tests | `medicore_test` | `identity_svc` (reused for now) |

Schemas are created by `scripts/init-db.sql` on first `docker compose up`.

---

## 6. EF Core Migrations

Each service manages its own migrations inside its Infrastructure project.

```bash
# From repo root — add a migration
dotnet ef migrations add <MigrationName> \
  --project src/services/<service>/MediCore.<Service>.Infrastructure/Infrastructure.csproj \
  --startup-project src/services/<service>/MediCore.<Service>.Api/Api.csproj \
  --context <Service>DbContext \
  --output-dir Persistence/Migrations

# Apply to local database
dotnet ef database update \
  --project src/services/<service>/MediCore.<Service>.Infrastructure/Infrastructure.csproj \
  --startup-project src/services/<service>/MediCore.<Service>.Api/Api.csproj \
  --context <Service>DbContext
```

**DbContext rules:**
- Bind to the service's schema: `modelBuilder.HasDefaultSchema("medicore_<service>");`
- Soft delete via `IsDeleted` + global query filter on every entity that supports it
- `CreatedAt`, `UpdatedAt`, `CreatedBy` on every entity, set in `SaveChangesAsync` override
- `AsNoTracking()` on all read-only queries

---

## 7. Kafka and the Outbox Pattern

Events are **never published directly** inside an HTTP request. The flow is:

```
HTTP Request
    │
    ▼
Application service
    │
    ▼
EF Core transaction
    ├── Modify business entity
    └── INSERT OutboxMessage { Topic, EventKey, EventType, Payload }
              │
              ▼
        Transaction commits
              │
              ▼
        OutboxProcessor (BackgroundService)
              │
              ▼
           Kafka
```

Copy these files from the Identity service and update namespaces:

| File | Purpose |
|---|---|
| `Application/Entities/OutBoxMessage.cs` | Outbox entity (note capital B) |
| `Application/Interfaces/IOutboxMessageRepository.cs` | Repository interface |
| `Application/Interfaces/IKafkaEventPublisher.cs` | Publisher interface |
| `Infrastructure/Messaging/KafkaEventPublisher.cs` | Confluent.Kafka producer |
| `Infrastructure/Messaging/OutboxProcessor.cs` | Background drain loop |
| `Infrastructure/Persistence/Repositories/OutboxMessageRepository.cs` | EF repository |
| `Infrastructure/Persistence/Configurations/OutboxMessageConfiguration.cs` | EF table config |

**EventKey** is the aggregate ID (e.g. `staffId.ToString()`) — this is the Kafka partition key, ensuring all events for the same entity land on the same partition in order.

---

## 8. Kafka Topics

All topics are created by `kafka-init` on `docker compose up`. Do not create topics in application code.

| Topic | Partitions | Publisher | Key |
|---|---|---|---|
| `staff-events` | 3 | Identity | `staffId` |
| `patient-events` | 3 | Patient | `patientId` |
| `appointment-events` | 3 | Appointment | `appointmentId` |
| `billing-events` | 3 | Billing | `invoiceId` |

Each topic has `.retry` and `.dlt` companions for failed consumer handling.

---

## 9. Health and Metrics Endpoints

Every service **must** expose these three endpoints. Copy from `Program.cs` in Identity:

| Endpoint | Purpose |
|---|---|
| `GET /health` | All health checks (Postgres + self) |
| `GET /health/live` | Liveness only (no DB check) |
| `GET /metrics` | Prometheus scrape target |

---

## 10. Logging — Serilog + Correlation ID

Every log line must carry a `CorrelationId`. Copy `Middleware/CorrelationIdMiddleware.cs` from Identity — it reads `X-Correlation-Id` from the incoming request header or generates a new GUID, attaches it to `ILogger` enrichment, and writes it back to the response header.

```csharp
// Register in Program.cs — must come before UseSerilogRequestLogging
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
```

For Seq (local): logs appear at `http://localhost:5341`. Filter by `CorrelationId` to trace a request across services.

---

## 11. Dynamic Reports — Raw ADO.NET

The report endpoint for each service **must** use raw ADO.NET (`NpgsqlCommand`), not EF Core LINQ. This satisfies the brief's "direct SQL" requirement and is faster for aggregations.

Pattern:
```csharp
// Infrastructure/Reporting/<Service>ReportQuery.cs
var sql = new StringBuilder("SELECT ... FROM ... WHERE 1=1");

if (filter.HasValue)
{
    sql.Append(" AND column = @param");
    cmd.Parameters.AddWithValue("@param", filter.Value);
}

// NEVER concatenate values into the SQL string — SQL injection is a graded failure
```

Use `_dbContext.Database.GetDbConnection()` to get the `NpgsqlConnection` from the EF context without opening a second connection pool.

---

## 12. Testing

One xUnit project holds **both** unit and integration tests. Integration tests are marked with a trait so they can be skipped locally without Docker:

```csharp
[Trait("Category", "Integration")]
public class MyIntegrationTest { ... }
```

Run only unit tests:
```bash
dotnet test --filter "Category!=Integration"
```

Run everything (requires `docker compose up`):
```bash
dotnet test
```

Coverage report:
```bash
dotnet test --collect:"XPlat Code Coverage"
reportgenerator -reports:"**/coverage.cobertura.xml" -targetdir:"coverage-report" -reporttypes:Html
```

---

## 13. Running Locally (Full Stack)

```bash
# 1. Start infrastructure
cp .env.example .env        # fill in values — never commit .env
docker compose up -d

# 2. Apply migrations for your service
dotnet ef database update \
  --project src/services/identity/MediCore.Identity.Infrastructure/Infrastructure.csproj \
  --startup-project src/services/identity/MediCore.Identity.Api/Api.csproj \
  --context IdentityDbContext

# 3. Run the service
dotnet run --project src/services/identity/MediCore.Identity.Api/Api.csproj
```

| Service | Local URL |
|---|---|
| Identity API + Swagger | http://localhost:5001/swagger |
| Kafka UI | http://localhost:8080 |
| Seq (structured logs) | http://localhost:5341 |
| Grafana | http://localhost:3000 |
| MailHog | http://localhost:8025 |
| Prometheus | http://localhost:9090 |

---

## 14. Dockerfile

Copy the Identity service Dockerfile and update the project name. Multi-stage build, non-root user. Image is published to GHCR by CI.

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
# ... restore, build, publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser
USER appuser
# ... copy publish output, EXPOSE 8080, ENTRYPOINT
```
