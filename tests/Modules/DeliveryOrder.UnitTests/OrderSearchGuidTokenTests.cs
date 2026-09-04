using DTMS.DeliveryOrder.Infrastructure.Projections;
using FluentAssertions;

namespace DeliveryOrder.UnitTests;

/// <summary>
/// The orders list searches a Postgres tsvector built from
/// <c>OrderListViewProjectionStore.BuildSearchText</c>, which writes the
/// order id in dash-less "N" form. The Order ID column in the UI copies
/// the dashed form, so the query side has to bridge the two spellings —
/// otherwise pasting a copied id silently matches nothing.
/// </summary>
public class OrderSearchGuidTokenTests
{
    [Fact]
    public void Dashed_guid_is_normalized_to_the_indexed_N_form()
    {
        var id = Guid.Parse("f2cef188-f477-45aa-a51a-1b2c3d4e5f60");

        var query = OrderListViewReadRepository.SanitizeQuery(id.ToString());

        query.Should().Be("f2cef188f47745aaa51a1b2c3d4e5f60:*");
    }

    [Fact]
    public void Braced_and_uppercase_guids_normalize_to_the_same_token()
    {
        var expected = "f2cef188f47745aaa51a1b2c3d4e5f60:*";

        OrderListViewReadRepository.SanitizeQuery("{F2CEF188-F477-45AA-A51A-1B2C3D4E5F60}")
            .Should().Be(expected);
        OrderListViewReadRepository.SanitizeQuery("F2CEF188-F477-45AA-A51A-1B2C3D4E5F60")
            .Should().Be(expected);
    }

    [Fact]
    public void Guid_already_in_N_form_is_left_alone()
    {
        const string n = "f2cef188f47745aaa51a1b2c3d4e5f60";

        OrderListViewReadRepository.SanitizeQuery(n).Should().Be($"{n}:*");
    }

    [Theory]
    // The visible prefix of the Order ID column — must stay a prefix term.
    [InlineData("f2cef188", "f2cef188:*")]
    [InlineData("OD-0450-WIP", "OD-0450-WIP:*")]
    [InlineData("Miw", "Miw:*")]
    public void Non_guid_terms_keep_their_existing_behaviour(string raw, string expected)
    {
        OrderListViewReadRepository.SanitizeQuery(raw).Should().Be(expected);
    }

    [Fact]
    public void Mixed_terms_normalize_only_the_guid_and_stay_ANDed()
    {
        var query = OrderListViewReadRepository.SanitizeQuery(
            "f2cef188-f477-45aa-a51a-1b2c3d4e5f60 WIP");

        query.Should().Be("f2cef188f47745aaa51a1b2c3d4e5f60:* & WIP:*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("&|!()")]
    public void Degenerate_input_still_yields_no_query(string raw)
    {
        OrderListViewReadRepository.SanitizeQuery(raw).Should().BeEmpty();
    }
}
