# ADR-001 — PostgreSQL Hosting for Sprint 1

| Field       | Value |
|-------------|-------|
| **Status**  | Accepted |
| **Date**    | 2026-08-30 |
| **Deciders**| DevOps (Sprint 1) |
| **Story**   | Scrum 20 — First Deployment |

---

## Context

MediCore needs a relational database accessible from:
1. The local development docker-compose stack (Scrum 18)
2. The Azure-hosted Identity service App Service container (Scrum 20)

Four schemas (`medicore_identity`, `medicore_patient`, `medicore_appointment`, `medicore_billing`) with role-based isolation are already defined in `infra/postgres/init/01-init-db.sql`.

We need a Postgres host for the **production (Sprint 1) deployment** that is:
- Accessible from Azure App Service
- Cheap enough for a student/Sprint 1 budget
- Managed (no VM administration)
- Easy to upgrade as load grows

---

## Decision

**Use Azure Database for PostgreSQL Flexible Server — Burstable B1ms tier.**

Connection string format:
```
Host=<server>.postgres.database.azure.com;Port=5432;
Database=medicore;
Username=medicore_admin@<server>;
Password=<from-keyvault>;
SslMode=Require;
```

The server name, admin user, and password are stored as **Azure Key Vault secrets** and injected into the App Service as environment variables at deploy time. They are **never committed to Git**.

---

## Alternatives Considered

| Option | Cost/month | Pros | Cons | Decision |
|--------|-----------|------|------|----------|
| **Azure Flexible Server B1ms** | ~$15 | Managed, auto-backups, private networking | Not free | ✅ **Chosen** |
| Azure Flexible Server Free Trial | $0 for 12 months | Free | Only one server per subscription, limited resources | Acceptable if budget is zero |
| Neon (serverless Postgres) | Free tier available | Serverless, no idle cost | Cold-start latency, third-party | Revisit at Sprint 3 |
| Supabase | Free tier available | Postgres + Auth + Storage | Built-in auth conflicts with our Identity service | Rejected |
| Self-hosted on Azure VM | ~$7 B1ls VM | Cheapest | No managed backups, patching overhead, student risk | Rejected |
| docker-compose Postgres on App Service | $0 extra | Simple | No persistent disk on App Service, data loss on restart | Rejected |

---

## Consequences

### Positive
- Managed service: automatic backups, point-in-time restore, patching handled by Azure
- Private endpoint can be added in Sprint 3 to prevent public internet access to the DB
- Straightforward migration path: change SKU to General Purpose when load grows
- Supports SSL enforcement (`SslMode=Require`) matching our security posture

### Negative / Risks
- Monthly cost (~$15) even when idle — acceptable for Sprint 1 academic project
- Connection string must be treated as a secret (stored in GitHub Secrets and Azure Key Vault)
- Azure Flexible Server does not support `SUPERUSER` — the `medicore_admin` role must be granted via Azure portal or `az postgres flexible-server` commands

---

## Migration Path

| Sprint | Action |
|--------|--------|
| Sprint 1 | Burstable B1ms, public endpoint with firewall rule |
| Sprint 3 | Add VNet integration + private endpoint; remove public access |
| Sprint 5 | Evaluate upgrade to General Purpose D2s_v3 if load requires it |

---

## Provisioning Commands (Sprint 1)

See full instructions in [`docs/setup/CI-CD-SETUP.md`](setup/CI-CD-SETUP.md).

Quick reference:
```bash
# Create the Flexible Server
az postgres flexible-server create \
  --resource-group medicore-rg \
  --name medicore-postgres-slit \
  --location southeastasia \
  --admin-user medicore_admin \
  --admin-password "<strong-password>" \
  --sku-name Standard_B1ms \
  --tier Burstable \
  --version 16 \
  --storage-size 32 \
  --yes

# Allow Azure services to connect
az postgres flexible-server firewall-rule create \
  --resource-group medicore-rg \
  --name medicore-postgres-slit \
  --rule-name AllowAzureServices \
  --start-ip-address 0.0.0.0 \
  --end-ip-address 0.0.0.0
```
