using System.Diagnostics.Metrics;
using DTMS.DeliveryOrder.Application.Diagnostics;
using DTMS.DeliveryOrder.Application.Services;
using FluentAssertions;
using R = DTMS.DeliveryOrder.Application.Diagnostics.OrderIntakeMetrics.Reasons;

namespace DeliveryOrder.UnitTests;

public class OrderIntakeMetricsTests
{
    // Every failure message StationValidationService produces, pinned to the
    // reason it must report — a reworded message that slips to "invalid" fails
    // here instead of silently emptying a Grafana panel.
    [Theory]
    [InlineData("'SHELF9' is not a valid station code.", R.StationUnknown, "SHELF9")]
    [InlineData("'PREGNANT' is deactivated and cannot be used for new orders.", R.StationInactive, "PREGNANT")]
    [InlineData("'STF_05' is currently offline (manual override).", R.StationOffline, "STF_05")]
    [InlineData("'STF_05' is currently offline (manual override): blocked aisle", R.StationOffline, "STF_05")]
    [InlineData("'LOC-999' is not a valid WMS location.", R.WmsLocationUnknown, "LOC-999")]
    [InlineData("'LOC-000312' is no longer active in WMS and cannot be used for new orders.", R.WmsLocationInactive, "LOC-000312")]
    [InlineData("something else entirely", R.Invalid, null)]
    public void ClassifyFailure_MapsEachValidationMessage(string error, string reason, string? code)
        => StationValidationService.ClassifyFailure(error).Should().Be((reason, code));

    [Fact]
    public void Rejections_CarryPathSystemReasonAndLocation_RoutesCountOncePerRoute()
    {
        using var metrics = new OrderIntakeMetrics();
        var seen = new List<(string Name, long Value, Dictionary<string, object?> Tags)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (inst, l) =>
            {
                if (inst.Meter.Name == OrderIntakeMetrics.MeterName) l.EnableMeasurementEvents(inst);
            },
        };
        listener.SetMeasurementEventCallback<long>((inst, value, tags, _) =>
            seen.Add((inst.Name, value, tags.ToArray().ToDictionary(t => t.Key, t => t.Value))));
        listener.Start();

        metrics.Accepted(OrderIntakeMetrics.Paths.Source, "oms");
        metrics.Rejected(OrderIntakeMetrics.Paths.Source, "oms", R.StationInactive, "PREGNANT");
        metrics.RoutesRejected(OrderIntakeMetrics.Paths.BulkSubmit, null, ["SHELF3 → PACK-1", "SHELF8 → AHF_06"]);

        seen.Should().ContainSingle(m => m.Name == "dtms.order_intake.accepted.total"
            && (string)m.Tags["path"]! == "source" && (string)m.Tags["system"]! == "oms");
        seen.Should().ContainSingle(m => m.Name == "dtms.order_intake.rejected.total"
            && (string)m.Tags["reason"]! == R.StationInactive && (string)m.Tags["location"]! == "PREGNANT");
        seen.Where(m => (string?)m.Tags.GetValueOrDefault("reason") == R.RouteNotDispatchable)
            .Select(m => ((string)m.Tags["location"]!, (string)m.Tags["system"]!))
            .Should().Equal(("SHELF3 → PACK-1", "unknown"), ("SHELF8 → AHF_06", "unknown"));
    }
}
