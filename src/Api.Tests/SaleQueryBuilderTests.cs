using Api.Decision;
using Api.SaleAssist;

namespace Api.Tests;

public class SaleQueryBuilderTests
{
    static Dictionary<string, string?> Answers(params (string Key, string? Value)[] pairs)
        => pairs.ToDictionary(pair => pair.Key, pair => pair.Value);

    static Assessment Chair() => new()
    {
        ObjectName = "Kontorstol",
        Category = "Møbler og indbo",
        Subcategory = "Kontorstol",
        Brand = "IKEA",
        Model = "MARKUS",
    };

    [Fact]
    public void Detected_producer_query_matches_python()
        => Assert.Equal(
            "IKEA MARKUS Kontorstol brugt pris Danmark",
            SaleQueryBuilder.BuildSaleQuery(Chair(), Answers(("producer", "detected"))));

    [Fact]
    public void Detected_marker_is_not_written_into_the_query()
        => Assert.DoesNotContain(
            "detected",
            SaleQueryBuilder.BuildSaleQuery(Chair(), Answers(("producer", "detected"))));

    [Fact]
    public void Premium_bicycle_typos_are_normalized()
        => Assert.Equal(
            "Trek Madone SLR eTap Racercykel brugt pris Danmark",
            SaleQueryBuilder.BuildSaleQuery(
                new Assessment { ObjectName = "Racercykel" },
                Answers(("producer_name", "trek"), ("model_name", "mardone slr etep"))));

    [Fact]
    public void Manual_producer_and_model_come_first()
        => Assert.Equal(
            "Håg Capisco IKEA MARKUS Kontorstol brugt pris Danmark",
            SaleQueryBuilder.BuildSaleQuery(Chair(), Answers(
                ("producer", "other"), ("producer_name", "Håg"), ("model_name", "Capisco"))));

    [Fact]
    public void Sale_name_does_not_repeat_the_subcategory()
        => Assert.Equal(
            "IKEA MARKUS Kontorstol",
            SaleQueryBuilder.BuildSaleObjectName(Chair(), Answers(("producer", "detected"))));

    [Fact]
    public void Sale_name_falls_back_when_nothing_is_known()
        => Assert.Equal("Genstand", SaleQueryBuilder.BuildSaleObjectName(new Assessment(), Answers()));

    [Theory]
    [InlineData("ikea", "IKEA")]
    [InlineData("anden", "")]
    [InlineData("unknown", "")]
    [InlineData("detected", "")]
    [InlineData("ved ikke", "")]
    [InlineData("Håg", "Håg")]
    public void Producer_search_name_filters_marker_values(string producer, string expected)
        => Assert.Equal(expected, SaleQueryBuilder.ProducerSearchName(Answers(("producer", producer))));

    [Fact]
    public void Reshopper_is_relevant_for_children_and_home()
    {
        Assert.True(SaleQueryBuilder.IsReshopperRelevant(
            new Assessment { ObjectName = "Barnevogn", Category = "Børn og baby", Subcategory = "Barnevogn" }));
        Assert.True(SaleQueryBuilder.IsReshopperRelevant(Chair()));
    }

    [Fact]
    public void Reshopper_is_excluded_for_bicycles()
        => Assert.False(SaleQueryBuilder.IsReshopperRelevant(
            new Assessment { ObjectName = "Racercykel", Category = "Cykler", Subcategory = "Racercykel" }));

    [Fact]
    public void Marketplace_url_percent_encodes_the_query()
        => Assert.Equal(
            "https://www.facebook.com/marketplace/search/?query=IKEA%20MARKUS%20brugt%20pris%20Danmark",
            SaleQueryBuilder.BuildMarketplaceSearchUrl("IKEA MARKUS brugt pris Danmark"));

    [Fact]
    public void Redundant_parts_contained_in_longer_ones_are_dropped()
        => Assert.Equal(
            ["Madone SLR"],
            SaleQueryBuilder.CompactRedundantParts(["Madone", "Madone SLR"]));

    [Fact]
    public void Partial_word_matches_are_not_treated_as_redundant()
        => Assert.Equal(
            ["Trek", "Trekant"],
            SaleQueryBuilder.CompactRedundantParts(["Trek", "Trekant"]));

    [Theory]
    [InlineData("yes", "no", "Møbler og indbo · virker · ingen kendte skader")]
    [InlineData("partly", "minor", "Møbler og indbo · virker delvist · mindre skader/slitage")]
    [InlineData("no", "major", "Møbler og indbo · virker ikke · store skader")]
    public void Details_join_with_the_danish_separator(string works, string damage, string expected)
        => Assert.Equal(expected, SaleQueryBuilder.BuildSaleDetails(
            Chair(), Answers(("works", works), ("damage", damage))));
}
