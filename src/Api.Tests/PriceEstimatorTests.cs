using Api.Decision;
using Api.SaleAssist;

namespace Api.Tests;

public class PriceEstimatorTests
{
    static Dictionary<string, string?> Answers(params (string Key, string? Value)[] pairs)
        => pairs.ToDictionary(pair => pair.Key, pair => pair.Value);

    static Assessment Bike() => new() { ObjectName = "Cykel", Category = "Cykler" };
    static Assessment Chair() => new() { ObjectName = "Kontorstol", Category = "Møbler og indbo" };

    [Fact]
    public void Working_known_bicycle_gets_a_price_floor_without_web_prices()
    {
        var estimate = PriceEstimator.Estimate(Bike(), Answers(
            ("works", "yes"), ("damage", "no"), ("accessories", "complete"),
            ("producer_name", "Trek"), ("age", "mid")), []);

        Assert.Equal("Sæt prisen til 2250 kr.", estimate.Label);
        Assert.Contains("Cykelestimat", estimate.Note);
    }

    [Fact]
    public void Furniture_without_web_prices_uses_the_category_range()
    {
        var estimate = PriceEstimator.Estimate(Chair(), Answers(
            ("works", "yes"), ("damage", "no"), ("age", "mid")), []);

        Assert.Equal("Sæt prisen til 550 kr.", estimate.Label);
        Assert.Contains("Foreløbigt prototypeestimat", estimate.Note);
    }

    [Fact]
    public void Web_prices_drive_the_estimate_when_present()
    {
        var estimate = PriceEstimator.Estimate(Chair(), Answers(
            ("works", "yes"), ("damage", "no"), ("age", "mid")), [400, 500, 600]);

        Assert.Equal("Sæt prisen til 500 kr.", estimate.Label);
        Assert.Contains("medianen af lignende webfund", estimate.Note);
    }

    [Fact]
    public void Premium_racer_gets_the_high_floor()
    {
        var estimate = PriceEstimator.Estimate(
            new Assessment { ObjectName = "Racercykel", Category = "Cykler" },
            Answers(("producer_name", "Trek"), ("model_name", "Madone SLR eTap"),
                    ("works", "yes"), ("age", "mid")), []);

        Assert.Equal("Sæt prisen til 34500 kr.", estimate.Label);
    }

    [Theory]
    [InlineData("Trek", "Madone SLR eTap", true)]
    [InlineData("trek", "mardone slr etep", true)]
    [InlineData("Trek", "FX 2", false)]
    [InlineData("Kildemoes", "Carbon", false)]
    public void Premium_detection_needs_producer_and_term(string producer, string model, bool expected)
        => Assert.Equal(expected, PriceEstimator.IsPremiumBicycle(
            Answers(("producer_name", producer), ("model_name", model))));

    [Theory]
    [InlineData("newer", 1.05)]
    [InlineData("mid", 1.0)]
    [InlineData("old", 0.75)]
    [InlineData("unknown", 0.9)]
    [InlineData(null, 1.0)]
    public void Age_factors_match_python(string? age, double expected)
        => Assert.Equal(expected, PriceEstimator.AgePriceFactor(age));

    [Theory]
    [InlineData("no", 1.0)]
    [InlineData("minor", 0.82)]
    [InlineData("major", 0.55)]
    [InlineData(null, 1.0)]
    public void Damage_factors_match_python(string? damage, double expected)
        => Assert.Equal(expected, PriceEstimator.DamagePriceFactor(Answers(("damage", damage))));

    [Fact]
    public void Partly_working_uses_the_major_damage_factor()
        => Assert.Equal(0.55, PriceEstimator.DamagePriceFactor(Answers(("works", "partly"))));

    [Fact]
    public void Outliers_are_trimmed_only_once_there_are_five_prices()
    {
        Assert.Equal([400, 500, 600], PriceEstimator.TrimPriceOutliers([10, 400, 500, 600, 999999], 50, 100000));
        Assert.Equal([400, 500, 600], PriceEstimator.TrimPriceOutliers([400, 500, 600], 50, 100000));
    }

    [Theory]
    [InlineData(137, 125, 150, 1500)]
    public void Rounding_matches_python(double value, int nearest25, int nearest50, int nearest500)
    {
        Assert.Equal(nearest25, PriceEstimator.RoundToNearest25(value));
        Assert.Equal(nearest50, PriceEstimator.RoundToNearest50(value));
        Assert.Equal(nearest500, PriceEstimator.RoundToNearest500(value * 10));
    }
}
