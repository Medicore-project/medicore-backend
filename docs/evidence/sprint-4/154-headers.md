# SCRUM-154 — HTTPS and security headers

Generated 2026-10-10 04:17 UTC by `scripts/verify/security-headers.sh`, probing `/health/live`.

| Target | HTTP→HTTPS | HSTS | nosniff | X-Frame-Options DENY | CSP | Referrer-Policy | HTTPS status |
|---|---|---|---|---|---|---|---|
| gateway | ❌ 200 | ✅ | ✅ | ✅ | ✅ | ✅ | 200 |
| gateway-proxied-patient | ❌ 200 | ✅ | ✅ | ✅ | ✅ | ✅ | 200 |
| identity | ❌ 200 | ❌ | ❌ | ❌ | ❌ | ❌ | 200 |
| patient | ❌ 200 | ❌ | ❌ | ❌ | ❌ | ❌ | 200 |
| appointment | ❌ 200 | ❌ | ❌ | ❌ | ❌ | ❌ | 200 |
| billing | ❌ 000 | ❌ | ❌ | ❌ | ❌ | ❌ | 000 |

## Raw values

### gateway

- HTTP probe: `http://medicore-gateway.azurewebsites.net/health/live` → 200
- HTTPS probe: `https://medicore-gateway.azurewebsites.net/health/live` → 200
- Strict-Transport-Security: `max-age=63072000; includeSubDomains; preload`
- Content-Security-Policy: `default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; font-src 'self'; object-src 'none'; frame-ancestors 'none'`
- Referrer-Policy: `no-referrer`

### gateway-proxied-patient

- HTTP probe: `http://medicore-gateway.azurewebsites.net/patient/health/live` → 200
- HTTPS probe: `https://medicore-gateway.azurewebsites.net/patient/health/live` → 200
- Strict-Transport-Security: `max-age=63072000; includeSubDomains; preload`
- Content-Security-Policy: `default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; font-src 'self'; object-src 'none'; frame-ancestors 'none'`
- Referrer-Policy: `no-referrer`

### identity

- HTTP probe: `http://medicore-identity.azurewebsites.net/health/live` → 200
- HTTPS probe: `https://medicore-identity.azurewebsites.net/health/live` → 200
- Strict-Transport-Security: `—`
- Content-Security-Policy: `—`
- Referrer-Policy: `—`

### patient

- HTTP probe: `http://medicore-patient.azurewebsites.net/health/live` → 200
- HTTPS probe: `https://medicore-patient.azurewebsites.net/health/live` → 200
- Strict-Transport-Security: `—`
- Content-Security-Policy: `—`
- Referrer-Policy: `—`

### appointment

- HTTP probe: `http://medicore-appointment.azurewebsites.net/health/live` → 200
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
| Gateway headers | **Pass**: HSTS (2 years, `includeSubDomains; preload`), `nosniff`, `X-Frame-Options: DENY`, a strict CSP, `Referrer-Policy: no-referrer`, `Permissions-Policy` | `SecurityHeadersMiddleware` adds them to every gateway response, proxied ones included (row `gateway-proxied-patient`). HSTS is skipped only in Development, and production has no `ASPNETCORE_ENVIRONMENT`, so it runs as Production | — |
| Service headers | **Fail** on identity, patient and appointment when called directly | Only the gateway has the middleware. The services are designed to sit behind it, but App Service gives each one a public `*.azurewebsites.net` hostname | **Accepted for submission.** Clients and the frontend only use the gateway. The robust fix is App Service *access restrictions* that admit only the gateway's outbound IPs; that's an Azure change, and the CI health loops would then need to probe through the gateway. Future work |
| HTTP→HTTPS | **Fail** everywhere: plain HTTP returns 200 | *HTTPS Only* is off on every Web App (recon R12). The containers listen on HTTP 8080 behind App Service's TLS front end, so the app can't see the original scheme to redirect itself | `az webapp update -g medicore-rg -n <app> --https-only true` for all five apps. Azure change, scheduled for the end-of-sprint deploy window. Re-run this script afterwards; the HTTP column should show 301 |
| billing | Not deployed yet | Billing Web App is created in the deploy window (plan §4.4) | Re-run after deployment |

Re-run after the deploy window:

```bash
scripts/verify/security-headers.sh > docs/evidence/sprint-4/154-headers.md
```
