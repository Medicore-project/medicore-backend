#!/usr/bin/env bash
# =============================================================================
# SCRUM-148 / 149 — RBAC matrix through the gateway, plus the audit window
# =============================================================================
# Logs in as one test user per staff role, gets a patient booking token, then
# calls every row below with each principal and with no token, comparing the
# status with the expected outcome:
#
#   allowed principal  → anything except 401/403 (200, or 400/404 for the
#                        deliberately empty/unknown-id probes below)
#   other principals   → 403
#   no token           → 401
#
# The probes never change data: writes send `{}` (rejected by validation once
# authorised) and ids are the all-zero GUID (404 once authorised).
# Authorization runs before validation, so a denied caller still gets 403.
#
# Credentials come from the environment only — nothing is hard-coded:
#   GATEWAY_URL            default http://localhost:5000
#   ADMIN_EMAIL ADMIN_PASSWORD
#   DOCTOR_EMAIL DOCTOR_PASSWORD
#   NURSE_EMAIL NURSE_PASSWORD
#   RECEPTIONIST_EMAIL RECEPTIONIST_PASSWORD
#   PATIENT_NUMBER PATIENT_DOB    (YYYY-MM-DD) for the booking token
#   AUDIT_OUT              optional path: write the SCRUM-149 audit report there
#
# A principal whose credentials are missing is skipped (column shows "—").
#
# Usage:
#   scripts/verify/rbac-matrix.sh > docs/evidence/sprint-4/148-rbac-matrix.md
# Exit code 1 if any cell differs from the expected outcome.
# =============================================================================
set -uo pipefail

GW="${GATEWAY_URL:-http://localhost:5000}"
Z="00000000-0000-0000-0000-000000000000"
TIMEOUT="${TIMEOUT:-30}"

# Rows: METHOD|PATH|BODY|ALLOWED|NOTE
#   ALLOWED is a set of A(dmin) D(octor) N(urse) R(eceptionist) P(atient booking token)
ROWS=(
  # ── Identity: staff, roles, reference data, audit ──
  "GET|/api/staff||ADNR|staff directory"
  "POST|/api/staff|{}|A|create staff (AdminOnly)"
  "GET|/api/roles||A|role list (AdminOnly)"
  "GET|/api/departments||AR|FrontDesk"
  "POST|/api/departments|{}|A|AdminOnly"
  "GET|/api/specializations||AR|FrontDesk"
  "GET|/api/reports/audit||A|audit report"
  # ── Patient: profile, clinical records, report ──
  "GET|/patient/api/patients/search?q=a||ADNR|PatientReader"
  "GET|/patient/api/patients/$Z||ADNR|PatientReader"
  "POST|/patient/api/patients|{}|AR|register (FrontDesk)"
  "PUT|/patient/api/patients/$Z|{}|AR|update profile (FrontDesk)"
  "GET|/patient/api/patients/$Z/records||ADNR|read clinical records"
  "POST|/patient/api/patients/$Z/records|{}|DN|write clinical record (ClinicalRecordWriter)"
  "GET|/patient/api/patients/$Z/allergies||ADNR|read allergies"
  "POST|/patient/api/patients/$Z/allergies|{}|DN|ClinicalRecordWriter"
  "GET|/patient/api/patients/$Z/prescriptions||ADNR|read prescriptions"
  "POST|/patient/api/patients/$Z/prescriptions|{}|DN|ClinicalRecordWriter"
  "GET|/patient/reports/demographics||A|demographics report"
  # ── Appointment: booking, scheduling, leave, report ──
  "GET|/appointment/api/appointments||ADNR|ScheduleReader"
  "POST|/appointment/api/appointments|{}|ARP|book (BookingCreator)"
  "GET|/appointment/api/appointments/mine||P|BookingHolder"
  "PUT|/appointment/api/appointments/$Z/complete|{}|D|AppointmentCompleter"
  "PUT|/appointment/api/appointments/$Z/no-show|{}|ADR|NoShowRecorder"
  "GET|/appointment/api/doctors||ADNR|ScheduleReader"
  "POST|/appointment/api/schedules|{}|AR|ScheduleManager"
  "POST|/appointment/api/holidays|{}|A|HolidayManager"
  "GET|/appointment/api/doctor-leaves/pending||A|LeaveApprover"
  "POST|/appointment/api/doctor-leaves|{}|D|LeaveManager"
  "GET|/appointment/api/waitlist/mine||P|BookingHolder"
  "GET|/appointment/reports/utilisation||A|ReportReader"
  # ── Billing ──
  "GET|/billing/api/invoices/$Z||AR|invoices"
  "POST|/billing/api/invoices/$Z/payments|{}|AR|record payment"
  "GET|/billing/api/service-tariffs||AR|tariff read"
  "POST|/billing/api/service-tariffs|{}|A|tariff write"
  "GET|/billing/api/notification-templates||A|templates"
  "GET|/billing/api/notifications||AR|notification log"
  "GET|/billing/reports/revenue||A|revenue report"
  "GET|/billing/reports/outstanding||A|outstanding report"
)

