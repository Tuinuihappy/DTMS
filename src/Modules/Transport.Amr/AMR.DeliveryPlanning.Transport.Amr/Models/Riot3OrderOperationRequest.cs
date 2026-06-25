using System.Text.Json.Serialization;

namespace AMR.DeliveryPlanning.Transport.Amr.Models;

// Matches RIOT3.0 API v4 "Order Operation" — PUT /api/v4/orders/{orderkey}/operation
// Spec wraps the command in an "orderCommand" envelope.
public class Riot3OrderOperationEnvelope
{
    [JsonPropertyName("orderCommand")]
    public Riot3OrderOperationRequest OrderCommand { get; set; } = new();
}

public class Riot3OrderOperationRequest
{
    [JsonPropertyName("orderCommandType")]
    public string OrderCommandType { get; set; } = string.Empty;

    // Whether to deactivate (take offline) the robot as part of the
    // operation. Spec marks this required; default false keeps the
    // robot online for the next order.
    [JsonPropertyName("disableVehicle")]
    public bool DisableVehicle { get; set; }
}

public static class Riot3OrderCommandType
{
    public const string Cancel = "CMD_ORDER_CANCEL";
    public const string Rejected = "CMD_ORDER_REJECTED";
    public const string Priority = "CMD_ORDER_PRIORITY";
    public const string ContinueFromRejected = "CMD_ORDER_CONTINUE_FROM_REJECTED";

    // Operator-initiated pause / resume pair
    public const string Hold = "CMD_ORDER_HELD";
    public const string ContinueFromHeld = "CMD_ORDER_CONTINUE_FROM_HELD";

    // Recovery from system-initiated hang
    public const string JumpFromHang = "CMD_ORDER_JUMP_FROM_HANG";
    public const string ContinueFromHang = "CMD_ORDER_CONTINUE_FROM_HANG";
}
