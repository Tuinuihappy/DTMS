import http from 'k6/http';
import { check } from 'k6';
import { Trend, Rate, Counter } from 'k6/metrics';
import { uuidv4 } from 'https://jslib.k6.io/k6-utils/1.4.0/index.js';

const API = __ENV.API_BASE || 'http://host.docker.internal:5219';

const createLatency = new Trend('latency_create_order', true);
const errorRate = new Rate('errors');
const createdCount = new Counter('orders_created');

export const options = {
  scenarios: {
    write_ramp: {
      executor: 'ramping-vus',
      startVUs: 1,
      stages: [
        { duration: '10s', target: 10 },
        { duration: '30s', target: 30 },
        { duration: '20s', target: 30 },
        { duration: '10s', target: 0 },
      ],
      gracefulRampDown: '5s',
    },
  },
  thresholds: {
    'http_req_duration{ep:create}': ['p(95)<1500', 'p(99)<3000'],
    http_req_failed: ['rate<0.05'],
    errors: ['rate<0.05'],
  },
  summaryTrendStats: ['min', 'avg', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

function buildPayload() {
  const ref = `LOAD-${uuidv4()}`;
  const now = new Date();
  const earliest = new Date(now.getTime() + 60 * 60 * 1000);
  const latest = new Date(now.getTime() + 24 * 60 * 60 * 1000);
  return {
    orderRef: ref,
    serviceWindow: {
      earliestUtc: earliest.toISOString(),
      latestUtc: latest.toISOString(),
    },
    items: [
      {
        itemId: `${ref}-I1`,
        description: 'Load test item',
        pickupLocationCode: 'WH-01',
        dropLocationCode: 'BAY-A',
        loadUnitProfileCode: 'PALLET-EU',
        dimensions: { lengthMm: 1200, widthMm: 800, heightMm: 1500 },
        weightKg: 250,
        quantity: { value: 1, uom: 'PALLET' },
      },
    ],
    priority: 'NORMAL',
    sourceSystem: 'SAP',
    requestedBy: 'k6-load',
    notes: 'k6 scenario B',
    requestedTransportMode: 'AMR',
  };
}

export default function () {
  const body = JSON.stringify(buildPayload());
  const r = http.post(`${API}/api/v1/delivery-orders/upstream`, body, {
    headers: { 'Content-Type': 'application/json' },
    tags: { ep: 'create' },
  });
  createLatency.add(r.timings.duration);
  const ok = check(r, {
    'create accepted (200/201/202)': (x) => x.status === 200 || x.status === 201 || x.status === 202,
  });
  if (ok) createdCount.add(1);
  errorRate.add(!ok);
}