json_escape() { printf '%s' "$1" | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g'; }
json_field() { sed -n "s/.*\"$1\"[[:space:]]*:[[:space:]]*\"\([^\"]*\)\".*/\1/p" | head -1; }

login() {   # login <email> <password> → access token on stdout
  local body
  body=$(printf '{"email":"%s","password":"%s"}' "$(json_escape "$1")" "$(json_escape "$2")")
  curl -s -m "$TIMEOUT" -H "Content-Type: application/json" -d "$body" "$GW/auth/login" | json_field accessToken
}

declare -A TOKEN
for r in ADMIN DOCTOR NURSE RECEPTIONIST; do
  email_var="${r}_EMAIL"; pass_var="${r}_PASSWORD"
  if [[ -n "${!email_var:-}" && -n "${!pass_var:-}" ]]; then
    TOKEN[${r:0:1}]=$(login "${!email_var}" "${!pass_var}")
    [[ -z "${TOKEN[${r:0:1}]}" ]] && echo "warning: login failed for $r" >&2
  fi
done
if [[ -n "${PATIENT_NUMBER:-}" && -n "${PATIENT_DOB:-}" ]]; then
  body=$(printf '{"patientNumber":"%s","dateOfBirth":"%s"}' "$(json_escape "$PATIENT_NUMBER")" "$(json_escape "$PATIENT_DOB")")
  TOKEN[P]=$(curl -s -m "$TIMEOUT" -H "Content-Type: application/json" -d "$body" \
    "$GW/patient/api/patients/identify" | json_field bookingToken)
  [[ -z "${TOKEN[P]}" ]] && echo "warning: patient identify failed" >&2
fi

# The gateway's "global" rate limiter allows 100 requests per minute per IP; the
# matrix makes ~230. Pace the calls under the limit, and if a 429 still slips
# through, wait for the window to reset and retry so it never lands in a cell.
PACE_SECONDS="${PACE_SECONDS:-0.7}"

call() {    # call <method> <path> <body> <token> → status code
  local args=(-s -o /dev/null -m "$TIMEOUT" -w '%{http_code}' -X "$1")
  [[ -n "$3" ]] && args+=(-H "Content-Type: application/json" -d "$3")
  [[ -n "$4" ]] && args+=(-H "Authorization: Bearer $4")
  local code attempt
  for attempt in 1 2 3 4 5; do
    sleep "$PACE_SECONDS"
    code=$(curl "${args[@]}" "$GW$2" 2>/dev/null)
    [[ "$code" != "429" ]] && break
    echo "rate limited on $1 $2, waiting 20 s (attempt $attempt)" >&2
    sleep 20
  done
  echo "${code:-000}"
}

START_UTC=$(date -u '+%Y-%m-%dT%H:%M:%SZ')
PASSES=0; FAILS=0
TABLE=""
for row in "${ROWS[@]}"; do
  IFS='|' read -r method path body allowed note <<< "$row"
  line="| \`$method ${path//$Z/{id\}}\` | $note |"
  for p in A D N R P; do
    if [[ -z "${TOKEN[$p]:-}" ]]; then line+=" — |"; continue; fi
    code=$(call "$method" "$path" "$body" "${TOKEN[$p]}")
    if [[ "$allowed" == *"$p"* ]]; then expected="allow"; else expected="403"; fi
    if [[ "$expected" == "allow" && "$code" != "401" && "$code" != "403" && "$code" != "000" ]] \
       || [[ "$expected" == "403" && "$code" == "403" ]]; then
      line+=" ✅ $code |"; PASSES=$((PASSES + 1))
    else
      line+=" ❌ $code (exp $expected) |"; FAILS=$((FAILS + 1))
    fi
  done
  code=$(call "$method" "$path" "$body" "")
  if [[ "$code" == "401" ]]; then line+=" ✅ 401 |"; PASSES=$((PASSES + 1))
  else line+=" ❌ $code (exp 401) |"; FAILS=$((FAILS + 1)); fi
  TABLE+="$line"$'\n'
done
END_UTC=$(date -u '+%Y-%m-%dT%H:%M:%SZ')

echo "# SCRUM-148 — RBAC matrix"
echo
echo "Generated by \`scripts/verify/rbac-matrix.sh\` against \`$GW\`, $START_UTC → $END_UTC."
echo
echo "Expected: an allowed principal gets anything except 401/403 (writes send \`{}\` and ids are the zero GUID, so 400/404 means *authorised, then rejected*); everyone else gets **403**; no token gets **401**. Patient = booking token from \`POST /patient/api/patients/identify\` (carries a \`patientId\` claim and no role)."
echo
echo "**Result: $PASSES passed, $FAILS failed.**"
echo
echo "| Endpoint | Rule | Admin | Doctor | Nurse | Receptionist | Patient | No token |"
echo "|---|---|---|---|---|---|---|---|"
printf '%s' "$TABLE"

if [[ -n "${AUDIT_OUT:-}" && -n "${TOKEN[A]:-}" ]]; then
  sleep 60   # let the gateway's rate-limit window reset before the audit query
  {
    echo "# SCRUM-149 — Audit entries for the RBAC run"
    echo
    echo "\`GET $GW/api/reports/audit?from=$START_UTC&to=$END_UTC\` as Admin, $(date -u '+%Y-%m-%d %H:%M UTC')."
    echo
    echo "Identity audits POST/PUT/DELETE on \`/api/staff\`, \`/api/departments\` and \`/api/roles\`; the matrix's write probes on those paths are what appears here."
    echo
    echo '```json'
    curl -s -m "$TIMEOUT" -H "Authorization: Bearer ${TOKEN[A]}" \
      "$GW/api/reports/audit?from=$START_UTC&to=$END_UTC"
    echo
    echo '```'
  } > "$AUDIT_OUT"
fi

[[ $FAILS -eq 0 ]]
