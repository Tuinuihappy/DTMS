import { test } from "node:test";
import assert from "node:assert/strict";
import { CHIME_COALESCE_MS, SEEN_CAP, initialPoolWatch, observePool } from "./pool-alert-logic.ts";

const T0 = 1_000_000;

test("the first load is never new, whatever is waiting", () => {
  const r = observePool(initialPoolWatch, ["a", "b"], T0);
  assert.equal(r.chime, false);
  assert.deepEqual(r.newIds, []);
  assert.equal(r.watch.primed, true);
});

test("a trip that appears after the first load chimes", () => {
  const primed = observePool(initialPoolWatch, ["a"], T0).watch;
  const r = observePool(primed, ["a", "b"], T0 + 10);
  assert.deepEqual(r.newIds, ["b"]);
  assert.equal(r.chime, true);
});

test("the same list twice does not chime again (event + refetch for one trip)", () => {
  let w = observePool(initialPoolWatch, [], T0).watch;
  w = observePool(w, ["a"], T0 + 10).watch;
  const r = observePool(w, ["a"], T0 + 20);
  assert.equal(r.chime, false);
  assert.deepEqual(r.newIds, []);
});

test("a burst within the coalesce window chimes once but reports every trip", () => {
  let w = observePool(initialPoolWatch, [], T0).watch;
  const first = observePool(w, ["a"], T0 + 10);
  w = first.watch;
  const second = observePool(w, ["a", "b"], T0 + 10 + CHIME_COALESCE_MS - 1);
  assert.equal(first.chime, true);
  assert.equal(second.chime, false);
  assert.deepEqual(second.newIds, ["b"]);
});

test("a trip after the window chimes again", () => {
  let w = observePool(initialPoolWatch, [], T0).watch;
  w = observePool(w, ["a"], T0).watch;
  const r = observePool(w, ["a", "b"], T0 + CHIME_COALESCE_MS);
  assert.equal(r.chime, true);
});

test("a list that shrinks and comes back does not make old trips new", () => {
  // A failed or partial refetch must not re-alert the whole pool.
  let w = observePool(initialPoolWatch, ["a", "b"], T0).watch;
  w = observePool(w, [], T0 + 10).watch;
  const r = observePool(w, ["a", "b"], T0 + 20);
  assert.equal(r.chime, false);
  assert.deepEqual(r.newIds, []);
});

test("claimed or cancelled trips leaving the list are not alerts", () => {
  let w = observePool(initialPoolWatch, ["a", "b"], T0).watch;
  const r = observePool(w, ["a"], T0 + 10);
  assert.equal(r.chime, false);
  assert.deepEqual(r.newIds, []);
});

test("seen memory is capped and keeps the newest ids", () => {
  const many = Array.from({ length: SEEN_CAP + 20 }, (_, i) => `t${i}`);
  let w = observePool(initialPoolWatch, [], T0).watch;
  w = observePool(w, many, T0).watch;
  assert.equal(w.seen.length, SEEN_CAP);
  assert.equal(w.seen[w.seen.length - 1], `t${SEEN_CAP + 19}`);
});

test("duplicate ids in one list count once", () => {
  let w = observePool(initialPoolWatch, [], T0).watch;
  const r = observePool(w, ["a", "a"], T0);
  assert.deepEqual(r.newIds, ["a"]);
});
