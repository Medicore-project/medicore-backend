# SCRUM-186 — Gateway routes for Billing

| | |
|---|---|
| Date | 2026-10-10 |
| Gateway | YARP, `src/gateway/MediCore.Gateway/appsettings.json` |
| Route | `billing-route`: `/billing/{**catch-all}` → `billingCluster`, prefix stripped (`PathPattern: /{**catch-all}`), rate-limit policy `global` |
| Production URL | `https://medicore-gateway.azurewebsites.net/billing/...` |

The Billing route already existed in `develop`, so this ticket is **verification and wiring**, not new routes.
- Billing paths all start with `/billing/`, so they can't collide with the gateway's `/api/reports/**` route, which goes to Identity.
- **The gateway doesn't validate JWTs** (finding F5). Billing validates every token itself, which is why the "no token" column below is 401 from Billing, not from the gateway.

## External paths and roles

Roles come from the controllers' `[Authorize]` attributes. When a class and a method both carry one, ASP.NET requires **both**, so the method-level `Admin` on tariff writes makes those Admin-only.

| Method | Gateway path | Billing path | Roles | No token |
|---|---|---|---|---|
| GET | `/billing/api/invoices/{invoiceId}` | `/api/invoices/{invoiceId}` | Admin, Receptionist | 401 |
| GET | `/billing/api/invoices/by-appointment/{appointmentId}` | `/api/invoices/by-appointment/{appointmentId}` | Admin, Receptionist | 401 |
| POST | `/billing/api/invoices/{invoiceId}/payments` | `/api/invoices/{invoiceId}/payments` | Admin, Receptionist | 401 |
| GET | `/billing/api/service-tariffs` | `/api/service-tariffs` | Admin, Receptionist | 401 |
| GET | `/billing/api/service-tariffs/{tariffId}` | `/api/service-tariffs/{tariffId}` | Admin, Receptionist | 401 |
| POST | `/billing/api/service-tariffs` | `/api/service-tariffs` | **Admin** | 401 |
| PUT | `/billing/api/service-tariffs/{tariffId}` | `/api/service-tariffs/{tariffId}` | **Admin** | 401 |
| DELETE | `/billing/api/service-tariffs/{tariffId}` | `/api/service-tariffs/{tariffId}` | **Admin** | 401 |
| GET | `/billing/api/notification-templates` | `/api/notification-templates` | Admin | 401 |
| GET | `/billing/api/notification-templates/{templateId}` | `/api/notification-templates/{templateId}` | Admin | 401 |
| POST | `/billing/api/notification-templates` | `/api/notification-templates` | Admin | 401 |
| PUT | `/billing/api/notification-templates/{templateId}` | `/api/notification-templates/{templateId}` | Admin | 401 |
| DELETE | `/billing/api/notification-templates/{templateId}` | `/api/notification-templates/{templateId}` | Admin | 401 |
| GET | `/billing/api/notifications` | `/api/notifications` | Admin, Receptionist | 401 |
| GET | `/billing/reports/revenue` | `/reports/revenue` (**no `api/`**) | Admin | 401 |
| GET | `/billing/reports/outstanding` | `/reports/outstanding` (**no `api/`**) | Admin | 401 |
| GET | `/billing/health/live` | `/health/live` | anonymous | 200 |
| GET | `/billing/metrics` | `/metrics` | anonymous | 200 |

Good RBAC rows for SCRUM-148:
- a **Receptionist** gets **403** on `/billing/reports/revenue` and on `POST /billing/api/service-tariffs`;
- a **Doctor** gets **403** on every Billing path.

## Frontend check

Checked against `medicore-frontend` `origin/develop` at `f665dd1`. **Every Billing call uses the gateway paths above.**

| Frontend module | Calls |
|---|---|
| `src/api/billing.ts` | `/billing/api/invoices/{id}`, `/by-appointment/{id}`, `POST /{id}/payments` |
| `src/api/serviceTariffs.ts` | `/billing/api/service-tariffs` (GET, POST, PUT, DELETE) |
| `src/api/notifications.ts` | `/billing/api/notification-templates` (GET, POST, PUT, DELETE), `/billing/api/notifications` |
| `src/api/revenueReports.ts` | `/billing/reports/revenue` |
| `src/api/outstandingReports.ts` | `/billing/reports/outstanding` |

The frontend's page guards are equal to or stricter than the API's:
- `/reports/revenue`, `/reports/outstanding` and `/billing/notifications` are Admin-only.
- The notifications page also edits templates, which are Admin-only, so it stays Admin-only even though the log API allows Receptionist.
- `/billing/tariffs` and `/billing/invoices/:id` allow the front-desk roles.

## Production wiring (gateway App Service settings)

At recon time (see [`00-azure-recon.md`](00-azure-recon.md)), the gateway had overrides for patient and appointment but **none for billing**. It still resolves the committed Docker hostname `http://billing-api:8080`, and `/health/services` reports billing `Degraded — Name or service not known (billing-api:8080)`.

Settings to add **after the Billing Web App passes its direct checks** (Appendix A.4). Not applied yet; each change restarts the gateway.

```bash
az webapp config appsettings set -g medicore-rg -n medicore-gateway --settings \
  ReverseProxy__Clusters__billingCluster__Destinations__billingPrimary__Address="https://medicore-billing.azurewebsites.net" \
  Services__Billing="https://medicore-billing.azurewebsites.net"
```

Verification after applying:

```bash
curl -s https://medicore-gateway.azurewebsites.net/health/services              # billing: Healthy
curl -s -o /dev/null -w "%{http_code}\n" \
  https://medicore-gateway.azurewebsites.net/billing/api/service-tariffs        # 401 without a token
curl -s -o /dev/null -w "%{http_code}\n" \
  https://medicore-gateway.azurewebsites.net/billing/health/live                # 200
```

## Local config fix (F15)

`src/gateway/MediCore.Gateway/appsettings.Development.json` named its clusters `identity-cluster`/`identity-primary` and so on. The routes reference `identityCluster`/`identityPrimary`, so the Development overrides created unused clusters, and `dotnet run` routed to the Docker hostnames from `appsettings.json` instead of `localhost:500x`. The ids are now camelCase, so local `dotnet run` routes to `localhost:5001`–`5004`.

## Not changed

- **Gateway JWT validation (F5)** is still absent. Adding it is gateway code, which the feature freeze rules out, so it's recorded as a SecOps triage item.
