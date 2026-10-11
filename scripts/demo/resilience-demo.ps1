<#
.SYNOPSIS
    SCRUM-153 resilience demo on the local docker compose stack.

.DESCRIPTION
    Walks through five steps and pauses for Enter before each one:

      1. Baseline       - every service live, outbox empty, DLT depth recorded
      2. Service down   - stop identity-api; an already-authenticated booking still
                          succeeds; Prometheus fires ServiceDown
      3. Broker outage  - stop Kafka; three bookings return 201 and wait in the
                          outbox; start Kafka; the outbox drains
      4. Dead letter    - stop MailHog; register a patient; Billing's welcome email
                          fails, retries 3 times and lands on patient-events.dlt;
                          KafkaDeadLetterQueueNotEmpty fires; start MailHog
      5. Restore        - everything running and live again

    Only the real failure paths are used: no hand-crafted Kafka messages. Never
    publish malformed events to appointment-events (finding F4: Billing's
    invoicing consumer would retry them forever).

    See docs/demo/resilience-runbook.md for what to show and say at each step.

.PARAMETER GatewayUrl
    Gateway base URL. Default http://localhost:5000.

.NOTES
    Credentials come from the environment, or are prompted for:
      MEDICORE_RECEPTION_EMAIL / MEDICORE_RECEPTION_PASSWORD  (a Receptionist)
    The stack must already be up: docker compose up -d --build
    Run from the medicore-backend directory.
#>
param(
    [string]$GatewayUrl = "http://localhost:5000",
    [string]$PrometheusUrl = "http://localhost:9090"
)

$ErrorActionPreference = "Stop"

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------
function Step([string]$Title) {
    Write-Host ""
    Write-Host ("=" * 78) -ForegroundColor DarkGray
    Write-Host "  $Title" -ForegroundColor Cyan
    Write-Host ("=" * 78) -ForegroundColor DarkGray
    [void](Read-Host "Press Enter to run this step")
}

function Ok([string]$Text)   { Write-Host "  [ok]   $Text" -ForegroundColor Green }
function Bad([string]$Text)  { Write-Host "  [fail] $Text" -ForegroundColor Red }
function Info([string]$Text) { Write-Host "  $Text" }

function Invoke-Api {
    param([string]$Method, [string]$Path, $Body, [string]$Token)
    $headers = @{}
    if ($Token) { $headers["Authorization"] = "Bearer $Token" }
    $params = @{ Method = $Method; Uri = "$GatewayUrl$Path"; Headers = $headers; TimeoutSec = 30 }
    if ($null -ne $Body) {
        $params["ContentType"] = "application/json"
        $params["Body"] = ($Body | ConvertTo-Json -Depth 5)
    }
    try {
        $response = Invoke-WebRequest @params -UseBasicParsing
        $content = $null
        if ($response.Content) { $content = $response.Content | ConvertFrom-Json }
        return [pscustomobject]@{ Status = [int]$response.StatusCode; Body = $content }
    }
    catch {
        $status = 0
        if ($_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
        return [pscustomobject]@{ Status = $status; Body = $null }
    }
}

function Get-Live([string]$Path) {
    try { return [int](Invoke-WebRequest -Uri "$GatewayUrl$Path" -UseBasicParsing -TimeoutSec 10).StatusCode }
    catch {
        if ($_.Exception.Response) { return [int]$_.Exception.Response.StatusCode }
        return 0
    }
}

function Show-Liveness {
    foreach ($target in @(
            @{ Name = "gateway"; Path = "/health/live" },
            @{ Name = "identity"; Path = "/identity/health/live" },
            @{ Name = "patient"; Path = "/patient/health/live" },
            @{ Name = "appointment"; Path = "/appointment/health/live" },
            @{ Name = "billing"; Path = "/billing/health/live" })) {
        $code = Get-Live $target.Path
        if ($code -eq 200) { Ok "$($target.Name) live (200)" } else { Bad "$($target.Name) not live ($code)" }
    }
}

function Invoke-Sql([string]$Sql) {
    # Runs as the container's own superuser; no credentials leave the container.
    # The SQL goes through stdin: Windows PowerShell 5.1 mangles embedded double
    # quotes in native-command arguments, and the quoted column names need them.
    $output = $Sql | docker exec -i medicore-postgres sh -c 'psql -U $POSTGRES_USER -d $POSTGRES_DB -tA'
    return ($output | Out-String).Trim()
}

function Get-UnpublishedOutbox {
    return [int](Invoke-Sql 'SELECT count(*) FROM medicore_appointment.outbox_messages WHERE "ProcessedOnUtc" IS NULL;')
}

function Get-PromValue([string]$Query) {
    $encoded = [uri]::EscapeDataString($Query)
    try {
        $result = Invoke-RestMethod -Uri "$PrometheusUrl/api/v1/query?query=$encoded" -TimeoutSec 10
        if ($result.data.result.Count -gt 0) { return [double]$result.data.result[0].value[1] }
    }
    catch { }
    return 0
}

function Wait-Alert([string]$AlertName, [string]$LabelValue, [int]$TimeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $alerts = (Invoke-RestMethod -Uri "$PrometheusUrl/api/v1/alerts" -TimeoutSec 10).data.alerts
            $match = $alerts | Where-Object {
                $_.labels.alertname -eq $AlertName -and $_.state -eq "firing" -and
                (-not $LabelValue -or ($_.labels.PSObject.Properties.Value -contains $LabelValue))
            }
            if ($match) { Ok "$AlertName is FIRING ($LabelValue) - show it at $PrometheusUrl/alerts"; return $true }
            $pending = $alerts | Where-Object { $_.labels.alertname -eq $AlertName -and $_.state -eq "pending" }
            if ($pending) { Info "$AlertName pending..." }
        }
        catch { }
        Start-Sleep -Seconds 10
    }
    Bad "$AlertName did not fire within $TimeoutSeconds s"
    return $false
}

