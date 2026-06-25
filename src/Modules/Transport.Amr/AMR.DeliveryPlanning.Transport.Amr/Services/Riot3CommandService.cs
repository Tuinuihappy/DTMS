using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using AMR.DeliveryPlanning.Transport.Abstractions.Models;
using AMR.DeliveryPlanning.Transport.Abstractions.Services;
using AMR.DeliveryPlanning.Transport.Amr.Models;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Transport.Amr.Services;

// Envelope-only RIOT3 client (Phase b7 removed per-task scheduling).
// Inbound: SendOrderAsync POSTs the multi-mission OrderTemplate envelope.
// Outbound state: vehicle queries via GetVehicleStateAsync; trip-level
// cancel/pause/resume go through SendOrderOperationAsync against the
// upperKey (the envelope key) — not individual tasks.
public class Riot3CommandService : IVehicleCommandService
{
    // Drop null/unset fields so the on-the-wire JSON matches the RIOT3 spec
    // example shape (MOVE missions don't include actionType/blockingType/
    // actionParameters fields at all, etc.). Without this, every nullable
    // property would serialize as `"field": null` and bloat the payload.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<Riot3CommandService> _logger;

    public Riot3CommandService(HttpClient httpClient, ILogger<Riot3CommandService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    // Multi-mission send used by the OrderTemplate instantiate flow.
    // Returns the orderKey RIOT3 minted for the new order on success.
    public async Task<Result<Riot3DispatchResult>> SendOrderAsync(
        Riot3OrderRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Sending RIOT3 order upperKey={UpperKey} with {Count} missions (structureType={Structure})",
            request.UpperKey, request.Missions.Count, request.StructureType);

        // Serialize ONCE and keep the JSON so the caller can snapshot
        // exactly what we transmitted — the same bytes go on the wire
        // and into Trip.VendorRequestSnapshot for forensic queries.
        string requestJson;
        try
        {
            requestJson = JsonSerializer.Serialize(request, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to serialize RIOT3 order upperKey={UpperKey}", request.UpperKey);
            return Result<Riot3DispatchResult>.Failure($"RIOT3 request serialization failed: {ex.Message}");
        }

        try
        {
            using var content = new StringContent(requestJson, System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("/api/v4/orders", content, cancellationToken);
            response.EnsureSuccessStatusCode();

            // RIOT3 returns HTTP 200 even when the order is rejected (e.g. station
            // not on road network → code "E100006"). The success signal is in the
            // response body: code == "0". Anything else is a vendor-side rejection
            // and must surface as a Failure so the caller doesn't create a Trip
            // for an order the vendor never actually accepted.
            var payload = await response.Content.ReadFromJsonAsync<Riot3CreateOrderResponse>(cancellationToken);
            if (payload?.Code != "0")
            {
                var msg = payload?.Message ?? "(no message)";
                _logger.LogWarning("RIOT3 rejected order upperKey={UpperKey}: code={Code} message={Message}",
                    request.UpperKey, payload?.Code ?? "(null)", msg);
                return Result<Riot3DispatchResult>.Failure($"RIOT3 rejected order (code {payload?.Code ?? "null"}): {msg}");
            }

            var orderKey = payload.Data?.OrderKey ?? string.Empty;
            _logger.LogInformation("RIOT3 accepted order upperKey={UpperKey} orderKey={OrderKey}",
                request.UpperKey, orderKey);
            return Result<Riot3DispatchResult>.Success(new Riot3DispatchResult(orderKey, requestJson));
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to send RIOT3 order upperKey={UpperKey}", request.UpperKey);
            return Result<Riot3DispatchResult>.Failure($"RIOT3 API error: {ex.Message}");
        }
    }

    // Envelope-level operation against the vendor's orderKey (Trip.VendorOrderKey).
    // An earlier implementation routed through DTMS upperKey via ?isUpper=true,
    // but RIOT3 silently no-ops the command in that mode — orderState stays
    // unchanged and code "0" is returned, leaving us no signal to retry.
    public Task<Result<Riot3OperationOutcome>> CancelEnvelopeAsync(string vendorOrderKey, CancellationToken cancellationToken = default)
        => SendOrderOperationAsync(vendorOrderKey, Riot3OrderCommandType.Cancel, "cancel", cancellationToken);

    public Task<Result<Riot3OperationOutcome>> PauseEnvelopeAsync(string vendorOrderKey, CancellationToken cancellationToken = default)
        => SendOrderOperationAsync(vendorOrderKey, Riot3OrderCommandType.Hold, "pause", cancellationToken);

    public Task<Result<Riot3OperationOutcome>> ResumeEnvelopeAsync(string vendorOrderKey, CancellationToken cancellationToken = default)
        // CMD_ORDER_CONTINUE_FROM_HELD pairs with CMD_ORDER_HELD; HANG-from-* is for system-initiated hangs.
        => SendOrderOperationAsync(vendorOrderKey, Riot3OrderCommandType.ContinueFromHeld, "resume", cancellationToken);

    // Pair-mate for vendor-initiated hangs (TASK_HANG, e.g. E230025 robot
    // mode change). Sending CONTINUE_FROM_HELD against a HANG order returns
    // E639999 "multi-level template fill error" because the vendor has no
    // held template to fill from.
    public Task<Result<Riot3OperationOutcome>> ResumeFromHangEnvelopeAsync(string vendorOrderKey, CancellationToken cancellationToken = default)
        => SendOrderOperationAsync(vendorOrderKey, Riot3OrderCommandType.ContinueFromHang, "resume-hang", cancellationToken);

    // Robot-level operation. Targets the vendor deviceKey (Trip.VendorVehicleKey),
    // NOT the orderKey — RIOT3 routes PASS by which robot is being acknowledged
    // at the waiting checkpoint, not by the in-flight order.
    public async Task<Result<Riot3OperationOutcome>> PassRobotAsync(
        string vendorVehicleKey, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("RIOT3 robot operation PASS for vendorVehicleKey {VehicleKey}", vendorVehicleKey);

        var request = new Riot3RobotOperationRequest
        {
            Vehicles = new List<Riot3VehicleKey> { new() { Key = vendorVehicleKey } },
            Operation = Riot3RobotOperationType.Pass
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                "/api/v4/robots/operation", request, JsonOptions, cancellationToken);

            // Vendor has no record of the deviceKey — surface a graceful outcome
            // so the caller (AcknowledgeRobotPassCommandHandler) can warn the
            // operator without auto-failing the still-in-flight Trip.
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("RIOT3 PASS: vendor has no record of vehicleKey {VehicleKey} (HTTP 404)",
                    vendorVehicleKey);
                return Result<Riot3OperationOutcome>.Success(Riot3OperationOutcome.NoVendorRecord);
            }
            response.EnsureSuccessStatusCode();

            // Same body-level check as the order ops: RIOT3 returns HTTP 200
            // even on logical failures. Code "0" = accepted; E110014 ("order
            // is empty") is the soft-404 equivalent at the robot endpoint too.
            var payload = await response.Content.ReadFromJsonAsync<Riot3CreateOrderResponse>(cancellationToken);
            if (payload?.Code == "0")
            {
                _logger.LogInformation("RIOT3 PASS accepted for vehicleKey {VehicleKey}", vendorVehicleKey);
                return Result<Riot3OperationOutcome>.Success(Riot3OperationOutcome.Accepted);
            }

            if (payload?.Code == "E110014")
            {
                _logger.LogWarning("RIOT3 PASS: vendor returned E110014 for vehicleKey {VehicleKey}",
                    vendorVehicleKey);
                return Result<Riot3OperationOutcome>.Success(Riot3OperationOutcome.NoVendorRecord);
            }

            var msg = payload?.Message ?? "(no message)";
            _logger.LogWarning("RIOT3 rejected PASS on vehicleKey {VehicleKey}: code={Code} message={Message}",
                vendorVehicleKey, payload?.Code ?? "(null)", msg);
            return Result<Riot3OperationOutcome>.Failure(
                $"RIOT3 rejected pass (code {payload?.Code ?? "null"}): {msg}");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to PASS RIOT3 vehicleKey {VehicleKey}", vendorVehicleKey);
            return Result<Riot3OperationOutcome>.Failure($"RIOT3 pass error: {ex.Message}");
        }
    }

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

