// =============================================================================
// tests/load/helpers.js — Helper functions for MediCore k6 tests
// =============================================================================

import http from 'k6/http';
import { check } from 'k6';
import { CONFIG } from './config.js';

/**
 * Authenticates against MediCore Identity / Gateway service and returns JWT token
 */
export function login(email = CONFIG.STAFF_USER.email, password = CONFIG.STAFF_USER.password) {
  const loginUrl = `${CONFIG.BASE_URL}/auth/login`;
  const payload = JSON.stringify({ email, password });
  const params = {
    headers: { 'Content-Type': 'application/json' },
    tags: { name: 'Auth_Login' },
  };

  const res = http.post(loginUrl, payload, params);

  const loginSuccess = check(res, {
    'login status is 200': (r) => r.status === 200,
    'login has accessToken': (r) => {
      try {
        const body = JSON.parse(r.body);
        return body && !!body.accessToken;
      } catch (e) {
        return false;
      }
    },
  });

  if (!loginSuccess) {
    return null;
  }

  const json = JSON.parse(res.body);
  return json.accessToken;
}

/**
 * Returns standard headers with Authorization Bearer token
 */
export function authHeaders(token) {
  return {
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${token}`,
    },
  };
}

/**
 * Generates a clean standalone HTML summary dashboard without external CDN dependencies
 */
export function generateHtmlReport(data, title = 'MediCore Performance Report') {
  const duration = data.metrics.http_req_duration ? data.metrics.http_req_duration.values : {};
  const reqs = data.metrics.http_reqs ? data.metrics.http_reqs.values.count : 0;
  const rps = data.metrics.http_reqs ? data.metrics.http_reqs.values.rate.toFixed(1) : 0;
  const failedRate = data.metrics.http_req_failed ? (data.metrics.http_req_failed.values.rate * 100).toFixed(2) : 0;
  const p95 = duration['p(95)'] ? duration['p(95)'].toFixed(1) : 'N/A';
  const avg = duration.avg ? duration.avg.toFixed(1) : 'N/A';
  const min = duration.min ? duration.min.toFixed(1) : 'N/A';
  const max = duration.max ? duration.max.toFixed(1) : 'N/A';

  const isPassed = Number(failedRate) < 1.0 && (p95 === 'N/A' || Number(p95) < 500);

  return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <title>${title}</title>
  <style>
    body { font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif; background: #0f172a; color: #f8fafc; margin: 0; padding: 24px; }
    .container { max-width: 900px; margin: 0 auto; }
    .header { display: flex; justify-content: space-between; align-items: center; border-bottom: 1px solid #334155; padding-bottom: 16px; margin-bottom: 24px; }
    h1 { margin: 0; font-size: 24px; color: #38bdf8; }
    .badge { padding: 6px 14px; border-radius: 9999px; font-weight: 600; font-size: 14px; }
    .badge.pass { background: #065f46; color: #34d399; border: 1px solid #10b981; }
    .badge.fail { background: #881337; color: #f43f5e; border: 1px solid #f43f5e; }
    .grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 16px; margin-bottom: 24px; }
    .card { background: #1e293b; padding: 18px; border-radius: 8px; border: 1px solid #334155; }
    .card .title { font-size: 13px; color: #94a3b8; text-transform: uppercase; letter-spacing: 0.05em; margin-bottom: 6px; }
    .card .value { font-size: 28px; font-weight: 700; color: #f1f5f9; }
    .card .unit { font-size: 14px; color: #64748b; font-weight: normal; }
    table { width: 100%; border-collapse: collapse; background: #1e293b; border-radius: 8px; overflow: hidden; border: 1px solid #334155; }
    th, td { padding: 12px 16px; text-align: left; border-bottom: 1px solid #334155; }
    th { background: #0f172a; color: #94a3b8; font-size: 13px; }
    tr:last-child td { border-bottom: none; }
    .footer { margin-top: 24px; text-align: center; color: #64748b; font-size: 12px; }
  </style>
</head>
<body>
  <div class="container">
    <div class="header">
      <div>
        <h1>${title}</h1>
        <small style="color: #64748b;">MediCore Microservice Performance & Load Testing</small>
      </div>
      <span class="badge ${isPassed ? 'pass' : 'fail'}">${isPassed ? 'SLO PASSED' : 'SLO BREACHED'}</span>
    </div>

    <div class="grid">
      <div class="card">
        <div class="title">Total Requests</div>
        <div class="value">${reqs}</div>
      </div>
      <div class="card">
        <div class="title">Throughput</div>
        <div class="value">${rps} <span class="unit">req/s</span></div>
      </div>
      <div class="card">
        <div class="title">p(95) Response Time</div>
        <div class="value">${p95} <span class="unit">ms</span></div>
      </div>
      <div class="card">
        <div class="title">Failure Rate</div>
        <div class="value">${failedRate}%</div>
      </div>
    </div>

    <table>
      <thead>
        <tr>
          <th>Metric</th>
          <th>Observed Value</th>
          <th>SLO Target / Threshold</th>
          <th>Status</th>
        </tr>
      </thead>
      <tbody>
        <tr>
          <td>Latency p(95)</td>
          <td>${p95} ms</td>
          <td>&lt; 400.0 ms</td>
          <td style="color: ${Number(p95) < 400 ? '#34d399' : '#f43f5e'}; font-weight: 600;">${Number(p95) < 400 ? 'PASS' : 'WARN'}</td>
        </tr>
        <tr>
          <td>Latency Average</td>
          <td>${avg} ms</td>
          <td>&lt; 250.0 ms</td>
          <td style="color: ${Number(avg) < 250 ? '#34d399' : '#f43f5e'}; font-weight: 600;">${Number(avg) < 250 ? 'PASS' : 'WARN'}</td>
        </tr>
        <tr>
          <td>Latency Min / Max</td>
          <td>${min} ms / ${max} ms</td>
          <td>-</td>
          <td>INFO</td>
        </tr>
        <tr>
          <td>HTTP Error Rate</td>
          <td>${failedRate}%</td>
          <td>&lt; 1.0%</td>
          <td style="color: ${Number(failedRate) < 1.0 ? '#34d399' : '#f43f5e'}; font-weight: 600;">${Number(failedRate) < 1.0 ? 'PASS' : 'FAIL'}</td>
        </tr>
      </tbody>
    </table>

    <div class="footer">
      Generated automatically by k6 Automated Testing Pipeline • MediCore SE3112 Assessment
    </div>
  </div>
</body>
</html>`;
}
