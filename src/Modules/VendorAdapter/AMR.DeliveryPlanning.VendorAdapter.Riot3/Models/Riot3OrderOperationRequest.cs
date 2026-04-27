using System.Text.Json.Serialization;

namespace AMR.DeliveryPlanning.VendorAdapter.Riot3.Models;

public class Riot3OrderOperationRequest
{
    [JsonPropertyName("orderCommandType")]
    public string OrderCommandType { get; set; } = string.Empty;
}

public static class Riot3OrderCommandType
{
    public const string Cancel = "CMD_ORDER_CANCEL";
    public const string Hold = "CMD_ORDER_HELD";
    public const string Resume = "CMD_ORDER_RESUME";
    public const string ChangePriority = "CMD_CHANGE_PRIORITY";
}
