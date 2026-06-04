using System.Net.Http.Json;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using AMR.DeliveryPlanning.VendorAdapter.Abstractions.Models;
using AMR.DeliveryPlanning.VendorAdapter.Abstractions.Services;
using AMR.DeliveryPlanning.VendorAdapter.Riot3.Models;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.VendorAdapter.Riot3.Services;

// Envelope-only RIOT3 client (Phase b7 removed per-task scheduling).
// Inbound: SendOrderAsync POSTs the multi-mission OrderTemplate envelope.
// Outbound state: vehicle queries via GetVehicleStateAsync; trip-level
// cancel/pause/resume go through SendOrderOperationAsync against the
// upperKey (the envelope key) — not individual tasks.
public class Riot3CommandService : IVehicleCommandService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<Riot3CommandService> _logger;

    public Riot3CommandService(HttpClient httpClient, ILogger<Riot3CommandService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    // Multi-mission send used by the OrderTemplate instantiate flow.
    // Returns the orderKey RIOT3 minted for the new order on success.
    public async Task<Result<string>> SendOrderAsync(
        Riot3OrderRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Sending RIOT3 order upperKey={UpperKey} with {Count} missions (structureType={Structure})",
            request.UpperKey, request.Missions.Count, request.StructureType);

        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/v4/orders", request, cancellationToken);
            response.EnsureSuccessStatusCode();

            // RIOT3 returns the created order; parse out the orderKey so the
            // caller can correlate later callbacks.
            var payload = await response.Content.ReadFromJsonAsync<Riot3CreateOrderResponse>(cancellationToken);
            var orderKey = payload?.Data?.OrderKey ?? string.Empty;
            _logger.LogInformation("RIOT3 accepted order upperKey={UpperKey} orderKey={OrderKey}",
                request.UpperKey, orderKey);
            return Result<string>.Success(orderKey);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to send RIOT3 order upperKey={UpperKey}", request.UpperKey);
            return Result<string>.Failure($"RIOT3 API error: {ex.Message}");
        }
    }

    // Envelope-level operation (cancel / pause / resume an entire upperKey).
    public Task<Result> CancelEnvelopeAsync(string upperKey, CancellationToken cancellationToken = default)
        => SendOrderOperationAsync(upperKey, Riot3OrderCommandType.Cancel, "cancel", cancellationToken);

    public Task<Result> PauseEnvelopeAsync(string upperKey, CancellationToken cancellationToken = default)
        => SendOrderOperationAsync(upperKey, Riot3OrderCommandType.Hold, "pause", cancellationToken);

    public Task<Result> ResumeEnvelopeAsync(string upperKey, CancellationToken cancellationToken = default)
        // CMD_ORDER_CONTINUE_FROM_HELD pairs with CMD_ORDER_HELD; HANG-from-* is for system-initiated hangs.
        => SendOrderOperationAsync(upperKey, Riot3OrderCommandType.ContinueFromHeld, "resume", cancellationToken);

    public async Task<StandardRobotState?> GetVehicleStateAsync(Guid vehicleId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<Riot3VehicleResponse>(
                $"/api/v4/robots/{vehicleId}",
                cancellationToken);

            if (response == null) return null;

            return new StandardRobotState
            {
                VehicleId = vehicleId,
                State = MapSystemState(response.SystemState),
                BatteryLevel = response.BatteryLevel / 100.0,
                CurrentX = response.Position?.X,
                CurrentY = response.Position?.Y,
                Timestamp = DateTime.UtcNow
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to get vehicle state for {VehicleId} from RIOT3", vehicleId);
            return null;
        }
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private async Task<Result> SendOrderOperationAsync(
        string upperKey,
        string orderCommandType,
        string operationLabel,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("RIOT3 envelope operation {Operation} ({Command}) for upperKey {UpperKey}",
            operationLabel, orderCommandType, upperKey);

        var envelope = new Riot3OrderOperationEnvelope
        {
            OrderCommand = new Riot3OrderOperationRequest
            {
                OrderCommandType = orderCommandType,
                DisableVehicle = false
            }
        };

        try
        {
            var response = await _httpClient.PutAsJsonAsync(
                $"/api/v4/orders/{Uri.EscapeDataString(upperKey)}/operation?isUpper=true",
                envelope,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            return Result.Success();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to {Operation} RIOT3 envelope {UpperKey}", operationLabel, upperKey);
            return Result.Failure($"RIOT3 {operationLabel} error: {ex.Message}");
        }
    }

    private static StandardState MapSystemState(string systemState) => systemState?.ToUpperInvariant() switch
    {
        "IDLE" => StandardState.Idle,
        "BUSY" => StandardState.Moving,
        "ERROR" => StandardState.Error,
        "CHARGING" => StandardState.Charging,
        "MAINTENANCE" => StandardState.Maintenance,
        _ => StandardState.Offline
    };
}
