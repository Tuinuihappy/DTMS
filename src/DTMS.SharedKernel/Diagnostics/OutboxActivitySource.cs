using System.Diagnostics;

namespace DTMS.SharedKernel.Diagnostics;

/// <summary>
/// Phase O4 — dedicated <see cref="ActivitySource"/> for the outbox
/// publish path. Register with OpenTelemetry via
/// <c>tracing.AddSource(OutboxActivitySource.Name)</c> in Program.cs.
///
/// <para>Kept static so the outbox processor doesn't need DI to start
/// spans, matching the pattern of <c>System.Diagnostics.ActivitySource</c>
/// as used across the ASP.NET Core / MassTransit ecosystem.</para>
/// </summary>
public static class OutboxActivitySource
{
    public const string Name = "DTMS.Outbox";

    /// <summary>The singleton ActivitySource — start spans through this.</summary>
    public static readonly ActivitySource Source = new(Name, "1.0.0");
}
