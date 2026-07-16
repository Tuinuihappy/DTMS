using System.Net;
using DTMS.Api.Infrastructure.Outbox;
using DTMS.SharedKernel.Outbox;
using FluentAssertions;

namespace DTMS.Api.UnitTests;

// Permanent-vs-transient classification for partitioned (HTTP callback)
// outbox rows. Driving incident: OMS's create-once /api/shipments returns
// 400 on a duplicate shipment.started — before classification, that poison
// row burned the full backoff (~2h45m) while head-blocking the whole `oms`
// partition. The decision logic is deliberately extracted into a pure
// static (HttpCallbackFailureClassifier.ApplyFailure) precisely so these
// tests need no Postgres — MultiPartitionOutboxProcessor's raw SQL
// (FOR UPDATE SKIP LOCKED) can't run on a test provider.
public class HttpCallbackFailureClassifierTests
{
    private static OutboxMessage Row() => new(
        id: Guid.NewGuid(),
        type: "shipment.started.v1",
        content: "{\"shipmentId\":\"x\"}",
        occurredOnUtc: DateTime.UtcNow,
        partitionKey: "oms");

    private static HttpRequestException HttpError(HttpStatusCode? status) =>
        status is { } s
            ? new HttpRequestException($"Response status code does not indicate success: {(int)s}.", null, s)
            : new HttpRequestException("Connection refused");   // StatusCode stays null

    // (1) Deterministic receiver rejections go terminal in ONE attempt.
    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]            // 400
    [InlineData(HttpStatusCode.MethodNotAllowed)]      // 405
    [InlineData(HttpStatusCode.Gone)]                  // 410
    [InlineData(HttpStatusCode.RequestEntityTooLarge)] // 413
    [InlineData(HttpStatusCode.UnsupportedMediaType)]  // 415
    [InlineData(HttpStatusCode.UnprocessableEntity)]   // 422
    public void ApplyFailure_PermanentStatus_GoesTerminalImmediately(HttpStatusCode status)
    {
        var row = Row();
        var at = DateTime.UtcNow;

        var permanent = HttpCallbackFailureClassifier.ApplyFailure(row, HttpError(status), at);

        permanent.Should().BeTrue();
        row.ProcessedOnUtc.Should().Be(at);
        row.NextRetryAtUtc.Should().BeNull();
        row.RetryCount.Should().Be(1);
        row.Error.Should().StartWith($"[permanent {(int)status}]");
    }

    // (2) Recoverable statuses keep the existing backoff — first failure
    // schedules the 30s retry, row NOT terminal.
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]        // 401 — token fixable on credential row
    [InlineData(HttpStatusCode.Forbidden)]           // 403
    [InlineData(HttpStatusCode.NotFound)]            // 404 — wrong CallbackBaseUrl / deploy window
    [InlineData(HttpStatusCode.RequestTimeout)]      // 408
    [InlineData(HttpStatusCode.TooManyRequests)]     // 429
    [InlineData(HttpStatusCode.InternalServerError)] // 500
    [InlineData(HttpStatusCode.BadGateway)]          // 502
    [InlineData(HttpStatusCode.ServiceUnavailable)]  // 503
    public void ApplyFailure_TransientStatus_KeepsBackoff(HttpStatusCode status)
    {
        var row = Row();
        var at = DateTime.UtcNow;

        var permanent = HttpCallbackFailureClassifier.ApplyFailure(row, HttpError(status), at);

        permanent.Should().BeFalse();
        row.ProcessedOnUtc.Should().BeNull();
        row.NextRetryAtUtc.Should().Be(at + TimeSpan.FromSeconds(30));
        row.RetryCount.Should().Be(1);
        row.Error.Should().NotStartWith("[permanent");
    }

    // (3) Connection-level HttpRequestException (DNS/refused/TLS) has no
    // StatusCode — must classify transient.
    [Fact]
    public void ApplyFailure_NullStatusCode_IsTransient()
    {
        var row = Row();

        var permanent = HttpCallbackFailureClassifier.ApplyFailure(row, HttpError(null), DateTime.UtcNow);

        permanent.Should().BeFalse();
        row.ProcessedOnUtc.Should().BeNull();
    }

    // (4) Non-HTTP exceptions (timeouts, config errors, cache failures) stay
    // transient — config errors must keep retrying so an admin fix
    // (CallbackBaseUrl, bearer token) auto-heals within the backoff window.
    [Theory]
    [InlineData(typeof(TimeoutException))]
    [InlineData(typeof(TaskCanceledException))]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(NotSupportedException))]
    [InlineData(typeof(UriFormatException))]
    public void ApplyFailure_NonHttpException_IsTransient(Type exceptionType)
    {
        var row = Row();
        var ex = (Exception)Activator.CreateInstance(exceptionType, "boom")!;

        var permanent = HttpCallbackFailureClassifier.ApplyFailure(row, ex, DateTime.UtcNow);

        permanent.Should().BeFalse();
        row.ProcessedOnUtc.Should().BeNull();
        row.NextRetryAtUtc.Should().NotBeNull();
    }

    // (5) Mixed history: a row that already failed transiently still goes
    // terminal the moment a permanent status shows up.
    [Fact]
    public void ApplyFailure_AfterTransientHistory_PermanentStillTerminal()
    {
        var row = Row();
        row.MarkAsFailed(DateTime.UtcNow, "503 first");
        row.MarkAsFailed(DateTime.UtcNow, "503 second");
        row.RetryCount.Should().Be(2);

        var at = DateTime.UtcNow;
        var permanent = HttpCallbackFailureClassifier.ApplyFailure(
            row, HttpError(HttpStatusCode.BadRequest), at);

        permanent.Should().BeTrue();
        row.RetryCount.Should().Be(3);
        row.ProcessedOnUtc.Should().Be(at);
        row.NextRetryAtUtc.Should().BeNull();
        row.Error.Should().StartWith("[permanent 400]");
    }

    // (6) Regression pin: the pre-existing transient path is untouched —
    // failures 1-5 schedule per the curve; the 6th goes terminal-in-place.
    [Fact]
    public void MarkAsFailed_SixthFailure_IsTerminal_FifthIsNot()
    {
        var row = Row();
        var at = DateTime.UtcNow;

        for (var i = 0; i < 5; i++) row.MarkAsFailed(at, "transient");
        row.ProcessedOnUtc.Should().BeNull("5th failure still schedules the 2h retry");
        row.NextRetryAtUtc.Should().Be(at + TimeSpan.FromHours(2));

        row.MarkAsFailed(at, "transient");
        row.ProcessedOnUtc.Should().Be(at, "6th failure exhausts the policy");
        row.NextRetryAtUtc.Should().BeNull();
    }

    // (7) Documented asymmetry: a fast-terminal row has NOT "reached max
    // retries" — RetryCount reflects attempts actually made. Intentional;
    // nothing on the partitioned path reads HasReachedMaxRetries.
    [Fact]
    public void MarkAsPermanentlyFailed_DoesNotFakeMaxRetries()
    {
        var row = Row();

        row.MarkAsPermanentlyFailed(DateTime.UtcNow, "[permanent 400] nope");

        row.RetryCount.Should().Be(1);
        row.HasReachedMaxRetries.Should().BeFalse();
        row.ProcessedOnUtc.Should().NotBeNull();
    }
}
