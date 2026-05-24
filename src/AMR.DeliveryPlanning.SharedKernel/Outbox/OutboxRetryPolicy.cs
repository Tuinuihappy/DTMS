namespace AMR.DeliveryPlanning.SharedKernel.Outbox;

public static class OutboxRetryPolicy
{
    public const int MaxRetries = 5;

    public static TimeSpan? GetNextRetryDelay(int failureCount) => failureCount switch
    {
        1 => TimeSpan.FromSeconds(30),
        2 => TimeSpan.FromMinutes(2),
        3 => TimeSpan.FromMinutes(10),
        4 => TimeSpan.FromMinutes(30),
        5 => TimeSpan.FromHours(2),
        _ => null
    };
}
