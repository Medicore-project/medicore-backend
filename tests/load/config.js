// =============================================================================
// tests/load/config.js — MediCore Load Testing Configuration
// =============================================================================

export const CONFIG = {
  // Target API Gateway by default (port 5000), or direct service if bypassed
  BASE_URL: __ENV.BASE_URL || 'http://localhost:5000',

  // Default credentials (matches MediCore seeded data)
  ADMIN_USER: {
    email: __ENV.ADMIN_EMAIL || 'tathira@medicore.com',
    password: __ENV.ADMIN_PASSWORD || 'Password123!',
  },
  STAFF_USER: {
    email: __ENV.STAFF_EMAIL || 'tathira@medicore.com',
    password: __ENV.STAFF_PASSWORD || 'Password123!',
  },

  // Service Level Objectives (SLOs) & Quality Gate Thresholds
  THRESHOLDS: {
    // 95% of requests must complete under 400ms, 99% under 800ms
    http_req_duration: ['p(95)<400', 'p(99)<800'],
    // Request failure rate must stay below 1%
    http_req_failed: ['rate<0.01'],
  },
};
