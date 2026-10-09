# SCRUM-46: Email notifications

Billing sends three event-driven emails through the configured SMTP server (MailHog in Docker Compose):

| Event | Template code | Available placeholders |
|---|---|---|
| `patient.registered` | `PATIENT_WELCOME` | `{{patientName}}`, `{{patientId}}` |
| `appointment.booked` | `APPOINTMENT_CONFIRMATION` | `{{patientName}}`, `{{appointmentId}}`, `{{doctorId}}`, `{{slotStart}}`, `{{serviceCode}}` |
| `invoice.paid` | `PAYMENT_RECEIPT` | `{{patientName}}`, `{{invoiceId}}`, `{{amount}}`, `{{method}}` |

The three default templates are seeded by the Billing migration. An Admin can manage templates at `/billing/notifications` or through `GET/POST/PUT/DELETE /billing/api/notification-templates`. The same page displays recent delivery attempts from `GET /billing/api/notifications`.

The appointment and payment event contracts contain a patient ID but no email address. Billing therefore keeps a local contact cache from `patient.registered` events; it never reads the Patient database. If a contact is unavailable when a booking or payment event arrives, delivery is retried. Patients whose registration event has expired from Kafka retention need a new registration event or a future contact-sync workflow before they can receive these emails.

The main event consumer and retry consumer run separately. A failed send is logged, published to `<source-topic>.retry`, and its original offset is committed only after the retry publish succeeds. Each retry waits with exponential backoff; after the configured maximum of three attempts, the message goes to `<source-topic>.dlt`. `SourceMessageId` plus template code uniquely identifies a notification log, so a successfully sent event is skipped on redelivery. A crash after SMTP accepts a message but before the Sent log is saved can still cause a duplicate email; SMTP and PostgreSQL cannot share a transaction.

Local verification:

1. Start the backend Compose stack and the frontend. Billing applies its EF migration on startup.
2. Register a new patient with a valid email and inspect MailHog at `http://localhost:8025` for the welcome email.
3. Book an appointment for that patient, then complete and pay its invoice. MailHog should show confirmation and receipt emails, and the notification page should show three `Sent` log entries.
4. Stop MailHog, trigger another notification, and confirm a `Failed` log entry followed by a message in the matching `.retry` or `.dlt` topic in Kafka UI. Other event processing remains available.

Configuration: `Smtp:Host`, `Smtp:Port`, `Smtp:EnableSsl`, `Smtp:SendTimeoutSeconds`, `Smtp:FromAddress`, `Smtp:FromName`, and optional `Smtp:Username`/`Smtp:Password`; plus `Kafka:NotificationConsumer:GroupId`, `Topics`, `RetryDelaySeconds`, and `MaxDeliveryAttempts`. Keep SMTP credentials in environment or deployment configuration.
