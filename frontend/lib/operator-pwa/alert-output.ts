"use client";

// The operator app's alert outputs: sound, vibration, and the preferences
// that gate them. Browser-only; every call is safe to make anywhere (no-ops
// when an API is missing) because tablets differ in what they support.

import {
  CHIME_COALESCE_MS,
  DEFERRED_SOUND_MAX_MS,
  PREF_KEYS,
  TONE_GAIN,
  TONES,
  VIBRATION,
} from "./alert-config";

export type AlertKind = keyof typeof TONES;

type AudioContextCtor = typeof AudioContext;

let ctx: AudioContext | null = null;

function audioContext(): AudioContext | null {
  if (ctx) return ctx;
  if (typeof window === "undefined") return null;
  const Ctor: AudioContextCtor | undefined =
    window.AudioContext ??
    (window as unknown as { webkitAudioContext?: AudioContextCtor }).webkitAudioContext;
  if (!Ctor) return null;
  try {
    ctx = new Ctor();
  } catch {
    return null;
  }
  return ctx;
}

/**
 * Chrome won't play audio until the user has interacted with the page.
 * Call from inside a tap/key handler; afterwards sound works for the life
 * of the page (a reload needs another tap).
 */
export async function unlockAudio(): Promise<boolean> {
  const c = audioContext();
  if (!c) return false;
  if (c.state !== "running") {
    try {
      await c.resume();
    } catch {
      return false;
    }
  }
  return c.state === "running";
}

export function isAudioUnlocked(): boolean {
  return ctx?.state === "running";
}

/**
 * Plays the alert tone for `kind`. Returns false when it couldn't play.
 * Chrome on Android suspends the AudioContext while the screen is off and
 * doesn't always resume it, so wake it first — allowed without a fresh tap
 * once the page has been interacted with.
 */
export async function playTone(kind: AlertKind): Promise<boolean> {
  const c = ctx;
  if (!c) return false;
  if (c.state !== "running") {
    try {
      await c.resume();
    } catch {
      return false;
    }
    // Re-read after the await (TS keeps the pre-await narrowing).
    if ((c.state as AudioContextState) !== "running") return false;
  }
  try {
    let t = c.currentTime;
    for (const step of TONES[kind]) {
      const osc = c.createOscillator();
      const gain = c.createGain();
      osc.type = "square";
      osc.frequency.value = step.hz;
      const end = t + step.ms / 1000;
      // Short attack/release so steps don't click.
      gain.gain.setValueAtTime(0, t);
      gain.gain.linearRampToValueAtTime(TONE_GAIN, t + 0.01);
      gain.gain.setValueAtTime(TONE_GAIN, end - 0.02);
      gain.gain.linearRampToValueAtTime(0, end);
      osc.connect(gain).connect(c.destination);
      osc.start(t);
      osc.stop(end);
      t = end;
    }
    return true;
  } catch {
    return false;
  }
}

export function vibrate(kind: AlertKind): void {
  try {
    navigator.vibrate?.(VIBRATION[kind]);
  } catch {
    // No motor, or blocked before the first user interaction.
  }
}

function readPref(key: string): boolean {
  try {
    return window.localStorage.getItem(key) !== "off";
  } catch {
    return true;
  }
}

function writePref(key: string, on: boolean): void {
  try {
    window.localStorage.setItem(key, on ? "on" : "off");
  } catch {
    // Private mode / storage blocked: the default (on) applies.
  }
}

/** Sound + vibration for alerts. Default on. Read in effects only. */
export const soundPref = {
  get: () => readPref(PREF_KEYS.sound),
  set: (on: boolean) => writePref(PREF_KEYS.sound, on),
};

/** Keep the screen awake on /m. Default on. Read in effects only. */
export const keepAwakePref = {
  get: () => readPref(PREF_KEYS.keepAwake),
  set: (on: boolean) => writePref(PREF_KEYS.keepAwake, on),
};

let deferred: { kind: AlertKind; at: number } | null = null;
let deferListening = false;

function deferUntilVisible(kind: AlertKind): void {
  // "Your trip was cancelled" outranks "new pool trip" if both are waiting.
  if (!deferred || kind === "trip-ended") deferred = { kind, at: Date.now() };
  if (deferListening) return;
  deferListening = true;
  document.addEventListener("visibilitychange", () => {
    if (document.visibilityState !== "visible" || !deferred) return;
    const held = deferred;
    deferred = null;
    // Older than this, the banner on screen says it all.
    if (Date.now() - held.at <= DEFERRED_SOUND_MAX_MS) void soundAlert(held.kind);
  });
}

/**
 * Plays one alert across all open tabs: only a visible tab sounds, and a
 * Web Lock (atomic across tabs) plus a shared timestamp stops two tabs that
 * saw the same trip from both chiming. Falls back to the timestamp alone
 * where Web Locks are missing (insecure origin without the flag).
 */
export async function soundAlert(kind: AlertKind): Promise<void> {
  if (typeof document === "undefined") return;
  if (!soundPref.get()) return;
  if (document.visibilityState !== "visible") {
    // Screen off / app in the background: Chrome may still deliver the
    // event, and by screen-on the trip is no longer "new". Hold the sound
    // and play it the moment the page is visible again.
    deferUntilVisible(kind);
    return;
  }

  const claimAndPlay = () => {
    const now = Date.now();
    try {
      const last = Number(window.localStorage.getItem(PREF_KEYS.lastChimeAt) ?? 0);
      if (kind === "pool-new" && now - last < CHIME_COALESCE_MS) return;
      if (kind === "pool-new") window.localStorage.setItem(PREF_KEYS.lastChimeAt, String(now));
    } catch {
      // No storage: this tab decides alone.
    }
    void playTone(kind);
    vibrate(kind);
  };

  const locks = (navigator as Navigator & { locks?: LockManager }).locks;
  if (!locks) {
    claimAndPlay();
    return;
  }
  try {
    await locks.request("dtms-operator-alert", { ifAvailable: true }, async (lock) => {
      if (lock) claimAndPlay();
    });
  } catch {
    claimAndPlay();
  }
}
