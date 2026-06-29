using DTMS.Iam.Application.Callbacks;
using DTMS.SharedKernel.Outbox;
using Microsoft.Extensions.Logging;

namespace DTMS.Iam.Infrastructure.Callbacks;

/// <summary>
/// Default dispatcher that records what *would* have been sent but
/// makes no external call. Useful in dev / integration tests, and as
/// the safe default when admin hasn't yet configured a real
/// <see cref="HttpSourceCallbackDispatcher" /> for a given system.
///
/// <para>Replace via DI with the HTTP implementation in S.3.1.</para>
/// </summary>
public sealed class LoggingSourceCallbackDispatcher : ISourceCallbackDispatcher
{
    private readonly ILogger<LoggingSourceCallbackDispatcher> _log;

    public LoggingSourceCallbackDispatcher(ILogger<LoggingSourceCallbackDispatcher> log)
    {
        _log = log;
    }

    public Task DispatchAsync(string systemKey, OutboxMessage message, CancellationToken ct)
    {
        _log.LogInformation(
            "[S.3 stub] Would dispatch outbox row {OutboxId} (type={Type}) to system={SystemKey}; " +
            "payload bytes={Bytes}; attempts={Attempts}",
            message.Id, message.Type, systemKey, message.Content.Length, message.RetryCount);
        return Task.CompletedTask;
    }
}
