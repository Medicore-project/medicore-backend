#!/usr/bin/env bash
# =============================================================================
# SCRUM-154 — HTTPS redirect, HSTS and security-header check
# =============================================================================
# For each target, checks:
#   1. plain HTTP redirects to HTTPS (301/302/307/308 with an https:// Location)
#   2. the HTTPS response carries Strict-Transport-Security, X-Content-Type-Options:
#      nosniff, X-Frame-Options: DENY, Content-Security-Policy and Referrer-Policy
#
# Prints a Markdown report on stdout; exit code 1 if any check fails.
#
# Usage:
#   scripts/verify/security-headers.sh > docs/evidence/sprint-4/154-headers.md
#   TARGETS="gateway=https://localhost:5000" scripts/verify/security-headers.sh
#
# TARGETS is a space-separated list of name=https-base-url. The probe path is
# /health/live, which every service and the gateway expose anonymously.
# =============================================================================
set -uo pipefail

TARGETS="${TARGETS:-gateway=https://medicore-gateway.azurewebsites.net \
gateway-proxied-patient=https://medicore-gateway.azurewebsites.net/patient \
identity=https://medicore-identity.azurewebsites.net \
patient=https://medicore-patient.azurewebsites.net \
appointment=https://medicore-appointment.azurewebsites.net \
billing=https://medicore-billing.azurewebsites.net}"
PROBE_PATH="${PROBE_PATH:-/health/live}"
TIMEOUT="${TIMEOUT:-30}"

FAILED=0
pass() { printf '✅'; }
fail() { printf '❌'; FAILED=1; }

# header <headers-text> <name>  → value (case-insensitive name match), empty if absent
header() {
  printf '%s\n' "$1" | tr -d '\r' | awk -v n="$(printf '%s' "$2" | tr '[:upper:]' '[:lower:]')" '
    { split($0, a, ":"); if (tolower(a[1]) == n) { sub(/^[^:]*:[ \t]*/, ""); print; exit } }'
}

echo "# SCRUM-154 — HTTPS and security headers"
echo
echo "Generated $(date -u '+%Y-%m-%d %H:%M UTC') by \`scripts/verify/security-headers.sh\`, probing \`${PROBE_PATH}\`."
echo
echo "| Target | HTTP→HTTPS | HSTS | nosniff | X-Frame-Options DENY | CSP | Referrer-Policy | HTTPS status |"
echo "|---|---|---|---|---|---|---|---|"

DETAILS=""
for entry in $TARGETS; do
  name="${entry%%=*}"
  base="${entry#*=}"
  http_url="http://${base#https://}${PROBE_PATH}"
  https_url="${base}${PROBE_PATH}"

  # 1. HTTP → HTTPS redirect (do not follow)
  redirect=$(curl -s -o /dev/null -m "$TIMEOUT" -w '%{http_code} %{redirect_url}' "$http_url" 2>/dev/null)
  redirect=${redirect:-000}
  code="${redirect%% *}"
  location="${redirect#* }"

  # 2. HTTPS response headers (GET, headers only on stdout)
  headers=$(curl -s -D - -o /dev/null -m "$TIMEOUT" "$https_url" 2>/dev/null)
  status=$(printf '%s\n' "$headers" | head -1 | tr -d '\r' | awk '{print $2}')
  status=${status:-000}

  hsts=$(header "$headers" "Strict-Transport-Security")
  xcto=$(header "$headers" "X-Content-Type-Options")
  xfo=$(header "$headers" "X-Frame-Options")
  csp=$(header "$headers" "Content-Security-Policy")
  refp=$(header "$headers" "Referrer-Policy")

  printf '| %s | ' "$name"
  if [[ "$code" =~ ^30[1278]$ && "$location" == https://* ]]; then pass; else fail; fi; printf ' %s | ' "$code"
  if [[ -n "$hsts" ]]; then pass; else fail; fi; printf ' | '
  if [[ "${xcto,,}" == "nosniff" ]]; then pass; else fail; fi; printf ' | '
  if [[ "${xfo^^}" == "DENY" ]]; then pass; else fail; fi; printf ' | '
  if [[ -n "$csp" ]]; then pass; else fail; fi; printf ' | '
  if [[ -n "$refp" ]]; then pass; else fail; fi; printf ' | %s |\n' "$status"

  DETAILS+=$'\n'"### ${name}"$'\n\n'"- HTTP probe: \`${http_url}\` → ${code}${location:+ → \`${location}\`}"$'\n'
  DETAILS+="- HTTPS probe: \`${https_url}\` → ${status}"$'\n'
  DETAILS+="- Strict-Transport-Security: \`${hsts:-—}\`"$'\n'
  DETAILS+="- Content-Security-Policy: \`${csp:-—}\`"$'\n'
  DETAILS+="- Referrer-Policy: \`${refp:-—}\`"$'\n'
done

echo
echo "## Raw values"
printf '%s\n' "$DETAILS"
exit $FAILED
