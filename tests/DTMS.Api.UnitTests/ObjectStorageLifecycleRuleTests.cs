using DTMS.Api.Infrastructure.Storage;
using FluentAssertions;
using Minio.DataModel.ILM;

namespace DTMS.Api.UnitTests;

// The decision half of EnsureExpiryRuleAsync. S3 lifecycle is written as a
// whole document, so the two things that can go wrong here are silent: losing
// a rule somebody else added, and re-PUTting an unchanged document on every
// boot. Neither shows up as an error from a real MinIO.
public class ObjectStorageLifecycleRuleTests
{
    private const string RuleId = "dtms-incoming-expiry";
    private const string Prefix = "incoming/";

    private static LifecycleRule Foreign() => new()
    {
        ID = "someone-elses-rule",
        Status = LifecycleRule.LifecycleRuleStatusEnabled,
        Filter = new RuleFilter { Prefix = "archive/" },
        Expiration = new Expiration { Days = 365 }
    };

    private static LifecycleRule Ours(int days, string prefix = Prefix, string? status = null) => new()
    {
        ID = RuleId,
        Status = status ?? LifecycleRule.LifecycleRuleStatusEnabled,
        Filter = new RuleFilter { Prefix = prefix },
        Expiration = new Expiration { Days = days }
    };

    [Fact]
    public void Adds_the_rule_when_the_bucket_has_no_lifecycle_document()
    {
        var plan = MinioObjectStorageService.PlanExpiryRule(null, RuleId, Prefix, 1);

        plan.Should().NotBeNull();
        plan!.Rules.Should().ContainSingle();

        var rule = plan.Rules[0];
        rule.ID.Should().Be(RuleId);
        rule.Status.Should().Be(LifecycleRule.LifecycleRuleStatusEnabled);
        rule.Filter!.Prefix.Should().Be(Prefix);
        rule.Expiration!.Days.Should().Be(1);
    }

    [Fact]
    public void Keeps_rules_it_did_not_create()
    {
        var existing = new LifecycleConfiguration(new List<LifecycleRule> { Foreign() });

        var plan = MinioObjectStorageService.PlanExpiryRule(existing, RuleId, Prefix, 1);

        plan.Should().NotBeNull();
        plan!.Rules.Select(r => r.ID).Should().BeEquivalentTo(["someone-elses-rule", RuleId]);
    }

    [Fact]
    public void Returns_null_when_the_rule_is_already_exactly_right()
    {
        var existing = new LifecycleConfiguration(new List<LifecycleRule> { Foreign(), Ours(1) });

        MinioObjectStorageService.PlanExpiryRule(existing, RuleId, Prefix, 1).Should().BeNull();
    }

    [Fact]
    public void Replaces_its_own_rule_in_place_rather_than_adding_a_second()
    {
        var existing = new LifecycleConfiguration(new List<LifecycleRule> { Foreign(), Ours(7) });

        var plan = MinioObjectStorageService.PlanExpiryRule(existing, RuleId, Prefix, 1);

        plan.Should().NotBeNull();
        plan!.Rules.Should().HaveCount(2);
        plan.Rules.Single(r => r.ID == RuleId).Expiration!.Days.Should().Be(1);
    }

    [Fact]
    public void Rewrites_when_only_the_prefix_moved()
    {
        var existing = new LifecycleConfiguration(new List<LifecycleRule> { Ours(1, prefix: "staging/") });

        var plan = MinioObjectStorageService.PlanExpiryRule(existing, RuleId, Prefix, 1);

        plan.Should().NotBeNull();
        plan!.Rules.Single().Filter!.Prefix.Should().Be(Prefix);
    }

    // Zero retention is the off switch, not a delete instruction — MinIO
    // rejects Expiration.Days below 1 even on a rule it will never run, so the
    // day count stays legal and Status carries the intent.
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Non_positive_retention_disables_the_rule_instead_of_dropping_it(int days)
    {
        var plan = MinioObjectStorageService.PlanExpiryRule(null, RuleId, Prefix, days);

        plan.Should().NotBeNull();
        var rule = plan!.Rules.Single();
        rule.Status.Should().Be(LifecycleRule.LifecycleRuleStatusDisabled);
        rule.Expiration!.Days.Should().Be(1);
    }

    [Fact]
    public void Re_enabling_a_disabled_rule_is_a_change()
    {
        var existing = new LifecycleConfiguration(new List<LifecycleRule>
        {
            Ours(1, status: LifecycleRule.LifecycleRuleStatusDisabled)
        });

        var plan = MinioObjectStorageService.PlanExpiryRule(existing, RuleId, Prefix, 1);

        plan.Should().NotBeNull();
        plan!.Rules.Single().Status.Should().Be(LifecycleRule.LifecycleRuleStatusEnabled);
    }

    [Fact]
    public void Rejects_an_empty_rule_id()
    {
        var act = () => MinioObjectStorageService.PlanExpiryRule(null, "  ", Prefix, 1);

        act.Should().Throw<ArgumentException>();
    }
}
