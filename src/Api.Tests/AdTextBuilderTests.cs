using Api.Decision;
using Api.SaleAssist;

namespace Api.Tests;

public class AdTextBuilderTests
{
    static readonly Assessment Lamp = new()
    {
        ObjectName = "Bordlampe", Brand = "IKEA", Model = "FADO",
        Materials = ["glas"], VisibleDamage = ["Ridse på foden"],
    };

    [Fact]
    public void Ad_contains_sales_copy_price_and_known_defects_but_no_internal_advice()
    {
        var answers = new Dictionary<string, string?>
        {
            ["works"] = "partly", ["damage"] = "major", ["accessories"] = "missing",
        };
        var text = AdTextBuilder.BuildAdText("IKEA FADO", Lamp, answers,
            new PriceEstimate("Sæt prisen til 250 kr.", "Pris sat ud fra medianen af webfund."),
            "Jeg sælger min IKEA FADO til dig, der har lyst til et lampeprojekt.");

        Assert.StartsWith("IKEA FADO sælges", text);
        Assert.Contains("lampeprojekt", text);
        Assert.Contains("Pris: 250 kr.", text);
        Assert.Contains("Fungerer kun delvist.", text);
        Assert.Contains("Har større fejl eller skader.", text);
        Assert.Contains("Der mangler tilbehør.", text);
        Assert.Contains("Ridse på foden", text);
        Assert.Contains("Materialer: glas", text);
        foreach (var forbidden in new[] { "appen", "webfund", "medianen", "Sæt prisen", "bør", "skal kontrolleres" })
            Assert.DoesNotContain(forbidden, text);
    }

    [Fact]
    public void Fallback_is_a_sales_ad_without_unverified_working_condition_or_delivery_claims()
    {
        var text = AdTextBuilder.BuildAdText("Bordlampe", new Assessment(),
            new Dictionary<string, string?>(), new PriceEstimate("Sæt prisen til 100 kr.", "Prototypeestimat"));
        Assert.Contains("Jeg sælger Bordlampe.", text);
        Assert.Contains("Interesseret?", text);
        foreach (var forbidden in new[] { "Fungerer", "Ingen kendte skader", "afhentes", "Prototype", "billeder og oplysninger" })
            Assert.DoesNotContain(forbidden, text);
    }

    [Theory]
    [InlineData("no", "Virker ikke og sælges til reparation eller reservedele.")]
    [InlineData("partly", "Fungerer kun delvist.")]
    [InlineData("yes", "Fungerer som den skal.")]
    public void Seller_function_answer_is_preserved(string works, string expected)
    {
        var lines = AdTextBuilder.BuildConditionLines(new Dictionary<string, string?> { ["works"] = works });
        Assert.Contains(expected, lines);
    }
}
