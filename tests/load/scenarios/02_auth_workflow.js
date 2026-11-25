// =============================================================================
// Scenario 2: Authenticated Staff Workflow Test
// =============================================================================
// Authenticates via the API Gateway in setup() and tests concurrent staff
// querying protected microservice endpoints using JWT Bearer authentication.
// =============================================================================

import http from 'k6/http';
import { check, sleep } from 'k6';
import { CONFIG } from '../config.js';
import { login, authHeaders, generateHtmlReport } from '../helpers.js';

export const options = {
  stages: [
    { duration: '3s', target: 2 },   // Ramp-up
    { duration: '10s', target: 5 },  // Concurrent authenticated staff browsing
    { duration: '3s', target: 0 },   // Ramp-down
  ],
  thresholds: CONFIG.THRESHOLDS,
};

// Authenticate once in setup to avoid tripping Gateway /auth/login rate limiter (5 req/min)
export function setup() {
  const token = login(CONFIG.STAFF_USER.email, CONFIG.STAFF_USER.password);
  if (!token) {
    throw new Error('Failed to obtain JWT token during setup. Check credentials or service status.');
  }
  return { token };
}

export default function (data) {
  const params = authHeaders(data.token);

  // 1. Query departments list via Gateway
  const deptRes = http.get(`${CONFIG.BASE_URL}/identity/api/departments`, params);
  check(deptRes, {
    'departments status is 200': (r) => r.status === 200,
  });

  sleep(1.2);

  // 2. Query specializations list via Gateway
  const specRes = http.get(`${CONFIG.BASE_URL}/identity/api/specializations`, params);
  check(specRes, {
    'specializations status is 200': (r) => r.status === 200,
  });

  sleep(1.5);
}

export function handleSummary(data) {
  return {
    'tests/load/reports/auth_workflow_report.html': generateHtmlReport(data, 'Scenario 2: Authenticated Staff Workflow'),
    stdout: textSummary(data),
  };
}

function textSummary(data) {
  const p95 = data.metrics.http_req_duration ? data.metrics.http_req_duration.values['p(95)'] : 'N/A';
  const reqs = data.metrics.http_reqs ? data.metrics.http_reqs.values.count : 0;
  return `\n=== MediCore Authenticated Workflow Test Complete ===\n` +
         `Total Requests: ${reqs}\n` +
         `p(95) Latency:  ${typeof p95 === 'number' ? p95.toFixed(2) + ' ms' : p95}\n` +
         `HTML Report:    tests/load/reports/auth_workflow_report.html\n\n`;
}
