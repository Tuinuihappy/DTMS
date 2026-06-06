import http from 'k6/http';
import { check, group } from 'k6';
import { Trend, Rate } from 'k6/metrics';

const FE = __ENV.FE_BASE || 'http://host.docker.internal:3000';

const rootLat = new Trend('latency_root', true);
const loginLat = new Trend('latency_login', true);
const dashLat = new Trend('latency_dashboard', true);
const errorRate = new Rate('errors');

export const options = {
  scenarios: {
    fe_browse: {
      executor: 'ramping-vus',
      startVUs: 1,
      stages: [
        { duration: '10s', target: 20 },
        { duration: '30s', target: 60 },
        { duration: '20s', target: 60 },
        { duration: '10s', target: 0 },
      ],
      gracefulRampDown: '5s',
    },
  },
  thresholds: {
    http_req_duration: ['p(95)<1500'],
    http_req_failed: ['rate<0.05'],
  },
  summaryTrendStats: ['min', 'avg', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

export default function () {
  group('root', () => {
    const r = http.get(`${FE}/`, { tags: { page: 'root' } });
    rootLat.add(r.timings.duration);
    errorRate.add(!check(r, { 'root 2xx/3xx': (x) => x.status >= 200 && x.status < 400 }));
  });

  group('login', () => {
    const r = http.get(`${FE}/login`, { tags: { page: 'login' } });
    loginLat.add(r.timings.duration);
    errorRate.add(!check(r, { 'login 2xx/3xx': (x) => x.status >= 200 && x.status < 400 }));
  });

  group('delivery-orders', () => {
    const r = http.get(`${FE}/delivery-orders`, { tags: { page: 'dashboard' } });
    dashLat.add(r.timings.duration);
    errorRate.add(!check(r, { 'dashboard 2xx/3xx': (x) => x.status >= 200 && x.status < 400 }));
  });
}
