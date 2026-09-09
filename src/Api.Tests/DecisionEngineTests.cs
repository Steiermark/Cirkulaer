using Api.Decision;

namespace Api.Tests;

public class DecisionEngineTests
{
    static Dictionary<string, string?> Answers(params (string Key, string? Value)[] pairs)
        => pairs.ToDictionary(pair => pair.Key, pair => pair.Value);

    [Fact]
    public void No_longer_used_working_item_is_sold_first()
    {
        var assessment = new Assessment
        {
            ObjectName = "Kontorstol",
            Category = "Møbler og indbo",
            WasteCategory = "Storskrald eller genbrugsplads",
            Materials = ["metal", "tekstil"],
        };

        var result = DecisionEngine.BuildRecommendation(assessment, Answers(
            ("works", "yes"), ("reason", "no_need"), ("damage", "no"),
            ("age", "mid"), ("accessories", "complete"), ("producer", "other")));

        Assert.Equal("sell", result.RecommendedAction);
        Assert.Equal(
            ["Bruger den ikke længere", "Vurder salg", "Tjek producentordninger", "Sælg"],
            result.DecisionPath);
    }

    [Fact]
    public void Defective_electronics_prioritizes_repair()
    {
        var assessment = new Assessment
        {
            ObjectName = "Akkuboremaskine",
            Category = "Elektronik og værktøj",
            WasteCategory = "Småt elektronik",
            Materials = ["plast", "metal", "batteri"],
        };

        var result = DecisionEngine.BuildRecommendation(assessment, Answers(
            ("works", "no"), ("reason", "defect"), ("damage", "minor"),
            ("age", "mid"), ("accessories", "complete")));

        Assert.Equal("repair", result.RecommendedAction);
        Assert.Equal(["Defekt", "Kontrollér reparation", "Reparér"], result.DecisionPath);
    }

    [Fact]
    public void Safety_risk_overrides_every_circular_action()
    {
        var assessment = new Assessment { ObjectName = "Elvarmer", Category = "Elektronik" };

        var result = DecisionEngine.BuildRecommendation(assessment, Answers(
            ("works", "yes"), ("reason", "no_need"), ("safety", "risk")));

        Assert.Equal("waste", result.RecommendedAction);
        Assert.Equal(
            ["Mulig sikkerhedsrisiko", "Undgå videre brug", "Sikker aflevering"],
            result.DecisionPath);
        Assert.All(result.Options.Where(option => option.Key != "waste"),
            option => Assert.False(option.Realistic));
    }

    [Fact]
    public void Furniture_can_prioritize_cleaning_before_sale()
    {
        var assessment = new Assessment
        {
            ObjectName = "Sofabord",
            Category = "Møbler og indbo",
            Materials = ["trae"],
        };

        var result = DecisionEngine.BuildRecommendation(assessment, Answers(
            ("works", "yes"), ("reason", "no_need"), ("damage", "no"),
            ("cleaning", "light"), ("age", "mid"), ("accessories", "complete")));

        Assert.Equal("clean", result.RecommendedAction);
    }

    [Fact]
    public void User_thinking_it_is_waste_does_not_force_waste()
    {
        var assessment = new Assessment
        {
            ObjectName = "Kontorstol",
            Category = "Møbler og indbo",
            Materials = ["metal"],
        };

        var result = DecisionEngine.BuildRecommendation(assessment, Answers(
            ("works", "yes"), ("reason", "waste_assumption"), ("damage", "no"),
            ("age", "mid"), ("accessories", "complete")));

        Assert.NotEqual("waste", result.RecommendedAction);
    }

    [Fact]
    public void Defective_item_without_realistic_repair_becomes_waste()
    {
        var assessment = new Assessment
        {
            ObjectName = "Lampe",
            Category = "Belysning",
            Materials = ["glas"],
        };

        var result = DecisionEngine.BuildRecommendation(assessment, Answers(
            ("works", "no"), ("reason", "defect"), ("damage", "major"),
            ("age", "old"), ("accessories", "partial")));

        Assert.Equal("waste", result.RecommendedAction);
        Assert.Equal(["Defekt", "Reparation ikke realistisk", "Affald"], result.DecisionPath);
    }

    [Fact]
    public void Waste_ordering_drops_repair_and_clean_entirely()
    {
        var assessment = new Assessment { ObjectName = "Elvarmer", Category = "Elektronik" };

        var result = DecisionEngine.BuildRecommendation(assessment, Answers(("safety", "risk")));

        Assert.Equal(["waste", "donate", "sell"], result.Options.Select(option => option.Key));
    }

    [Fact]
    public void Object_name_falls_back_to_the_danish_placeholder()
    {
        var result = DecisionEngine.BuildRecommendation(new Assessment(), Answers());

        Assert.Equal("Ukendt genstand", result.ObjectName);
    }

    [Fact]
    public void Waste_fraction_falls_back_when_the_assessment_has_none()
    {
        var result = DecisionEngine.BuildRecommendation(new Assessment(), Answers());

        Assert.Equal("Afhænger af materiale", result.Waste.GeneralFraction);
    }
}
