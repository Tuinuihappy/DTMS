namespace DTMS.SharedKernel.Exceptions;

// Thrown by ShipmentStartedCallbackFanoutConsumer (and the started resend) when
// the trip has no VendorVehicleName yet. Normally a sub-second race with the
// MarkVendorStarted save, which the in-process retry ladder (~65s) covers. But
// when the vendor never reports a robot (RIOT3 outage, order failed pre-assignment,
// purged record) it never resolves. Excluded from DELAYED redelivery (the
// minutes-scale ladder) in ModuleServiceRegistration so it dead-letters after the
// fast in-process retries instead of dragging ~21 minutes — long past trip
// completion, which is what made the audit read time-reversed.
public sealed class VendorVehicleUnavailableException : InvalidOperationException
{
    public VendorVehicleUnavailableException(string message) : base(message) { }
}
