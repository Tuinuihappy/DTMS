namespace DTMS.DeliveryOrder.Domain.Enums;

// Fleet (3PL) was removed 2026-08-20 — Phase 5 cancelled. Stored as string
// in every table (no ordinal coupling), so re-adding a mode is additive.
public enum TransportMode { Amr, Manual }
