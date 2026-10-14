"use client";

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from "react";
import { isAudioUnlocked, soundAlert, unlockAudio, type AlertKind } from "@/lib/operator-pwa/alert-output";

// The operator app's attention layer: the banners on top of every /m page,
// plus the sound and vibration that go with them. Feature code raises
// alerts by key; this owns how they look and sound, so a new kind of alert
// (e.g. "your trip was cancelled") is a few lines, not a new banner.

export type OperatorAlert = {
  /** One banner per key; raising the same key updates it in place. */
  key: string;
  kind: AlertKind;
  title: string;
  detail?: string;
  /** Where tapping the banner goes. */
  href: string;
  /** Called when the banner is closed (tapped, ✕, or dismissed by code). */
  onDismiss?: () => void;
};

type AlertsValue = {
  alerts: OperatorAlert[];
  /** Show (or update) a banner; `sound` plays the kind's tone + vibration. */
  raise: (alert: OperatorAlert, opts?: { sound?: boolean }) => void;
  dismiss: (key: string) => void;
  /** Sound + vibration with no banner (e.g. the pool page, cards in view). */
  sound: (kind: AlertKind) => void;
  /** Chrome blocks sound until the first tap on the page. */
  audioLocked: boolean;
};

const AlertsContext = createContext<AlertsValue | null>(null);

/** The alerts layer, or null outside /m (the nav renders elsewhere too). */
export function useOperatorAlerts(): AlertsValue | null {
  return useContext(AlertsContext);
}

export function OperatorAlertsProvider({ children }: { children: ReactNode }) {
  const [alerts, setAlerts] = useState<OperatorAlert[]>([]);
  const [audioLocked, setAudioLocked] = useState(true);
  const alertsRef = useRef(alerts);
  alertsRef.current = alerts;

  // Unlock audio on the first interaction, anywhere on the page.
  useEffect(() => {
    if (isAudioUnlocked()) {
      setAudioLocked(false);
      return;
    }
    const onInteract = () => {
      void unlockAudio().then((ok) => {
        if (!ok) return;
        setAudioLocked(false);
        window.removeEventListener("pointerdown", onInteract, true);
        window.removeEventListener("keydown", onInteract, true);
      });
    };
    window.addEventListener("pointerdown", onInteract, true);
    window.addEventListener("keydown", onInteract, true);
    return () => {
      window.removeEventListener("pointerdown", onInteract, true);
      window.removeEventListener("keydown", onInteract, true);
    };
  }, []);

  const raise = useCallback((alert: OperatorAlert, opts?: { sound?: boolean }) => {
    setAlerts((prev) => {
      const i = prev.findIndex((a) => a.key === alert.key);
      if (i === -1) return [...prev, alert];
      const next = [...prev];
      next[i] = alert;
      return next;
    });
    if (opts?.sound) void soundAlert(alert.kind);
  }, []);

  const dismiss = useCallback((key: string) => {
    const gone = alertsRef.current.find((a) => a.key === key);
    if (!gone) return;
    setAlerts((prev) => prev.filter((a) => a.key !== key));
    gone.onDismiss?.();
  }, []);

  const sound = useCallback((kind: AlertKind) => void soundAlert(kind), []);

  const value = useMemo<AlertsValue>(
    () => ({ alerts, raise, dismiss, sound, audioLocked }),
    [alerts, raise, dismiss, sound, audioLocked],
  );

  return <AlertsContext.Provider value={value}>{children}</AlertsContext.Provider>;
}
