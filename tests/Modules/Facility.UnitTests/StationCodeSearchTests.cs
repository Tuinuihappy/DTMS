using DTMS.Facility.Domain.Services;
using FluentAssertions;

namespace Facility.UnitTests;

public class StationCodeSearchTests
{
    [Theory]
    [InlineData("shelf11", "SHELF11")]
    [InlineData("  stf_02 ", "STF_02")]
    [InlineData("STF05-FEEDER-OUT", "STF05-FEEDER-OUT")]
    public void NormalizeExact_TrimsAndUppercases_ButKeepsSeparators(string input, string expected)
    {
        // Resolution must match the stored form (Station.SetCode = TRIM+UPPER)
        // without loosening it — separators are part of the code's identity.
        StationCodeSearch.NormalizeExact(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("stf-02", "STF02")]
    [InlineData("  stf_02 ", "STF02")]
    [InlineData("STF 02", "STF02")]
    [InlineData("stf05_feeder_in", "STF05FEEDERIN")]
    [InlineData("29", "29")]
    public void NormalizeQuery_StripsSeparatorsAndUppercases(string input, string expected)
    {
        StationCodeSearch.NormalizeQuery(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("_")]
    [InlineData("_- ")]
    public void NormalizeQuery_SeparatorOnlyInput_ReturnsEmpty(string input)
    {
        // Callers must treat "" as no filter, not match-everything semantics.
        StationCodeSearch.NormalizeQuery(input).Should().BeEmpty();
    }

    [Fact]
    public void SearchSemantics_SeparatorBlindQuery_MatchesStoredCode()
    {
        // Mirrors the StationRepository predicate: both sides stripped of
        // separators, stored side already upper-cased.
        const string storedCode = "STF_02";
        var strippedStored = storedCode.Replace("_", "").Replace("-", "").Replace(" ", "");

        strippedStored.Should().Contain(StationCodeSearch.NormalizeQuery("stf02"));
        strippedStored.Should().Contain(StationCodeSearch.NormalizeQuery("STF-02"));
        strippedStored.Should().Contain(StationCodeSearch.NormalizeQuery("stf 02"));
    }

    [Fact]
    public void ExactSemantics_NeverStripsSeparators()
    {
        // "STF_2" must stay "STF_2" so it cannot accidentally equal a
        // stripped "STF_29"-style code during resolution.
        StationCodeSearch.NormalizeExact("stf_2").Should().Be("STF_2");
        StationCodeSearch.NormalizeExact("stf_2").Should().NotBe("STF_29");
    }
}
