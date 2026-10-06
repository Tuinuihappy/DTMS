// Run: npm run test:overlay   (node --test, Node 24 strips the types)
// Excluded from tsconfig — the .ts import below needs Node's resolver.
import { test } from "node:test";
import assert from "node:assert/strict";
import { createLayerStack, LAYER_Z_BASE, LAYER_Z_MAX } from "./layer-stack.ts";

type Opts = { parentId?: string | null; modal?: boolean; dismissible?: boolean };

function setup() {
  const target = new EventTarget();
  const stack = createLayerStack(target);
  const dismissed: string[] = [];
  const add = (id: string, o: Opts = {}) =>
    stack.register({
      id,
      parentId: o.parentId ?? null,
      modal: o.modal ?? true,
      dismissible: { current: o.dismissible ?? true },
      onDismiss: { current: () => dismissed.push(id) },
    });
  const key = (k: string, init: Partial<KeyboardEventInit & { target?: unknown }> = {}) => {
    const e = new Event("keydown", { cancelable: true }) as Event & Record<string, unknown>;
    const { target: evTarget, ...rest } = init;
    Object.assign(e, { key: k, repeat: false, isComposing: false, shiftKey: false, ...rest });
    if (evTarget) Object.defineProperty(e, "target", { value: evTarget });
    stack.handleKeyDown(e);
    return e;
  };
  return { target, stack, dismissed, add, key };
}

test("Esc dismisses only the topmost layer", () => {
  const { add, key, dismissed } = setup();
  add("order");
  add("trip");
  add("pass");
  key("Escape");
  assert.deepEqual(dismissed, ["pass"]);
});

test("a child that registers before its parent is still on top", () => {
  // React runs child effects first: deep link opening drawer + dialog together.
  const { add, stack } = setup();
  add("dialog", { parentId: "drawer" });
  add("drawer");
  assert.equal(stack.topId(), "dialog");
  assert.ok(stack.zOf("dialog")! > stack.zOf("drawer")!);
});

test("a later unrelated layer is above an earlier one's child", () => {
  const { add, stack } = setup();
  add("a");
  add("b");
  add("a-child", { parentId: "a" });
  assert.equal(stack.topId(), "a-child");
  add("c");
  assert.equal(stack.topId(), "c");
});

test("non-modal list inside a dialog takes Esc first", () => {
  const { add, key, dismissed } = setup();
  add("dialog");
  add("combobox-list", { parentId: "dialog", modal: false });
  key("Escape");
  assert.deepEqual(dismissed, ["combobox-list"]);
});

test("busy (not dismissible) swallows Esc without falling through", () => {
  const { add, key, dismissed } = setup();
  add("drawer");
  add("dialog", { dismissible: false });
  const e = key("Escape");
  assert.deepEqual(dismissed, []);
  assert.equal(e.defaultPrevented, true);
});

test("repeat, defaultPrevented and IME composition are ignored", () => {
  const { add, key, dismissed, stack } = setup();
  add("dialog");
  key("Escape", { repeat: true });
  key("Escape", { isComposing: true });
  const pre = new Event("keydown", { cancelable: true }) as Event & Record<string, unknown>;
  Object.assign(pre, { key: "Escape", repeat: false, isComposing: false });
  pre.preventDefault();
  stack.handleKeyDown(pre);
  assert.deepEqual(dismissed, []);
});

test("Esc from a date/time input is left to the browser picker", () => {
  const { add, key, dismissed } = setup();
  add("dialog");
  key("Escape", { target: { tagName: "INPUT", type: "date" } });
  key("Escape", { target: { tagName: "INPUT", type: "time" } });
  assert.deepEqual(dismissed, []);
  key("Escape", { target: { tagName: "INPUT", type: "text" } });
  assert.deepEqual(dismissed, ["dialog"]);
});

test("Enter is never handled", () => {
  const { add, key, dismissed } = setup();
  add("pass-robot");
  const e = key("Enter");
  assert.deepEqual(dismissed, []);
  assert.equal(e.defaultPrevented, false);
});

test("unregistering a layer that is not on top keeps the top", () => {
  const { add, stack, key, dismissed } = setup();
  const leaveA = add("a");
  add("b");
  leaveA();
  assert.equal(stack.topId(), "b");
  key("Escape");
  assert.deepEqual(dismissed, ["b"]);
});

test("z-index band starts at 100, steps by 10 and never reaches toasts (200)", () => {
  const { add, stack } = setup();
  for (let i = 0; i < 15; i++) add(`l${i}`);
  assert.equal(stack.zOf("l0"), LAYER_Z_BASE);
  assert.equal(stack.zOf("l1"), LAYER_Z_BASE + 10);
  assert.equal(stack.zOf("l14"), LAYER_Z_MAX);
  assert.ok(LAYER_Z_MAX < 200);
  assert.equal(stack.zOf("missing"), null);
});

test("listener attaches with the first layer and detaches with the last", () => {
  const { add, stack } = setup();
  assert.equal(stack.isListening(), false);
  const leave1 = add("a");
  const leave2 = add("b");
  assert.equal(stack.isListening(), true);
  leave1();
  assert.equal(stack.isListening(), true);
  leave2();
  assert.equal(stack.isListening(), false);
});

test("a stale unregister does not remove a re-registered layer (StrictMode)", () => {
  const { add, stack } = setup();
  const leaveOld = add("dialog");
  leaveOld();
  add("dialog");
  leaveOld();
  assert.equal(stack.isOpen("dialog"), true);
});

test("Tab wraps inside the topmost modal panel", () => {
  const { add, stack, key } = setup();
  const focused: string[] = [];
  const mk = (name: string) => ({ name, focus: () => focused.push(name) });
  const first = mk("first");
  const last = mk("last");
  const node = {
    focus: () => focused.push("panel"),
    contains: (x: unknown) => x === first || x === last,
    querySelectorAll: () => [first, last],
  };
  add("dialog");
  stack.setNode("dialog", node);
  (globalThis as Record<string, unknown>).document = { activeElement: last };
  const e1 = key("Tab");
  assert.equal(e1.defaultPrevented, true);
  (globalThis as Record<string, unknown>).document = { activeElement: first };
  key("Tab", { shiftKey: true });
  (globalThis as Record<string, unknown>).document = { activeElement: { outside: true } };
  key("Tab");
  assert.deepEqual(focused, ["first", "last", "first"]);
  delete (globalThis as Record<string, unknown>).document;
});
