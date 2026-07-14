namespace DTMS.OmsAdapter.Abstractions.Exceptions;

// Thrown by ShipmentStartedCallbackFanoutConsumer (and the started resend) when
// the trip has no VendorVehicleName yet. Normally a sub-second race with the
// MarkVendorStarted save, which the in-process retry ladder (~65s) covers. But
// when the vendor never reports a robot (RIOT3 outage, order failed pre-assignment,
// purged record) it never resolves. Excluded from DELAYED redelivery (the
// minutes-scale ladder) in ModuleServiceRegistration so it dead-letters after the
// fast in-process retries instead of dragging ~21 minutes — long past trip
// completion, which is what made the audit read time-reversed.
//
// (Formerly this file also held OmsPermanentException / OmsTransientException for
// the legacy OMS adapter; both were removed in Phase 4 when the adapter was
// deleted — the federated dispatcher signals rejection via SourceCallbackOutcome
// / HttpRequestException status, not typed OMS exceptions.)
public sealed class VendorVehicleUnavailableException : InvalidOperationException
{
    public VendorVehicleUnavailableException(string message) : base(message) { }
}
