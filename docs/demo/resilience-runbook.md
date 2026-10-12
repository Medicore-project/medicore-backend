# Resilience demo runbook (SCRUM-153)

Script: [`scripts/demo/resilience-demo.ps1`](../../scripts/demo/resilience-demo.ps1). It pauses before each step, so you can talk while it runs.
Environment: the **local docker compose stack**. Monitoring (Prometheus, Grafana, alerts) runs only locally, so this is where resilience is visible.
Total time: about 10–12 minutes, most of it alert wait time. Talk through the waits.

## Before the demo (do this 30 minutes ahead)

1. `docker compose up -d --build` from `medicore-backend/`, then wait until `docker compose ps` shows every service *healthy*.
2. Seed data if the database is fresh: `scripts/seed-local-demo.ps1`. You need at least one patient and **four free slots** in the next three weeks.
3. Open these tabs:
   - Grafana *MediCore — Overview* (`http://localhost:3000`)
   - Prometheus alerts (`http://localhost:9090/alerts`)
   - Kafka UI (`http://localhost:8080`) → Consumers
   - MailHog (`http://localhost:8025`)
4. Set the receptionist credentials for the session, or let the script prompt:
   ```powershell
   $env:MEDICORE_RECEPTION_EMAIL = "reception@medicore.local"
   $env:MEDICORE_RECEPTION_PASSWORD = Read-Host -AsSecureString | ForEach-Object { [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($_)) }
   ```
5. **Rehearse once, end to end.** Step 4 leaves a message on `patient-events.dlt`, and the DLT alert keeps firing until that topic is recreated. Before the real demo, reset it:
   ```powershell
   docker compose rm -sf kafka kafka-init kafka-exporter
   docker volume rm medicore_kafka-data
   docker compose up -d
   ```
   This wipes local Kafka data only; the volume name comes from `docker volume ls`. Postgres data and the outbox are untouched, and kafka-init recreates the 12 topics.

Run: `.\scripts\demo\resilience-demo.ps1`

## Step 1: Baseline

**Show:** the script's liveness lines (all five `[ok]`), the Grafana health tiles all green, and the lag panel flat at 0.

**Say:** "Five independently deployed services behind a YARP gateway. Each has its own database schema and talks to the others only through Kafka events, written through a transactional outbox. Everything is up, consumer lag is zero, and the outbox is empty."

## Step 2: Service down (identity)

**What happens:**
- `identity-api` stops, and new logins fail.
- A booking made with the **token issued before the outage** still returns **201**.
- After about 90 seconds `ServiceDown{job="identity-api"}` fires.

**Show:**
- the failed login status;
- the `booked … -> 201` line;
- the Grafana Identity tile turning red;
- the Prometheus alerts page with ServiceDown **firing**. Screenshot: `169-servicedown-firing.png`.

**Say:** "Identity is gone, but booking keeps working. The appointment service validates the JWT itself with the shared signing key, and it keeps its own copy of doctors from `staff-events` (SCRUM-33), so there's no runtime call to Identity. The failure is contained, and monitoring tells us within a minute."

## Step 3: Broker outage (Kafka)

**What happens:**
- Kafka stops, and three more bookings all return **201**.
- The appointment outbox shows three unpublished rows.
- When you press Enter, Kafka starts, the outbox drains to 0 and lag returns to 0.

**Show:**
- the three 201s;
- the `unpublished appointment outbox rows: 3` line. Optionally show them in pgAdmin or psql: `SELECT "EventType","OccurredOnUtc","ProcessedOnUtc" FROM medicore_appointment.outbox_messages ORDER BY "OccurredOnUtc" DESC LIMIT 3;`
- the drain loop counting down;
- Kafka UI consumers catching up.

**Say:** "The broker is down, but users don't notice. Each booking and its event are saved in one database transaction, so the event can't be lost and the request doesn't need Kafka. When the broker comes back, the outbox processor publishes the backlog in order, keyed by appointment id, and the consumers deduplicate by message id. That's at-least-once delivery with idempotent consumers."

## Step 4: Dead letter (notifications)

**What happens:**
- MailHog stops, and a new patient is registered.
- Billing's notification consumer can't send the welcome email. It routes the event to `patient-events.retry` up to 3 times (`MaxDeliveryAttempts=3`), then to `patient-events.dlt`.
- The DLT depth rises, `KafkaDeadLetterQueueNotEmpty` fires, and MailHog is restarted.

**Show:**
- the `billing log: Notification delivery failed … routed attempt N to …` lines;
- Grafana's *DLT depth* panel turning red;
- the Prometheus alert firing. Screenshot: `179-dlt-alert-firing.png`.
- Then register another patient in the UI and show the welcome email arriving in MailHog. Screenshot: `179-mailhog-welcome-email.png`.

**Say:** "A poison or undeliverable message must not block the stream. After three attempts the notification is parked on a dead-letter topic, the alert asks a human to decide, and every other message keeps flowing. Invoicing never stopped, and new emails go out as soon as SMTP is back. This is SCRUM-46's acceptance criterion 4 and the SCRUM-39 retry/DLT pattern."

> ⚠️ **Never** demonstrate dead-lettering by publishing malformed or invalid messages to `appointment-events`. Billing's *invoicing* consumer has no DLT path (finding F4): it would retry that message forever and stall invoicing. The notification path used here is the safe, real one.

**If step 4 doesn't produce a DLT message,** for example because the patient registration was rejected:
- register a patient in the UI while MailHog is still stopped, then press Enter to continue;
- as a last resort, use the fallback in the sprint plan §5.3: a *well-formed* `appointment.completed` for an unknown patient, which Patient dead-letters to `appointment-events.dlt`. Rehearse it first, and stop if Billing logs "offset was not committed".

## Step 5: Restore

**Show:** all five liveness lines `[ok]` and the Grafana tiles green.

**Say:** "Everything has recovered without manual data repair. ServiceDown clears on its own. The DLT alert stays on deliberately: a dead-lettered message needs someone to look at it."

## Known limits to mention if asked

- Monitoring is local only; production Azure isn't scraped (future work: Azure Monitor / Application Insights).
- Prometheus has no Alertmanager, so alerts are shown on the Prometheus page, not routed to email or Slack.
- The DLT alert is based on the topic's end offset, so it stays firing until the topic is cleared or recreated.
