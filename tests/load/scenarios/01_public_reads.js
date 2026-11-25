// =============================================================================
// Scenario 1: Public Read Scalability Test
// =============================================================================
// Tests realistic concurrent browsing on appointment directory endpoints
// (Specializations & Doctors list) through the API Gateway.
// Concurrency is tuned for single-IP client benchmarking within gateway limits.
// =============================================================================

import http from 'k6/http';
import { check, sleep } from 'k6';
import { CONFIG } from '../config.js';
import { generateHtmlReport } from '../helpers.js';

export const options = {
  stages: [
    { duration: '3s', target: 2 },   // Warm-up ramp
    { duration: '10s', target: 5 },  // Sustained realistic concurrent browsing
    { duration: '3s', target: 0 },   // Graceful ramp-down
  ],
  thresholds: CONFIG.THRESHOLDS,
};

export default function () {
  // 1. Fetch available specializations
  const specRes = http.get(`${CONFIG.BASE_URL}/appointment/api/public/booking/specializations`, {
    tags: { name: 'Get_Specializations' },
  });
  check(specRes, {
    'specializations status is 200': (r) => r.status === 200,
    'specializations returned': (r) => {
      try {
        const data = JSON.parse(r.body);
        return Array.isArray(data) && data.length >= 0;
      } catch (e) {
        return false;
      }
    },
  });

  // Realistic user reading/browsing pause
  sleep(1.2);

  // 2. Fetch doctors directory
  const docRes = http.get(`${CONFIG.BASE_URL}/appointment/api/public/booking/doctors`, {
    tags: { name: 'Get_Doctors' },
  });
  check(docRes, {
    'doctors status is 200': (r) => r.status === 200,
    'doctors returned': (r) => {
      try {
        const data = JSON.parse(r.body);
        return Array.isArray(data) && data.length >= 0;
      } catch (e) {
        return false;
      }
    },
  });

  sleep(1.5);
}

export function handleSummary(data) {
  return {
    'tests/load/reports/public_reads_report.html': generateHtmlReport(data, 'Scenario 1: Public Read Scalability'),
    stdout: textSummary(data),
  };
}

function textSummary(data) {
  const p95 = data.metrics.http_req_duration ? data.metrics.http_req_duration.values['p(95)'] : 'N/A';
  const reqs = data.metrics.http_reqs ? data.metrics.http_reqs.values.count : 0;
  return `\n=== MediCore Public Reads Load Test Complete ===\n` +
         `Total Requests: ${reqs}\n` +
         `p(95) Latency:  ${typeof p95 === 'number' ? p95.toFixed(2) + ' ms' : p95}\n` +
         `HTML Report:    tests/load/reports/public_reads_report.html\n\n`;
}
