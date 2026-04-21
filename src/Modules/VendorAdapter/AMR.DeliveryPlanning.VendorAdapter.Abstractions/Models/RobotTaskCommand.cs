namespace AMR.DeliveryPlanning.VendorAdapter.Abstractions.Models;

public class RobotTaskCommand
{
    public Guid TaskId { get; set; }
    public RobotActionType Action { get; set; }
    public string? TargetNodeId { get; set; }
    public double? TargetX { get; set; }
    public double? TargetY { get; set; }
    public double? TargetTheta { get; set; }
    public Dictionary<string, string>? AdditionalParameters { get; set; }
}
