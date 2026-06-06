import http from 'k6/http';
import { Counter } from 'k6/metrics';

const API = __ENV.API_BASE || 'http://host.docker.internal:5219';
const status0 = new Counter('s_0');
const status2xx = new Counter('s_2xx');
const status4xx = new Counter('s_4xx');
const status5xx = new Counter('s_5xx');

export const options = {
  scenarios: {
    probe: {
      executor: 'constant-vus',
      vus: 30,
      duration: '15s',
    },
  },
};

let logged = 0;
export default function () {
  const r = http.get(`${API}/api/v1/delivery-orders/stats`);
  if (r.status === 0) status0.add(1);
  else if (r.status >= 200 && r.status < 300) status2xx.add(1);
  else if (r.status >= 400 && r.status < 500) {
    status4xx.add(1);
    if (logged < 3) {
      logged++;
      console.log(`status=${r.status} body=${(r.body || '').slice(0, 200)} headers=${JSON.stringify(r.headers)}`);
    }
  }
  else if (r.status >= 500) status5xx.add(1);
}
