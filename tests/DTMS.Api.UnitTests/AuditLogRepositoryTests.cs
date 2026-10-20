using DTMS.Iam.Application.Security;
using DTMS.Iam.Domain.Entities;
using DTMS.Iam.Infrastructure.Data;
using DTMS.Iam.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace DTMS.Api.UnitTests;

// Frontend review M1/M3: every system-* / subscription-* audit row names its
// system only inside Details, so the admin page could neither show nor filter
// "everything that happened to oms". The repository now filters on it and
// lists the actions that really occur.
public class AuditLogRepositoryTests
{
    private static (AuditLogRepository repo, IamDbContext db) Create()
    {
        var options = new DbContextOptionsBuilder<IamDbContext>()
            .UseInMemoryDatabase($"audit-{Guid.NewGuid()}")
            .Options;
        var db = new IamDbContext(options, Substitute.For<ICallbackTokenProtector>());
        return (new AuditLogRepository(db), db);
    }

    private static async Task Seed(AuditLogRepository repo)
    {
        await repo.AppendAsync(new PermissionAuditEntry("86347852", "system-credential-rotated", details: "{\"systemKey\":\"oms\"}"));
        await repo.AppendAsync(new PermissionAuditEntry("86347852", "system-callback-token-revealed", details: "{\"systemKey\":\"wms\"}"));
        await repo.AppendAsync(new PermissionAuditEntry("86347852", "system-grant", permissionCode: "dtms:x:y:read", details: "{\"systemKey\":\"oms2\"}"));
        await repo.AppendAsync(new PermissionAuditEntry("11111111", "grant", role: "Admin", permissionCode: "dtms:x:y:read"));
    }

    [Fact]
    public async Task SystemKey_MatchesThatSystemOnly()
    {
        var (repo, _) = Create();
        await Seed(repo);

        var (items, total) = await repo.QueryAsync(null, null, null, 1, 50, systemKey: "oms");

        total.Should().Be(1, "\"oms\" must not also match \"oms2\" or rows without details");
        items.Single().Action.Should().Be("system-credential-rotated");
    }

    [Fact]
    public async Task NoSystemKey_ReturnsEverything()
    {
        var (repo, _) = Create();
        await Seed(repo);

        var (_, total) = await repo.QueryAsync(null, null, null, 1, 50);

        total.Should().Be(4);
    }

    [Fact]
    public async Task DistinctActions_ListsWhatIsInTheLog()
    {
        var (repo, _) = Create();
        await Seed(repo);

        var actions = await repo.DistinctActionsAsync();

        actions.Should().Equal("grant", "system-callback-token-revealed", "system-credential-rotated", "system-grant");
    }
}
