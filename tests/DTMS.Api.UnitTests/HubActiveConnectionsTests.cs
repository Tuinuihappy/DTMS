using System.Diagnostics.Metrics;
using DTMS.Api.Realtime.Filters;
using DTMS.Api.Realtime.Observability;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DTMS.Api.UnitTests;

// dtms.pool.broadcast.total{outcome=sent} counts a send to an empty group
// as sent. dtms.signalr.hub.connections_active is what shows whether any
// tablet was there to receive it, so it must go up on connect, back down
// on disconnect, and never drift when a connect is rejected.
public class HubActiveConnectionsTests
{
    // A hub type of its own so the "hub" tag isolates this test's
    // measurements from anything else sharing the meter.
    private sealed class ProbeHub : Hub;

    private static (Func<long> active, MeterListener listener) Listen(HubMetrics metrics)
    {
        long total = 0;
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == HubMetrics.MeterName && instrument.Name == "dtms.signalr.hub.connections_active")
                    l.EnableMeasurementEvents(instrument);
            },
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            foreach (var tag in tags)
                if (tag.Key == "hub" && (string?)tag.Value == nameof(ProbeHub))
                    Interlocked.Add(ref total, value);
        });
        listener.Start();
        return (() => Interlocked.Read(ref total), listener);
    }

    private static HubLifetimeContext Context()
        => new(Substitute.For<HubCallerContext>(), Substitute.For<IServiceProvider>(), new ProbeHub());

    [Fact]
    public async Task ConnectThenDisconnect_ReturnsToZero()
    {
        using var metrics = new HubMetrics();
        var (active, listener) = Listen(metrics);
        using var _ = listener;
        var filter = new TracingHubFilter(metrics, NullLogger<TracingHubFilter>.Instance);

        await filter.OnConnectedAsync(Context(), _ => Task.CompletedTask);
        await filter.OnConnectedAsync(Context(), _ => Task.CompletedTask);
        active().Should().Be(2);

        await filter.OnDisconnectedAsync(Context(), null, (_, _) => Task.CompletedTask);
        active().Should().Be(1);
    }

    [Fact]
    public async Task RejectedConnect_IsNotCounted()
    {
        using var metrics = new HubMetrics();
        var (active, listener) = Listen(metrics);
        using var _ = listener;
        var filter = new TracingHubFilter(metrics, NullLogger<TracingHubFilter>.Instance);

        var act = () => filter.OnConnectedAsync(Context(), _ => throw new HubException("rejected"));

        await act.Should().ThrowAsync<HubException>();
        active().Should().Be(0, "SignalR never calls OnDisconnectedAsync for a connect that threw");
    }
}
