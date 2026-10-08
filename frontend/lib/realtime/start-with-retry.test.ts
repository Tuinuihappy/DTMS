import { test } from "node:test";
import assert from "node:assert/strict";
import { retryDelayMs, startWithRetry, type HubLinkState } from "./start-with-retry.ts";

function fakeConnection(failures: number) {
  let state: HubLinkState = "disconnected";
  let calls = 0;
  return {
    get calls() {
      return calls;
    },
    set(s: HubLinkState) {
      state = s;
    },
    state: () => state,
    start: async () => {
      calls++;
      if (calls <= failures) throw new Error(`fail ${calls}`);
      state = "connected";
    },
  };
}

test("retries a failed start until it connects", async () => {
  const conn = fakeConnection(2);
  const sleeps: number[] = [];
  await startWithRetry({
    start: conn.start,
    state: conn.state,
    sleep: async (ms) => {
      sleeps.push(ms);
    },
    random: () => 0,
  });
  assert.equal(conn.calls, 3);
  assert.equal(conn.state(), "connected");
  assert.deepEqual(sleeps, [1_000, 2_000]);
});

test("backoff doubles and caps at 30s plus jitter", () => {
  assert.equal(retryDelayMs(0, 0), 1_000);
  assert.equal(retryDelayMs(3, 0), 8_000);
  assert.equal(retryDelayMs(10, 0), 30_000);
  assert.equal(retryDelayMs(10, 0.5), 30_500);
});

test("does not start a connection that is already connected", async () => {
  const conn = fakeConnection(0);
  conn.set("connected");
  await startWithRetry({ start: conn.start, state: conn.state, sleep: async () => {}, random: () => 0 });
  assert.equal(conn.calls, 0);
});

test("waits while another start is in flight instead of starting twice", async () => {
  const conn = fakeConnection(0);
  conn.set("busy");
  let waits = 0;
  await startWithRetry({
    start: conn.start,
    state: conn.state,
    sleep: async () => {
      waits++;
      if (waits === 2) conn.set("connected"); // the other start finished
    },
    random: () => 0,
  });
  assert.equal(conn.calls, 0);
  assert.equal(waits, 2);
});

test("shouldStop ends the loop without connecting", async () => {
  const conn = fakeConnection(Infinity);
  let rounds = 0;
  await startWithRetry({
    start: conn.start,
    state: conn.state,
    shouldStop: () => rounds >= 2,
    sleep: async () => {
      rounds++;
    },
    random: () => 0,
  });
  assert.equal(conn.calls, 2);
});

test("reports each failure with its attempt number", async () => {
  const conn = fakeConnection(2);
  const seen: number[] = [];
  await startWithRetry({
    start: conn.start,
    state: conn.state,
    sleep: async () => {},
    random: () => 0,
    onError: (_err, attempt) => seen.push(attempt),
  });
  assert.deepEqual(seen, [0, 1]);
});
