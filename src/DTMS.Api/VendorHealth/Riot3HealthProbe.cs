using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace DTMS.Api.VendorHealth;

public sealed class Riot3HealthProbe : IRiot3HealthProbe
{
    private readonly HttpClient _http;
    private readonly IOptionsMonitor<VendorHealthOptions> _options;
    private readonly ILogger<Riot3HealthProbe> _logger;

    public Riot3HealthProbe(
        HttpClient http,
        IOptionsMonitor<VendorHealthOptions> options,
        ILogger<Riot3HealthProbe> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
    }

    public async Task<ProbeOutcome> ProbeAsync(CancellationToken ct)
    {
        var opts = _options.CurrentValue.Riot3;
        var path = string.IsNullOrWhiteSpace(opts.HealthPath) ? "/api/v4/health" : opts.HealthPath;

        var sw = Stopwatch.StartNew();
        try
        {
            using var response = await _http.GetAsync(path, ct);
            sw.Stop();

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return new ProbeOutcome(
                    ProbeOutcomeKind.Auth, Code: null, Message: null,
                    LatencyMs: (int)sw.ElapsedMilliseconds,
                    FailureReason: "RIOT3 reachable but ApiKey is invalid (401)");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new ProbeOutcome(
                    ProbeOutcomeKind.Failure, Code: null, Message: null,
                    LatencyMs: (int)sw.ElapsedMilliseconds,
                    FailureReason: $"RIOT3 returned HTTP {(int)response.StatusCode}");
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            string? code = null;
            string? message = null;
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("code", out var c))
                    code = c.ValueKind == JsonValueKind.String ? c.GetString() : c.ToString();
                if (doc.RootElement.TryGetProperty("message", out var m))
                    message = m.GetString();
            }
            catch (JsonException ex)
            {
                return new ProbeOutcome(
                    ProbeOutcomeKind.Failure, Code: null, Message: null,
                    LatencyMs: (int)sw.ElapsedMilliseconds,
                    FailureReason: $"RIOT3 returned invalid JSON: {ex.Message}");
            }

            if (code != "0")
            {
                return new ProbeOutcome(
                    ProbeOutcomeKind.Failure, code, message,
                    LatencyMs: (int)sw.ElapsedMilliseconds,
                    FailureReason: $"RIOT3 returned code={code}");
            }

            return new ProbeOutcome(
                ProbeOutcomeKind.Success, code, message,
                LatencyMs: (int)sw.ElapsedMilliseconds,
                FailureReason: null);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            sw.Stop();
            return new ProbeOutcome(
                ProbeOutcomeKind.Failure, Code: null, Message: null,
                LatencyMs: (int)sw.ElapsedMilliseconds,
                FailureReason: $"RIOT3 connection timed out (>{opts.TimeoutSeconds}s)");
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            sw.Stop();
            _logger.LogDebug(ex, "RIOT3 health probe failed");
            return new ProbeOutcome(
                ProbeOutcomeKind.Failure, Code: null, Message: null,
                LatencyMs: (int)sw.ElapsedMilliseconds,
                FailureReason: $"RIOT3 unreachable: {ex.Message}");
        }
    }
}
