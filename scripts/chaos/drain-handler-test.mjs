// G1 Phase 3 verification — headless SignalR client that proves:
//   1. /admin/drain-start triggers a __drain broadcast on the hub
//   2. The client receives it (verifies backend Phase 1 broadcast loop)
//   3. The cycle pattern Phase 3 ships (stop → start) works without
//      DrainAwareHubFilter rejecting the reconnect (because the settle
//      window expires fast enough; in K8s the reconnect goes to a
//      different pod anyway).
//
// Standalone — does NOT use the frontend bundle. Runs inside a one-off
// node container with @microsoft/signalr installed inline.

import * as signalR from "@microsoft/signalr";

// API_URL pins to a specific replica when set (e.g. http://dtms-api-1:8080),
// or load-balances via Docker DNS round-robin when defaulted to "api:8080".
// Used by the F1 + per-pod-drain isolation test to assert that draining
// pod A leaves clients connected to pod B untouched.
const API = process.env.API_URL || "http://api:8080";
const HUB = "/hubs/dashboard";
const LABEL = process.env.LABEL || "client";

const log = (msg) => console.log(`[${new Date().toISOString()}] [${LABEL}] ${msg}`);

async function login() {
  const r = await fetch(`${API}/api/auth/token`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ username: "admin", password: "admin123" }),
  });
  if (!r.ok) throw new Error(`auth failed: ${r.status}`);
  const data = await r.json();
  return data.token;
}

async function triggerDrain() {
  // Loopback guard means we have to call /admin/drain-start from inside
  // the api container itself. Use a one-off docker exec via a side
  // channel — easier: just call from this container if it shares net.
  // But the loopback guard checks IPAddress.IsLoopback of the REMOTE
  // address as seen by the server, and a sibling container's IP is NOT
  // loopback. So we need the api to exec curl on itself.
  //
  // Trick: run the drain trigger via a child process that does
  // `docker exec dtms-api curl ...`. But this container doesn't have
  // docker CLI access either.
  //
  // Cleaner: skip self-trigger; the operator runs the drain command
  // from the host after this script prints READY. We just wait.
  log("waiting for __drain — trigger it from host now:");
  log(`  docker exec dtms-api curl -s -X POST -H 'Content-Type: application/json' -d '{"settleSeconds":5}' http://localhost:8080/api/v1/admin/drain-start`);
}

async function run() {
  log("logging in as admin...");
  const token = await login();
  log(`got JWT (length ${token.length})`);

  const url = `${API}${HUB}`;
  const connection = new signalR.HubConnectionBuilder()
    .withUrl(url, {
      accessTokenFactory: () => token,
      transport: signalR.HttpTransportType.WebSockets,
      skipNegotiation: true,
    })
    .withAutomaticReconnect({
      nextRetryDelayInMilliseconds: (ctx) => {
        const base = Math.min(1000 * Math.pow(2, ctx.previousRetryCount), 30000);
        return base + Math.random() * 1000;
      },
    })
    .configureLogging(signalR.LogLevel.Information)
    .build();

  connection.onreconnecting(() => log("STATE: reconnecting (auto)"));
  connection.onreconnected(() => log("STATE: reconnected (auto)"));
  connection.onclose((err) => log(`STATE: closed${err ? ` — ${err.message}` : ""}`));

  let drainReceived = false;
  connection.on("__drain", async () => {
    drainReceived = true;
    log("*** RECEIVED __drain event ***");
    log("simulating Phase 3 client cycle: stop() → start()");
    try {
      await connection.stop();
      log("stopped successfully");
    } catch (err) {
      log(`stop failed: ${err.message}`);
    }
    // Small delay so server's settle window lapses before we reconnect
    // (otherwise DrainAwareHubFilter rejects with HubException).
    await new Promise((r) => setTimeout(r, 6000));
    log("attempting restart...");
    try {
      await connection.start();
      log("*** RESTARTED — Phase 3 cycle complete ***");
    } catch (err) {
      log(`restart failed (expected if server still draining): ${err.message}`);
    }
  });

  log(`connecting to ${url}...`);
  await connection.start();
  log(`STATE: connected (state=${connection.state})`);

  // Subscribe to a board so the connection is "active" not just open
  try {
    await connection.invoke("Subscribe", "vendor-health");
    log("subscribed to vendor-health board");
  } catch (err) {
    log(`subscribe warn (non-fatal): ${err.message}`);
  }

  await triggerDrain();

  // Wait up to 90s for __drain (operator runs the trigger from host)
  const deadline = Date.now() + 90000;
  while (!drainReceived && Date.now() < deadline) {
    await new Promise((r) => setTimeout(r, 500));
  }

  if (!drainReceived) {
    log("FAIL: did not receive __drain within 30s");
    process.exit(1);
  }

  // After receiving + restart, wait briefly for state to settle
  await new Promise((r) => setTimeout(r, 3000));
  log(`final state: ${connection.state}`);

  if (connection.state === signalR.HubConnectionState.Connected) {
    log("PASS: full Phase 1+3 round-trip verified");
    process.exit(0);
  } else {
    log(`MIXED: __drain received but did not reach Connected post-cycle (state=${connection.state})`);
    process.exit(2);
  }
}

run().catch((err) => {
  log(`FATAL: ${err.stack || err}`);
  process.exit(1);
});
