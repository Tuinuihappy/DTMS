using AMR.DeliveryPlanning.SharedKernel.Auth;
using FluentAssertions;

namespace DeliveryOrder.UnitTests;

// P0 — verifies ICurrentActorContext lookup priority + AsyncLocal scope
// isolation. Lives in the DeliveryOrder test project because it's the
// project that already has FluentAssertions wired and SharedKernel
// dependencies — no need to spin up a separate SharedKernel test
// project for a handful of foundation tests.
public class P0ActorContextTests
{
    [Fact]
    public void Current_FallsBackToSystem_WhenNoScopeAndNoHttpResolver()
    {
        var ctx = new AsyncLocalActorContext();
        ctx.Current.Should().Be(ActorContext.System);
        ctx.Current.TriggeredBy.Should().Be("system");
    }

    [Fact]
    public void Current_ReadsFromHttpResolver_WhenNoExplicitScope()
    {
        var fromHttp = new ActorContext(UserId: "ops-01", Source: "http", CorrelationId: null);
        var ctx = new AsyncLocalActorContext(() => fromHttp);

        ctx.Current.Should().Be(fromHttp);
        ctx.Current.TriggeredBy.Should().Be("ops-01");
    }

    [Fact]
    public void BeginScope_OverridesHttpResolver_WhileActive()
    {
        var fromHttp = new ActorContext("ops-01", "http", null);
        var ctx = new AsyncLocalActorContext(() => fromHttp);

        var consumerCtx = new ActorContext(null, "vendor-webhook", Guid.NewGuid());
        using (ctx.BeginScope(consumerCtx))
        {
            ctx.Current.Should().Be(consumerCtx);
            ctx.Current.TriggeredBy.Should().Be("vendor-webhook");
        }

        // After dispose — resume falling back to HTTP resolver
        ctx.Current.Should().Be(fromHttp);
    }

    [Fact]
    public void BeginScope_Nesting_RestoresOuterOnDispose()
    {
        var ctx = new AsyncLocalActorContext();
        var outer = new ActorContext("outer", "scheduled-job", null);
        var inner = new ActorContext("inner", "scheduled-job", null);

        using (ctx.BeginScope(outer))
        {
            ctx.Current.Should().Be(outer);
            using (ctx.BeginScope(inner))
                ctx.Current.Should().Be(inner);
            ctx.Current.Should().Be(outer);
        }
        ctx.Current.Should().Be(ActorContext.System);
    }

    [Fact]
    public async Task BeginScope_IsolatedAcrossAsyncBranches()
    {
        var ctx = new AsyncLocalActorContext();
        var branchA = new ActorContext("branch-a", "http", null);
        var branchB = new ActorContext("branch-b", "scheduled-job", null);

        async Task<string> RunBranch(ActorContext branchContext)
        {
            using (ctx.BeginScope(branchContext))
            {
                await Task.Yield();   // force ambient handoff
                return ctx.Current.TriggeredBy;
            }
        }

        var results = await Task.WhenAll(RunBranch(branchA), RunBranch(branchB));
        results.Should().BeEquivalentTo(new[] { "branch-a", "branch-b" });
    }

    [Fact]
    public void TriggeredBy_FallsBackToSource_WhenUserIdMissing()
    {
        new ActorContext(null, "vendor-webhook", null).TriggeredBy.Should().Be("vendor-webhook");
        new ActorContext("", "scheduled-job", null).TriggeredBy.Should().Be("scheduled-job");
        new ActorContext("   ", "system", null).TriggeredBy.Should().Be("system");
        new ActorContext("ops-01", "http", null).TriggeredBy.Should().Be("ops-01");
    }
}
