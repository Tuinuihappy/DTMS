import http from 'k6/http';
import { check, group } from 'k6';
import { Trend, Rate, Counter } from 'k6/metrics';
import { uuidv4 } from 'https://jslib.k6.io/k6-utils/1.4.0/index.js';

const API = __ENV.API_BASE || 'http://host.docker.internal:5219';

const createLat = new Trend('latency_create', true);
const getLat = new Trend('latency_get', true);
const listLat = new Trend('latency_list', true);
const statsLat = new Trend('latency_stats', true);
const errorRate = new Rate('errors');
const e2eCount = new Counter('e2e_cycles');

export const options = {
  scenarios: {
    mixed: {
      executor: 'ramping-vus',
      startVUs: 1,
      stages: [
        { duration: '10s', target: 15 },
        { duration: '40s', target: 50 },
        { duration: '20s', target: 50 },
        { duration: '10s', target: 0 },
      ],
      gracefulRampDown: '5s',
    },
  },
  thresholds: {
    'http_req_duration{ep:list}': ['p(95)<800'],
    'http_req_duration{ep:stats}': ['p(95)<800'],
    'http_req_duration{ep:get}': ['p(95)<1000'],
    'http_req_duration{ep:create}': ['p(95)<2000'],
    http_req_failed: ['rate<0.05'],
  },
  summaryTrendStats: ['min', 'avg', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

function newOrderPayload() {
  const ref = `MIX-${uuidv4()}`;
  const now = Date.now();
  return {
    orderRef: ref,
    serviceWindow: {
      earliestUtc: new Date(now + 3600_000).toISOString(),
      latestUtc: new Date(now + 86_400_000).toISOString(),
    },
    items: [
      {
        itemId: `${ref}-I1`,
        description: 'mixed scenario',
        pickupLocationCode: 'WH-01',
        dropLocationCode: 'BAY-B',
        loadUnitProfileCode: 'PALLET-EU',
        dimensions: { lengthMm: 1200, widthMm: 800, heightMm: 1400 },
        weightKg: 180,
        quantity: { value: 1, uom: 'PALLET' },
      },
    ],
    priority: 'NORMAL',
    sourceSystem: 'SAP',
    requestedTransportMode: 'AMR',
  };
}

export default function () {
  let createdId = null;

  group('create', () => {
    const r = http.post(
      `${API}/api/v1/delivery-orders/upstream`,
      JSON.stringify(newOrderPayload()),
      { headers: { 'Content-Type': 'application/json' }, tags: { ep: 'create' } }
    );
    createLat.add(r.timings.duration);
    const ok = check(r, { 'create ok': (x) => x.status === 200 || x.status === 201 || x.status === 202 });
    errorRate.add(!ok);
    if (ok) {
      try {
        createdId = r.json('id');
      } catch (_) {}
    }
  });

  group('list', () => {
    const r = http.get(`${API}/api/v1/delivery-orders?pageSize=20`, { tags: { ep: 'list' } });
    listLat.add(r.timings.duration);
    errorRate.add(!check(r, { 'list ok': (x) => x.status === 200 }));
  });

  group('stats', () => {
    const r = http.get(`${API}/api/v1/delivery-orders/stats`, { tags: { ep: 'stats' } });
    statsLat.add(r.timings.duration);
    errorRate.add(!check(r, { 'stats ok': (x) => x.status === 200 }));
  });

  if (createdId) {
    group('get', () => {
      const r = http.get(`${API}/api/v1/delivery-orders/${createdId}`, { tags: { ep: 'get' } });
      getLat.add(r.timings.duration);
      errorRate.add(!check(r, { 'get ok': (x) => x.status === 200 }));
    });
  }

  e2eCount.add(1);
}
