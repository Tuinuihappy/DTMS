"use client";

import { useContext } from "react";
import {
  OperatorPoolContext,
  type OperatorPoolValue,
} from "@/components/operator-pwa/operator-pool-provider";

// WMS PR-4b (PR-D) — Operator pool state. The state and its single
// /hubs/operator-pool subscription live in OperatorPoolProvider (mounted by
// app/m/layout.tsx); see the comment there for why it must be one.
export function usePoolTrips(): OperatorPoolValue {
  const value = useContext(OperatorPoolContext);
  if (!value) {
    throw new Error(
      "usePoolTrips must be used under <OperatorPoolProvider> (app/m/layout.tsx). " +
        "Don't add a second pool subscription — see operator-pool-provider.tsx.",
    );
  }
  return value;
}
