# MediCore — Microservice Load & Performance Testing Pipeline

**Module:** SE3112 – Advanced Software Engineering (BSc Hons in Computer Science)  
**Testing Paradigm:** Load & Performance Testing  
**Tool Stack:** Grafana k6, Bash, HTML Reporting  
**Target Architecture:** MediCore Microservices (API Gateway, Identity Service, Appointment Service)

---

## 1. Overview & Individual Contribution

As part of the SE3112 Microservice Testing Pipeline, this module implements automated **Load & Performance Testing** using **k6** to evaluate the scalability, throughput, latency, and resilience of the MediCore microservices ecosystem under concurrent user traffic.

### Microservice Endpoints Under Test
1. **Public Read Scalability (`/appointment/api/public/booking/*`)**:
   - Evaluates response time for anonymous public booking directory queries (`GET /specializations`, `GET /doctors`).
2. **Authenticated Core Workflow (`/auth/login` + Protected Routes)**:
   - Measures end-to-end latency for JWT-based user authentication and protected resource fetching across microservices through the YARP API Gateway.
3. **Stress, Spike & Rate-Limiting Resilience**:
   - Simulates sudden concurrency surges up to 60+ Virtual Users (VUs) to test MediCore's rate limiting (`AppointmentRateLimitOptions` & Gateway rate limiting) ensuring requests fail gracefully with HTTP 429 rather than cascading 500 crashes.

---

## 2. Service Level Objectives (SLOs) & Quality Gates

The test suite enforces the following automated pass/fail thresholds:

| Metric | Target Threshold | Rationale |
| :--- | :--- | :--- |
| **p(95) Response Time** | `< 400ms` | 95% of user requests must complete within acceptable web interaction limits |
| **p(99) Response Time** | `< 800ms` | Outlier requests under load must remain sub-second |
| **HTTP Error Rate** | `< 1.0%` | System must not drop or fail valid requests under expected operational load |
| **Server Crash Rate** | `0%` | No 500/502/503 errors during stress spikes (429 Too Many Requests is expected for rate limiting) |

---

## 3. Directory Structure

```
tests/load/
├── config.js              # Centralized URLs, user credentials & SLO thresholds
├── helpers.js             # Authentication (JWT token retrieval) & HTML report builder
├── run-demo.sh            # Automated test runner with browser report viewer
├── README.md              # Setup and performance documentation
├── reports/               # Auto-generated standalone HTML summary dashboards
└── scenarios/
    ├── 01_public_reads.js # Read scalability (Ramp-up -> Peak -> Ramp-down)
    ├── 02_auth_workflow.js# Multi-step authenticated user journey
    └── 03_stress_spike.js # Rapid concurrency surge & rate-limiting resilience
```

---

## 4. Setup & Installation

### Prerequisites
* **k6**:
  * macOS: `brew install k6`
  * Linux: `sudo gpg -k && sudo apt-get install k6`
  * Windows: `winget install k6` or `choco install k6`
* **Docker & MediCore Services running:**
  ```bash
  docker compose up -d
  ```

---

## 5. Execution Commands

### A. Quick Demo Runner (For 2-Minute Presentation)
To run a scenario live and automatically open the HTML performance dashboard:

```bash
# Scenario 1: Public Read Scalability (Recommended for live demo)
./tests/load/run-demo.sh public

# Scenario 2: Authenticated Staff Workflow
./tests/load/run-demo.sh auth

# Scenario 3: Stress & Rate-Limit Resilience
./tests/load/run-demo.sh stress

# Full Suite (Automated Pipeline)
./tests/load/run-demo.sh all
```

### B. Direct k6 CLI Execution
```bash
# Run public read test with 30 Virtual Users
k6 run tests/load/scenarios/01_public_reads.js

# Custom Target Host (e.g., staging / remote environment)
k6 run -e BASE_URL=http://localhost:5000 tests/load/scenarios/01_public_reads.js
```

---

## 6. Understanding the Metrics & Output

* **`http_req_duration (p95)`**: 95th percentile response time. This means 95% of all requests completed faster than this number.
* **`http_reqs`**: Total requests processed and average Requests Per Second (Throughput).
* **`vus` & `vus_max`**: Number of active concurrent Virtual Users simulating real clients.
* **`http_req_failed`**: Percentage of failed HTTP requests (checked against the `< 1%` threshold).
* **`checks`**: Functional assertions verifying status codes (`200 OK`) and response body integrity.

---

## 7. Group CI/CD Pipeline Integration (GitHub Actions)

Add this step to your GitHub Actions workflow (`.github/workflows/pipeline.yml`) to systematically run the performance tests as part of the unified test pipeline:

```yaml
- name: Run k6 Load & Performance Tests
  uses: grafana/k6-action@v0.3.1
  with:
    filename: tests/load/scenarios/01_public_reads.js
    flags: --env BASE_URL=http://localhost:5000
```
