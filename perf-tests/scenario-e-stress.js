// Stress test: ramp VU 0→500 across mixed read+write workload to find
// the breaking point (latency knee, first sustained error rate, throughput
// plateau). Threshold abort lets the test stop early when SLOs degrade
// past acceptable limits — but we keep them loose so we can see the
// failure mode rather than masking it.
import http from 'k6/http';
import { check, group } from 'k6';
import { Trend, Rate, Counter } from 'k6/metrics';
import { uuidv4 } from 'https://jslib.k6.io/k6-utils/1.4.0/index.js';

const API = __ENV.API_BASE || 'http://host.docker.internal:5219';

const readLat = new Trend('latency_read', true);
const writeLat = new Trend('latency_write', true);
const errorRate = new Rate('errors');
const totalReqs = new Counter('total_requests');

export const options = {
  scenarios: {
    stress_ramp: {
      executor: 'ramping-vus',
      startVUs: 5,
      stages: [
        { duration: '30s', target: 50 },
        { duration: '60s', target: 150 },
        { duration: '60s', target: 300 },
        { duration: '60s', target: 500 },
        { duration: '30s', target: 500 },
        { duration: '20s', target: 0 },
      ],
      gracefulRampDown: '10s',
    },
  },
  thresholds: {
    // Loose thresholds — we want to OBSERVE the breaking point, not
    // abort early. Treat as informational only.
    http_req_failed: ['rate<0.50'],
    errors: ['rate<0.50'],
  },
  summaryTrendStats: ['min', 'avg', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

function newOrderPayload() {
  const ref = `STR-${uuidv4()}`;
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
        description: 'stress test',
        pickupLocationCode: 'WH-01',
        dropLocationCode: 'BAY-A',
        loadUnitProfileCode: 'PALLET-EU',
        dimensions: { lengthMm: 1200, widthMm: 800, heightMm: 1500 },
        weightKg: 200,
        quantity: { value: 1, uom: 'PALLET' },
      },
    ],
    priority: 'NORMAL',
    sourceSystem: 'SAP',
    requestedTransportMode: 'AMR',
  };
}

export default function () {
  // 70/30 read/write blend — closer to a real ops workload than scenario B
  const roll = Math.random();
  if (roll < 0.7) {
    group('read', () => {
      const r = http.get(`${API}/api/v1/delivery-orders?pageSize=20`, { tags: { op: 'read' } });
      readLat.add(r.timings.duration);
      totalReqs.add(1);
      errorRate.add(!check(r, { 'read ok': (x) => x.status === 200 }));
    });
  } else {
    group('write', () => {
      const r = http.post(
        `${API}/api/v1/delivery-orders/upstream`,
        JSON.stringify(newOrderPayload()),
        { headers: { 'Content-Type': 'application/json' }, tags: { op: 'write' } },
      );
      writeLat.add(r.timings.duration);
      totalReqs.add(1);
      errorRate.add(!check(r, { 'write ok': (x) => x.status >= 200 && x.status < 300 }));
    });
  }
}
