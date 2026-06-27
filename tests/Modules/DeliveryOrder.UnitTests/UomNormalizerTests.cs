using DTMS.DeliveryOrder.Application.Options;
using DTMS.DeliveryOrder.Application.Services;
using DTMS.DeliveryOrder.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace DeliveryOrder.UnitTests;

public class UomNormalizerTests
{
    private static UomNormalizer Build(Dictionary<string, string[]>? aliases = null)
    {
        var opts = Options.Create(new UomOptions { Aliases = aliases ?? new Dictionary<string, string[]>() });
        return new UomNormalizer(opts);
    }

    [Fact]
    public void CanonicalNames_AreAlwaysAccepted_EvenWithoutAliasConfig()
    {
        var sut = Build();

        sut.Normalize("KG").Should().Be(UnitOfMeasure.KG);
        sut.Normalize("EA").Should().Be(UnitOfMeasure.EA);
        sut.Normalize("BOX").Should().Be(UnitOfMeasure.BOX);
    }

    [Fact]
    public void Aliases_ResolveToCanonical()
    {
        var sut = Build(new Dictionary<string, string[]>
        {
            ["KG"] = new[] { "kgm", "kilogram", "กก" },
            ["EA"] = new[] { "PCS", "piece", "ชิ้น" },
        });

        sut.Normalize("kgm").Should().Be(UnitOfMeasure.KG);
        sut.Normalize("kilogram").Should().Be(UnitOfMeasure.KG);
        sut.Normalize("กก").Should().Be(UnitOfMeasure.KG);
        sut.Normalize("PCS").Should().Be(UnitOfMeasure.EA);
        sut.Normalize("piece").Should().Be(UnitOfMeasure.EA);
        sut.Normalize("ชิ้น").Should().Be(UnitOfMeasure.EA);
    }

    [Fact]
    public void Lookup_IsCaseInsensitive_And_TrimsWhitespace()
    {
        var sut = Build(new Dictionary<string, string[]>
        {
            ["EA"] = new[] { "PCS" },
        });

        sut.Normalize("ea").Should().Be(UnitOfMeasure.EA);
        sut.Normalize("Ea").Should().Be(UnitOfMeasure.EA);
        sut.Normalize("  EA  ").Should().Be(UnitOfMeasure.EA);
        sut.Normalize("pcs").Should().Be(UnitOfMeasure.EA);
    }

    [Fact]
    public void UnknownInput_ReturnsNull()
    {
        var sut = Build();

        sut.Normalize("moo").Should().BeNull();
        sut.Normalize("🐮").Should().BeNull();
        sut.Normalize("xyz").Should().BeNull();
    }

    [Fact]
    public void NullOrWhitespace_ReturnsNull()
    {
        var sut = Build();

        sut.Normalize(null).Should().BeNull();
        sut.Normalize("").Should().BeNull();
        sut.Normalize("   ").Should().BeNull();
    }

    [Fact]
    public void InvalidCanonicalKeyInConfig_IsSkipped()
    {
        // A typo in the canonical key (e.g. "KGS" instead of "KG") shouldn't
        // crash the normalizer; the row is silently skipped and the rest still
        // works.
        var sut = Build(new Dictionary<string, string[]>
        {
            ["KGS"] = new[] { "bogus" },   // invalid canonical — ignored
            ["EA"]  = new[] { "PCS" },
        });

        sut.Normalize("bogus").Should().BeNull();
        sut.Normalize("PCS").Should().Be(UnitOfMeasure.EA);
    }
}
