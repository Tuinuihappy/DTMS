namespace DTMS.Api.VendorHealth;

public sealed class VendorHealthOptions
{
    public Riot3HealthOptions Riot3 { get; set; } = new();

    public InfraHealthOptions Infra { get; set; } = new();
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

public sealed class InfraHealthOptions
{
    public bool Enabled { get; set; } = true;
    // 10s default for infra: each cycle hits postgres + redis + rabbitmq +
    // masstransit. The rabbitmq IHealthCheck opens a fresh TCP connection
    // per call, so going faster is wasteful without an upstream fix.
    public int PollIntervalSeconds { get; set; } = 10;
    public int TimeoutSeconds { get; set; } = 5;
    public int FailureThreshold { get; set; } = 3;
    public int RecoveryThreshold { get; set; } = 2;
    public int StartupDelaySeconds { get; set; } = 15;
    // Prefix prepended to each check's name when stored. Lets the frontend
    // split infra cards from external-vendor cards without a schema field.
    public string VendorNamePrefix { get; set; } = "infra:";
}
