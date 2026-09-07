using DTMS.Fleet.Application.Consumers;
using DTMS.Fleet.IntegrationEvents;
using DTMS.SharedKernel.Storage;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Fleet.UnitTests;

/// <summary>
/// The cleanup consumer used to log the length of the key list once the loop
/// finished, which read as success even when the process was pointed at an
/// unreachable endpoint and every object survived. These tests exist so a
/// count of intentions can never pass for evidence again.
/// </summary>
public class AttachmentOrphanCleanupTests
{
    private readonly IObjectStorageService _storage = Substitute.For<IObjectStorageService>();
    private readonly CapturingLogger<AttachmentObjectsOrphanedConsumer> _log = new();

    private static ConsumeContext<AttachmentObjectsOrphanedIntegrationEvent> Message(params string[] keys)
    {
        var ctx = Substitute.For<ConsumeContext<AttachmentObjectsOrphanedIntegrationEvent>>();
        ctx.Message.Returns(new AttachmentObjectsOrphanedIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, "dtms-attachments", keys));
        ctx.CancellationToken.Returns(CancellationToken.None);
        return ctx;
    }

    private void Outcome(string key, ObjectDeleteOutcome outcome) =>
        _storage.DeleteAsync("dtms-attachments", key, Arg.Any<CancellationToken>()).Returns(outcome);

    [Fact]
    public async Task EverythingRemoved_ReportsSuccess()
    {
        Outcome("a.jpg", ObjectDeleteOutcome.Deleted);
        Outcome("a.thumb.jpg", ObjectDeleteOutcome.Deleted);

        await new AttachmentObjectsOrphanedConsumer(_storage, _log)
            .Consume(Message("a.jpg", "a.thumb.jpg"));

        _log.Worst.Should().Be(LogLevel.Information);
        _log.Text.Should().Contain("2 removed");
    }

    [Fact]
    public async Task ObjectsThatSurvive_AreReportedAsAWarning_NotCountedAsCleared()
    {
        // The exact shape of the incident: the delete is accepted, the object
        // is still there. Nothing throws, so only the outcome distinguishes it.
        Outcome("a.jpg", ObjectDeleteOutcome.Deleted);
        Outcome("a.thumb.jpg", ObjectDeleteOutcome.Failed);

        await new AttachmentObjectsOrphanedConsumer(_storage, _log)
            .Consume(Message("a.jpg", "a.thumb.jpg"));

        _log.Worst.Should().Be(LogLevel.Warning);
        _log.Text.Should().Contain("incomplete").And.Contain("a.thumb.jpg");
    }

    [Fact]
    public async Task AlreadyGone_IsNotAFailure()
    {
        // Redelivery after a successful pass is normal — at-least-once
        // delivery. Treating it as failure would park the message in the DLQ.
        Outcome("a.jpg", ObjectDeleteOutcome.AlreadyAbsent);

        await new AttachmentObjectsOrphanedConsumer(_storage, _log).Consume(Message("a.jpg"));

        _log.Worst.Should().Be(LogLevel.Information);
    }

    [Fact]
    public async Task OneFailingKey_DoesNotStrandTheRest()
    {
        _storage.DeleteAsync("dtms-attachments", "a.jpg", Arg.Any<CancellationToken>())
                .Returns<ObjectDeleteOutcome>(_ => throw new InvalidOperationException("boom"));
        Outcome("b.jpg", ObjectDeleteOutcome.Deleted);

        await new AttachmentObjectsOrphanedConsumer(_storage, _log).Consume(Message("a.jpg", "b.jpg"));

        await _storage.Received(1).DeleteAsync("dtms-attachments", "b.jpg", Arg.Any<CancellationToken>());
        _log.Text.Should().Contain("1 removed");
    }

    [Fact]
    public async Task AFailedDelete_DoesNotThrow_SoTheMessageIsNotDeadLettered()
    {
        // A wrong endpoint cannot be fixed by retrying, and a message that can
        // never succeed would cycle to the DLQ taking the working keys with it.
        Outcome("a.jpg", ObjectDeleteOutcome.Failed);

        var act = async () => await new AttachmentObjectsOrphanedConsumer(_storage, _log)
            .Consume(Message("a.jpg"));

        await act.Should().NotThrowAsync();
    }
}

/// <summary>Records what was logged so a test can assert on severity, which is
/// the thing that actually failed here — not the presence of a message.</summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Message)> _entries = [];

    public LogLevel Worst => _entries.Count == 0 ? LogLevel.None : _entries.Max(e => e.Level);
    public string Text => string.Join(" | ", _entries.Select(e => e.Message));

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
        => _entries.Add((logLevel, formatter(state, exception)));
}
