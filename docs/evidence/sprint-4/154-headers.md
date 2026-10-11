# SCRUM-154 — HTTPS and security headers

Generated 2026-10-10 05:43 UTC by `scripts/verify/security-headers.sh`, probing `/health/live`.

| Target | HTTP→HTTPS | HSTS | nosniff | X-Frame-Options DENY | CSP | Referrer-Policy | HTTPS status |
|---|---|---|---|---|---|---|---|
| gateway | ✅ 301 | ✅ | ✅ | ✅ | ✅ | ✅ | 200 |
| gateway-proxied-patient | ✅ 301 | ✅ | ✅ | ✅ | ✅ | ✅ | 200 |
| identity | ✅ 301 | ❌ | ❌ | ❌ | ❌ | ❌ | 200 |
| patient | ✅ 301 | ❌ | ❌ | ❌ | ❌ | ❌ | 200 |
| appointment | ✅ 301 | ❌ | ❌ | ❌ | ❌ | ❌ | 200 |
| billing | ❌ 000 | ❌ | ❌ | ❌ | ❌ | ❌ | 000 |

## Raw values

### gateway

- HTTP probe: `http://medicore-gateway.azurewebsites.net/health/live` → 301 → `https://medicore-gateway.azurewebsites.net/health/live`
- HTTPS probe: `https://medicore-gateway.azurewebsites.net/health/live` → 200
- Strict-Transport-Security: `max-age=63072000; includeSubDomains; preload`
- Content-Security-Policy: `default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; font-src 'self'; object-src 'none'; frame-ancestors 'none'`
- Referrer-Policy: `no-referrer`

### gateway-proxied-patient

- HTTP probe: `http://medicore-gateway.azurewebsites.net/patient/health/live` → 301 → `https://medicore-gateway.azurewebsites.net/patient/health/live`
- HTTPS probe: `https://medicore-gateway.azurewebsites.net/patient/health/live` → 200
- Strict-Transport-Security: `max-age=63072000; includeSubDomains; preload`
- Content-Security-Policy: `default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; font-src 'self'; object-src 'none'; frame-ancestors 'none'`
- Referrer-Policy: `no-referrer`

### identity

- HTTP probe: `http://medicore-identity.azurewebsites.net/health/live` → 301 → `https://medicore-identity.azurewebsites.net/health/live`
- HTTPS probe: `https://medicore-identity.azurewebsites.net/health/live` → 200
- Strict-Transport-Security: `—`
- Content-Security-Policy: `—`
- Referrer-Policy: `—`

### patient

- HTTP probe: `http://medicore-patient.azurewebsites.net/health/live` → 301 → `https://medicore-patient.azurewebsites.net/health/live`
- HTTPS probe: `https://medicore-patient.azurewebsites.net/health/live` → 200
- Strict-Transport-Security: `—`
- Content-Security-Policy: `—`
- Referrer-Policy: `—`

### appointment

- HTTP probe: `http://medicore-appointment.azurewebsites.net/health/live` → 301 → `https://medicore-appointment.azurewebsites.net/health/live`
- HTTPS probe: `https://medicore-appointment.azurewebsites.net/health/live` → 200
- Strict-Transport-Security: `—`
- Content-Security-Policy: `—`
- Referrer-Policy: `—`

### billing

- HTTP probe: `http://medicore-billing.azurewebsites.net/health/live` → 000
- HTTPS probe: `https://medicore-billing.azurewebsites.net/health/live` → 000
- Strict-Transport-Security: `—`
- Content-Security-Policy: `—`
- Referrer-Policy: `—`


## Analysis

| Area | Result | Why | Remediation |
|---|---|---|---|
| HTTP→HTTPS | **Pass** on all four deployed apps (301 to `https://`) | *HTTPS Only* enabled on 2026-10-10. Before that, plain HTTP returned 200 everywhere (recon R12; first run of this script, same day) | — |
| Gateway headers | **Pass**: HSTS (2 years, `includeSubDomains; preload`), `nosniff`, `X-Frame-Options: DENY`, a strict CSP, `Referrer-Policy: no-referrer`, `Permissions-Policy` | `SecurityHeadersMiddleware` adds them to every gateway response, proxied ones included (row `gateway-proxied-patient`). HSTS is skipped only in Development, and production has no `ASPNETCORE_ENVIRONMENT`, so it runs as Production | — |
| Service headers | **Fail** on identity, patient and appointment when called directly | Only the gateway has the middleware. The services are designed to sit behind it, but App Service gives each one a public `*.azurewebsites.net` hostname | **Accepted for submission.** Clients and the frontend only use the gateway. The robust fix is App Service *access restrictions* that admit only the gateway's outbound IPs; that's an Azure change, and the CI health loops would then need to probe through the gateway. Future work |
| billing | Not deployed yet | The Billing Web App is created in the deploy window (plan §4.4) | Re-run after deployment |

Also applied on 2026-10-10 to identity, patient, appointment and gateway:
- **Always On** (no idle unload, so the Kafka consumers keep running);
- a **health check path** of `/health/live`, so App Service replaces an unhealthy instance.

All four answered `/health/live` 200 afterwards, and the gateway's `/health/services` reported identity, patient and appointment Healthy.
