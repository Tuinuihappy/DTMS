namespace AMR.DeliveryPlanning.Api.VendorHealth;

public sealed class VendorHealthOptions
{
    public Riot3HealthOptions Riot3 { get; set; } = new();
}

public sealed class Riot3HealthOptions
{
    public bool Enabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 5;
    public int TimeoutSeconds { get; set; } = 3;
    public int FailureThreshold { get; set; } = 3;
    public int RecoveryThreshold { get; set; } = 2;
    public string HealthPath { get; set; } = "/api/v4/health";
    public int StartupDelaySeconds { get; set; } = 10;
}
