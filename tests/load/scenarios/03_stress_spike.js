// =============================================================================
// Scenario 3: Stress & Rate-Limit Resilience Spike Test
// =============================================================================
// Pushes a rapid surge of concurrent traffic to evaluate how MediCore's
// rate-limiting middleware protects internal services and recovers gracefully.
//
// In this test:
//   - HTTP 200: Normal processed request
//   - HTTP 429: Active rate limiting protection (Expected security/resilience defense)
//   - HTTP 500/502/503: Unhandled server failure (Forbidden by threshold)
// =============================================================================

import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter, Rate } from 'k6/metrics';
import { CONFIG } from '../config.js';
import { generateHtmlReport } from '../helpers.js';

// Custom metrics to track resilience indicators
const successfulRequests = new Counter('successful_200_reqs');
const rateLimitedRequests = new Counter('rate_limited_429_reqs');
const unexpectedErrors = new Rate('unexpected_server_errors');

export const options = {
  stages: [
    { duration: '3s', target: 5 },    // Normal traffic
    { duration: '6s', target: 40 },   // Aggressive surge beyond rate limit
    { duration: '5s', target: 40 },   // Hold spike
    { duration: '4s', target: 0 },    // Cool-down
  ],
  thresholds: {
    // 0% unhandled server crashes (no 500s)
    'unexpected_server_errors': ['rate<0.01'],
  },
};

export default function () {
  // Tell k6 that both 200 and 429 are expected responses for this resilience test
  const res = http.get(`${CONFIG.BASE_URL}/appointment/api/public/booking/specializations`, {
    tags: { name: 'Spike_Specializations' },
    responseCallback: http.expectedStatuses(200, 429),
  });

  if (res.status === 200) {
    successfulRequests.add(1);
    unexpectedErrors.add(0);
  } else if (res.status === 429) {
    rateLimitedRequests.add(1);
    unexpectedErrors.add(0);
  } else {
    unexpectedErrors.add(1);
  }

  // MediCore resilience check
  check(res, {
    'handled gracefully (status is 200 or 429)': (r) => r.status === 200 || r.status === 429,
    'no unhandled 500 server crash': (r) => r.status !== 500 && r.status !== 502 && r.status !== 503,
  });

  sleep(0.1);
}

export function handleSummary(data) {
  return {
    'tests/load/reports/stress_spike_report.html': generateHtmlReport(data, 'Scenario 3: Stress & Rate-Limit Resilience'),
    stdout: textSummary(data),
  };
}

function textSummary(data) {
  const p95 = data.metrics.http_req_duration ? data.metrics.http_req_duration.values['p(95)'] : 'N/A';
  const reqs = data.metrics.http_reqs ? data.metrics.http_reqs.values.count : 0;
  const limited = data.metrics.rate_limited_429_reqs ? data.metrics.rate_limited_429_reqs.values.count : 0;
  const ok = data.metrics.successful_200_reqs ? data.metrics.successful_200_reqs.values.count : 0;

  return `\n=== MediCore Stress & Rate Limit Test Complete ===\n` +
         `Total Requests:          ${reqs}\n` +
         `Processed (200 OK):      ${ok}\n` +
         `Rate Limited (429):      ${limited}\n` +
         `p(95) Latency:           ${typeof p95 === 'number' ? p95.toFixed(2) + ' ms' : p95}\n` +
         `HTML Report:             tests/load/reports/stress_spike_report.html\n\n`;
}
