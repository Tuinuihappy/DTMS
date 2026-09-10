using System.Data.Common;
using DTMS.SharedKernel.Projection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Dispatch.UnitTests;

// Guards the classification every projector now shares. The case that
// matters most is the unique violation: it used to be judged permanent,
// so a lost insert race logged "event dropped" and swallowed the message.
public class ProjectionFaultsTests
{
    [Fact]
    public void UniqueViolation_InsideDbUpdateException_IsTransient()
    {
        var ex = new DbUpdateException("insert failed", Pg("23505"));

        ProjectionFaults.IsTransient(ex).Should().BeTrue();
    }

    [Fact]
    public void UniqueViolation_NestedDeeper_IsStillTransient()
    {
        // Providers wrap; don't assume the driver exception sits one level in.
        var ex = new DbUpdateException("insert failed",
            new InvalidOperationException("wrapped", Pg("23505")));

        ProjectionFaults.IsTransient(ex).Should().BeTrue();
    }

    [Fact]
    public void OtherSqlState_IsPermanent()
    {
        // 42703 = undefined_column — a real schema defect. Retrying it just
        // burns the ladder and delays the error queue, so it must stay permanent.
        var ex = new DbUpdateException("insert failed", Pg("42703"));

        ProjectionFaults.IsTransient(ex).Should().BeFalse();
    }

    [Fact]
    public void DbUpdateException_WithNoDbExceptionInside_IsPermanent()
    {
        var ex = new DbUpdateException("insert failed", new InvalidOperationException("nope"));

        ProjectionFaults.IsTransient(ex).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(PreviouslyKnownTransientFaults))]
    public void PreviouslyKnownTransientFaults_StayTransient(Exception ex)
        => ProjectionFaults.IsTransient(ex).Should().BeTrue();

    public static TheoryData<Exception> PreviouslyKnownTransientFaults() => new()
    {
        new DbUpdateConcurrencyException("concurrency"),
        new TimeoutException("timeout"),
        new TaskCanceledException("cancelled"),
    };

    [Fact]
    public void UnrelatedException_IsPermanent()
        => ProjectionFaults.IsTransient(new InvalidOperationException("bug")).Should().BeFalse();

    // Npgsql isn't referenced from SharedKernel (nor needed here) — the
    // classifier reads DbException.SqlState, which is BCL surface, so a
    // stand-in with the right SQLSTATE exercises the real path.
    private static DbException Pg(string sqlState) => new FakeDbException(sqlState);

    private sealed class FakeDbException(string sqlState) : DbException
    {
        public override string? SqlState { get; } = sqlState;
    }
}
