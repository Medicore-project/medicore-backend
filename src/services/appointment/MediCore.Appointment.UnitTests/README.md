# MediCore Appointment — Unit Testing & Mocking

**SE3112 Microservice Testing Pipeline · Individual role: Unit Testing & Mocking**
**Owner:** Nizam T.H.B (IT24103458) · Group 29

Isolated unit tests for the **Appointment & Scheduling** service's Application layer, using
**parameterized cases** and **Moq test doubles** so every test exercises service logic only —
no database, no Kafka, no HTTP.

---

## Tools

| Tool | Version | Why |
|---|---|---|
| xUnit | 2.9.3 | The stack's existing test framework; `[Theory]` gives `InlineData`, `MemberData` and `ClassData` parameterization |
| Moq | 4.20.72 | Stubs (`Setup/Returns`), mocks (`Verify` + `Times`), argument matchers (`It.Is`), `Callback` capture, `SetupSequence`, `MockSequence`, `MockBehavior.Strict` |
| FluentValidation.TestHelper | 12.1.1 | Ships inside FluentValidation; idiomatic `TestValidate` assertions for request validators |
| Coverlet + ReportGenerator | 6.0.4 / 5.4.4 | Line/branch coverage of the Application layer, rendered as HTML |

The project references **only** `MediCore.Appointment.Application`. It cannot reach EF Core, Npgsql
or Confluent.Kafka even by accident — isolation is enforced by the build, not by discipline.

---

## Run it

From `src/services/appointment/MediCore.Appointment.UnitTests/`:

```powershell
./run-tests.ps1                        # all tests + HTML coverage report   (Windows)
./run-tests.ps1 -Filter Parameterized  # only the [Theory] tests
./run-tests.ps1 -Filter Mocking        # only the Moq behaviour-verification tests
./run-tests.ps1 -Filter Booking        # one feature: Booking | Lifecycle | Waitlist | Validation | DomainRules
./run-tests.ps1 -Open                  # also open the coverage report
```

```bash
./run-tests.sh            # Linux / macOS, same filters as the first argument
dotnet test --filter "Technique=Parameterized"   # raw dotnet CLI
```

Output: console results, `TestResults/unit-tests.trx`, and `TestResults/coverage-report/index.html`.

### In the pipeline

`.github/workflows/unit-tests-appointment.yml` runs on every push/PR touching the Appointment
Application layer or these tests, writes a pass/fail + coverage table to the run's summary page, and
uploads the TRX and HTML report as artifacts. It also declares `workflow_call`, so the group's
unified pipeline includes it as one stage:

```yaml
jobs:
  unit-tests:
    uses: ./.github/workflows/unit-tests-appointment.yml
```

**For the mutation-testing stage:** Stryker.NET can use this suite directly —
`dotnet stryker --test-project src/services/appointment/MediCore.Appointment.UnitTests/UnitTests.csproj --project Application.csproj`.

---

## What is tested

| Area | System under test | Highlights |
|---|---|---|
| **Booking** (SCRUM-34/35) | `AppointmentBookingService` | Transactional outbox verified with `It.Is<OutboxMessage>`; call-order check (patient lock → read → overlap → save); every rejection proves *nothing was saved*; service code always from the doctor cache (anti-tampering); retry loop driven by a fake transaction that loses N races; `SetupSequence` reproduces "lost the race, retry finds slot booked" |
| **Lifecycle** (SCRUM-36) | `AppointmentLifecycleService` | Cancellation window boundaries incl. clamped misconfiguration; slot released and handed to the waitlist mock (same instance); `MockSequence` + strict mocks prove the waitlist offer is staged before the single save; reschedule guards; who may complete / mark no-show |
| **Waitlist** (SCRUM-37) | `WaitlistOfferer` | Strict mocks prove an unbookable slot touches no dependency; queue walk withdraws, skips and stops at the right patient; offer expiry = min(window, slot start) |
| **Validation** | Request validators | Boundary-value analysis: 500-char reason, 8,000-char notes, trimming |
| **Domain rules** | `ServiceCodes`, `AppointmentStatusTransitions` | Slug table incl. punctuation/non-ASCII/overlong input; full 5×5 state-machine matrix from `ClassData` |

### Test-double vocabulary used in the code

| Double | Where | Verified by |
|---|---|---|
| **Stub** — canned answers in | `GetTrackedBySlotIdAsync` returns a slot; `TimeProvider` frozen at a fixed instant | The state of the result |
| **Mock** — expectations checked after | Outbox, history, waitlist offerer, `SaveChangesAsync` | `Verify(..., Times.Once/Never)` |
| **Fake** — a working light implementation | `ExecuteInTransactionAsync` pass-through that can be told to lose races | Attempt counter (`TransactionProbe`) |
| **Strict mock** — any unplanned call fails | `WaitlistOfferer` pre-conditions, sequence test | Moq throws `MockException` |

---

## Folder layout

```
MediCore.Appointment.UnitTests/
├── Booking/          AppointmentBookingServiceTests.cs
├── Lifecycle/        AppointmentLifecycleServiceTests.cs
├── Waitlist/         WaitlistOffererTests.cs
├── Validation/       AppointmentRequestValidatorTests.cs
├── DomainRules/      ServiceCodesTests.cs, AppointmentStatusTransitionsTests.cs
├── Support/          builders, frozen clock, fake transaction, trait names
├── coverlet.runsettings
├── run-tests.ps1 / run-tests.sh
└── UnitTests.csproj
```

Every class carries `[Trait("Category","Unit")]`, a `Feature` trait, and each test a `Technique`
trait (`Parameterized` or `Mocking`), which is what the filters above select on.
