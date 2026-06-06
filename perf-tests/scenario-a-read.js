import http from 'k6/http';
import { check, group } from 'k6';
import { Trend, Rate, Counter } from 'k6/metrics';

const API = __ENV.API_BASE || 'http://host.docker.internal:5219';

const statsLatency = new Trend('latency_stats', true);
const listLatency = new Trend('latency_list', true);
const pageLatency = new Trend('latency_list_paged', true);
const errorRate = new Rate('errors');
const reqCount = new Counter('total_requests');

export const options = {
  scenarios: {
    read_ramp: {
      executor: 'ramping-vus',
      startVUs: 1,
      stages: [
        { duration: '10s', target: 25 },
        { duration: '40s', target: 100 },
        { duration: '20s', target: 100 },
        { duration: '10s', target: 0 },
      ],
      gracefulRampDown: '5s',
    },
  },
  thresholds: {
    http_req_duration: ['p(95)<500', 'p(99)<1500'],
    http_req_failed: ['rate<0.01'],
    errors: ['rate<0.01'],
  },
  summaryTrendStats: ['min', 'avg', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

export default function () {
  group('stats', () => {
    const r = http.get(`${API}/api/v1/delivery-orders/stats`, { tags: { ep: 'stats' } });
    statsLatency.add(r.timings.duration);
    reqCount.add(1);
    const ok = check(r, { 'stats 200': (x) => x.status === 200 });
    errorRate.add(!ok);
  });

  group('list', () => {
    const r = http.get(`${API}/api/v1/delivery-orders?pageSize=20&page=1`, { tags: { ep: 'list' } });
    listLatency.add(r.timings.duration);
    reqCount.add(1);
    const ok = check(r, { 'list 200': (x) => x.status === 200 });
    errorRate.add(!ok);
  });

  group('list_status', () => {
    const r = http.get(`${API}/api/v1/delivery-orders?pageSize=50&page=1&status=DRAFT`, { tags: { ep: 'list_status' } });
    pageLatency.add(r.timings.duration);
    reqCount.add(1);
    const ok = check(r, { 'list_status 200': (x) => x.status === 200 });
    errorRate.add(!ok);
  });
}