function Get-FreeSlots([string]$Token, [int]$Count) {
    $doctors = (Invoke-Api -Method Get -Path "/appointment/api/doctors" -Token $Token).Body
    $from = (Get-Date).AddDays(1).ToString("yyyy-MM-dd")
    $to = (Get-Date).AddDays(21).ToString("yyyy-MM-dd")
    $slots = @()
    foreach ($doctor in @($doctors)) {
        $available = (Invoke-Api -Method Get -Token $Token `
                -Path "/appointment/api/slots/available?doctorId=$($doctor.doctorId)&from=$from&to=$to").Body
        $slots += @($available)
        if ($slots.Count -ge $Count) { break }
    }
    return @($slots | Select-Object -First $Count)
}

function Get-AnyPatientId([string]$Token) {
    $result = (Invoke-Api -Method Get -Path "/patient/api/patients/search?q=a&pageSize=1" -Token $Token).Body
    return @($result.items)[0].patientId
}

function Book([string]$Token, $Slot, [string]$PatientId) {
    $result = Invoke-Api -Method Post -Path "/appointment/api/appointments" -Token $Token `
        -Body @{ slotId = $Slot.slotId; patientId = $PatientId }
    if ($result.Status -eq 201) { Ok "booked slot $($Slot.startUtc) -> 201" } else { Bad "booking returned $($result.Status)" }
    return $result.Status
}

$dltQuery = 'sum(max without (instance) (kafka_topic_partition_current_offset{topic="patient-events.dlt"}))'

# ---------------------------------------------------------------------------
# Credentials
# ---------------------------------------------------------------------------
$email = $env:MEDICORE_RECEPTION_EMAIL
if (-not $email) { $email = Read-Host "Receptionist email" }
$password = $env:MEDICORE_RECEPTION_PASSWORD
if (-not $password) {
    $secure = Read-Host "Receptionist password" -AsSecureString
    $password = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
        [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
}

# ---------------------------------------------------------------------------
# 1. Baseline
# ---------------------------------------------------------------------------
Step "1. Baseline - everything live, outbox empty, DLT depth recorded"
Show-Liveness
$login = Invoke-Api -Method Post -Path "/auth/login" -Body @{ email = $email; password = $password }
if ($login.Status -ne 200) { throw "Receptionist login failed ($($login.Status)). Check the credentials." }
$token = $login.Body.accessToken
Ok "signed in as Receptionist (token kept for the rest of the demo)"
$patientId = Get-AnyPatientId $token
if (-not $patientId) { throw "No patient found to book for. Run scripts/seed-local-demo.ps1 first." }
$slots = Get-FreeSlots $token 4
if ($slots.Count -lt 4) { throw "Need 4 free slots in the next 3 weeks; found $($slots.Count). Run scripts/seed-local-demo.ps1." }
Info "unpublished appointment outbox rows: $(Get-UnpublishedOutbox)"
Info "consumer lag (all groups): $(Get-PromValue 'sum(max without (instance) (kafka_consumergroup_lag))')"
$dltBefore = Get-PromValue $dltQuery
Info "patient-events.dlt depth: $dltBefore"

# ---------------------------------------------------------------------------
# 2. Service down
# ---------------------------------------------------------------------------
Step "2. Service down - stop identity-api; booking still works; ServiceDown fires"
docker compose stop identity-api | Out-Null
Ok "identity-api stopped"
$relogin = Invoke-Api -Method Post -Path "/auth/login" -Body @{ email = $email; password = $password }
Info "new logins now fail: POST /auth/login -> $($relogin.Status)"
[void](Book $token $slots[0] $patientId)
Info "the token is validated by the appointment service itself and doctors come from its local cache (SCRUM-33)"
Info "waiting for Prometheus (alert has for: 1m)..."
[void](Wait-Alert "ServiceDown" "identity-api" 180)
docker compose start identity-api | Out-Null
Ok "identity-api started again"

# ---------------------------------------------------------------------------
# 3. Broker outage
# ---------------------------------------------------------------------------
Step "3. Broker outage - stop Kafka; bookings still return 201; the outbox drains on restart"
docker compose stop kafka | Out-Null
Ok "kafka stopped"
foreach ($slot in $slots[1..3]) { [void](Book $token $slot $patientId) }
Info "unpublished appointment outbox rows while Kafka is down: $(Get-UnpublishedOutbox)"
Info "the events are safe in Postgres (transactional outbox); nothing was lost and no request failed"
[void](Read-Host "Show the outbox rows / Grafana now, then press Enter to start Kafka")
docker compose start kafka | Out-Null
Ok "kafka started; waiting for the outbox to drain..."
$deadline = (Get-Date).AddMinutes(4)
do {
    Start-Sleep -Seconds 10
    $remaining = Get-UnpublishedOutbox
    Info "unpublished outbox rows: $remaining"
} while ($remaining -gt 0 -and (Get-Date) -lt $deadline)
if ($remaining -eq 0) { Ok "outbox drained - every booking event reached Kafka" } else { Bad "outbox still has $remaining rows" }
Info "consumer lag (all groups): $(Get-PromValue 'sum(max without (instance) (kafka_consumergroup_lag))')"

# ---------------------------------------------------------------------------
# 4. Dead letter
# ---------------------------------------------------------------------------
Step "4. Dead letter - stop MailHog; a welcome email fails 3 times and is dead-lettered"
docker compose stop mailhog | Out-Null
Ok "mailhog stopped (Billing can no longer send email)"
$stamp = Get-Date -Format "MMddHHmmss"   # 10 digits, unique per run
$registration = Invoke-Api -Method Post -Path "/patient/api/patients" -Token $token -Body @{
    nic = "19$stamp"                                        # 12-digit NIC
    firstName = "Resilience"; lastName = "Demo"
    dateOfBirth = "1990-01-01"; gender = "Female"
    email = "resilience.$stamp@example.test"; phone = "077$($stamp.Substring(3))"   # 077 + 7 digits
    addressLine1 = "1 Demo Street"; addressLine2 = $null; district = "Colombo"
    emergencyContactName = $null; emergencyContactPhone = $null
}
if ($registration.Status -eq 201) { Ok "registered patient $($registration.Body.patientNumber) -> 201" }
else { Bad "patient registration returned $($registration.Status) - register one in the UI instead, then continue" }
Info "Billing consumes patient.registered, the SMTP send fails, and it routes the event"
Info "patient-events -> patient-events.retry (x3) -> patient-events.dlt"
$deadline = (Get-Date).AddMinutes(3)
do {
    Start-Sleep -Seconds 10
    $dltNow = Get-PromValue $dltQuery
    Info "patient-events.dlt depth: $dltNow (was $dltBefore)"
} while ($dltNow -le $dltBefore -and (Get-Date) -lt $deadline)
if ($dltNow -gt $dltBefore) { Ok "the failed notification reached the dead-letter topic" } else { Bad "DLT depth did not rise" }
docker logs medicore-billing --since 5m 2>&1 | Select-String "Notification delivery failed" | Select-Object -Last 4 |
    ForEach-Object { Info "billing log: $($_.Line.Trim())" }
[void](Wait-Alert "KafkaDeadLetterQueueNotEmpty" "patient-events.dlt" 90)
docker compose start mailhog | Out-Null
Ok "mailhog started - new notifications deliver again; invoicing was never blocked"

# ---------------------------------------------------------------------------
# 5. Restore
# ---------------------------------------------------------------------------
Step "5. Restore - all green"
Start-Sleep -Seconds 15
Show-Liveness
Info "ServiceDown resolves within about a minute; the DLT alert stays on until the DLT is cleared,"
Info "because a dead-lettered message needs a human decision - that is the point of the alert."
Write-Host ""
Write-Host "Demo complete." -ForegroundColor Cyan
