namespace AMR.DeliveryPlanning.VendorAdapter.Riot3.Models;

/// <summary>
/// RIOT3-side outcome of an envelope operation (cancel / pause / resume).
/// Surface separates "vendor accepted" from "vendor doesn't know about
/// this order" so callers can apply per-command policy — Cancel can
/// forgive a NoVendorRecord, while Pause / Resume should escalate it
/// to a Trip state reconciliation.
/// </summary>
public enum Riot3OperationOutcome
{
    Accepted = 0,
    NoVendorRecord = 1
}
