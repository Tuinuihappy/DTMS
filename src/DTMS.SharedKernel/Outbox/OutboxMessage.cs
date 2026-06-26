namespace DTMS.SharedKernel.Outbox;

public class OutboxMessage
{
    public Guid Id { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public string Content { get; private set; } = string.Empty;
    public DateTime OccurredOnUtc { get; private set; }
    public DateTime? ProcessedOnUtc { get; private set; }
    public string? Error { get; private set; }
    public int RetryCount { get; private set; }
    public DateTime? NextRetryAtUtc { get; private set; }

    public bool HasReachedMaxRetries => RetryCount >= OutboxRetryPolicy.MaxRetries;

    private OutboxMessage() { } // For EF Core

    public OutboxMessage(Guid id, string type, string content, DateTime occurredOnUtc)
    {
        Id = id;
        Type = type;
        Content = content;
        OccurredOnUtc = occurredOnUtc;
    }

    public void MarkAsProcessed(DateTime processedOnUtc)
    {
        ProcessedOnUtc = processedOnUtc;
        Error = null;
        NextRetryAtUtc = null;
    }

    public void MarkAsFailed(DateTime attemptedAtUtc, string error)
    {
        RetryCount++;
        Error = error;

        var nextDelay = OutboxRetryPolicy.GetNextRetryDelay(RetryCount);
        if (nextDelay.HasValue)
        {
            NextRetryAtUtc = attemptedAtUtc.Add(nextDelay.Value);
        }
        else
        {
            // Max retries reached — terminal failure; stop polling this row.
            ProcessedOnUtc = attemptedAtUtc;
            NextRetryAtUtc = null;
        }
    }
}