    private async Task<Result<Riot3OperationOutcome>> SendOrderOperationAsync(
        string vendorOrderKey,
        string orderCommandType,
        string operationLabel,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("RIOT3 envelope operation {Operation} ({Command}) for vendorOrderKey {OrderKey}",
            operationLabel, orderCommandType, vendorOrderKey);

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
            // Operate by RIOT3's own orderKey, NOT the DTMS upperKey.
            // RIOT3 returns code "0" but silently no-ops when the operation
            // is addressed via ?isUpper=true (verified empirically on IN_QUEUE
            // orders: CMD_ORDER_CANCEL with upperKey leaves orderState
            // unchanged). The orderKey path is the only reliable form.
            var response = await _httpClient.PutAsJsonAsync(
                $"/api/v4/orders/{Uri.EscapeDataString(vendorOrderKey)}/operation",
                envelope,
                cancellationToken);

            // Vendor purged / never received → graceful path for the caller.
            // We surface the outcome rather than throwing so each handler can
            // pick its own policy (Cancel forgives, Pause/Resume escalate).
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("RIOT3 {Operation}: vendor has no record of orderKey {OrderKey} (HTTP 404)",
                    operationLabel, vendorOrderKey);
                return Result<Riot3OperationOutcome>.Success(Riot3OperationOutcome.NoVendorRecord);
            }
            response.EnsureSuccessStatusCode();

            // Same body-level check as SendOrderAsync: RIOT3 returns HTTP 200
            // even on logical failures (e.g. order already finished, vendor
            // can't pause). Code "0" means accepted; E110014 ("order is empty")
            // is the soft-404 equivalent of HTTP 404 and gets the same outcome.
            var payload = await response.Content.ReadFromJsonAsync<Riot3CreateOrderResponse>(cancellationToken);
            if (payload?.Code == "0")
            {
                _logger.LogInformation("RIOT3 {Operation} accepted for orderKey {OrderKey}",
                    operationLabel, vendorOrderKey);
                return Result<Riot3OperationOutcome>.Success(Riot3OperationOutcome.Accepted);
            }

            if (payload?.Code == "E110014")
            {
                _logger.LogWarning("RIOT3 {Operation}: vendor returned E110014 (order is empty) for orderKey {OrderKey}",
                    operationLabel, vendorOrderKey);
                return Result<Riot3OperationOutcome>.Success(Riot3OperationOutcome.NoVendorRecord);
            }

            var msg = payload?.Message ?? "(no message)";
            _logger.LogWarning("RIOT3 rejected {Operation} on orderKey {OrderKey}: code={Code} message={Message}",
                operationLabel, vendorOrderKey, payload?.Code ?? "(null)", msg);
            return Result<Riot3OperationOutcome>.Failure(
                $"RIOT3 rejected {operationLabel} (code {payload?.Code ?? "null"}): {msg}");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to {Operation} RIOT3 envelope {OrderKey}", operationLabel, vendorOrderKey);
            return Result<Riot3OperationOutcome>.Failure($"RIOT3 {operationLabel} error: {ex.Message}");
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
