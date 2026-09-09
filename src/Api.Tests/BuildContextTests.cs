using Api.Decision;

namespace Api.Tests;

public class BuildContextTests
{
    static Assessment Chair() => new()
    {
        ObjectName = "Kontorstol",
        Category = "Møbler og indbo",
        Materials = ["metal", "tekstil"],
        Brand = "IKEA",
        Model = "MARKUS",
    };

    static Dictionary<string, string?> Answers(params (string Key, string? Value)[] pairs)
        => pairs.ToDictionary(pair => pair.Key, pair => pair.Value);

    [Fact]
    public void Detected_producer_falls_back_to_assessment_brand()
    {
        var context = DecisionEngine.BuildContext(Chair(), Answers(("producer", "detected")));

        Assert.Equal("ikea", context.Producer);
    }

    [Fact]
    public void Absent_producer_also_falls_back_to_assessment_brand()
    {
        var context = DecisionEngine.BuildContext(Chair(), Answers());

        Assert.Equal("ikea", context.Producer);
    }

    [Fact]
    public void Manual_producer_and_model_are_used()
    {
        var context = DecisionEngine.BuildContext(Chair(), Answers(
            ("producer", "other"),
            ("producer_name", "Håg"),
            ("model_name", "Capisco")));

        Assert.Equal("haag", context.Producer);
        Assert.Equal("capisco", context.Model);
    }

    [Fact]
    public void Unknown_producer_clears_it()
    {
        var context = DecisionEngine.BuildContext(Chair(), Answers(("producer", "unknown")));

        Assert.Equal("", context.Producer);
    }

    [Fact]
    public void Furniture_is_detected_from_normalized_category()
    {
        var context = DecisionEngine.BuildContext(Chair(), Answers());

        Assert.True(context.IsFurniture);
        Assert.False(context.IsElectronics);
    }

    [Fact]
    public void Battery_material_marks_electronics_and_battery()
    {
        var assessment = Chair() with { Materials = ["plast", "batteri"] };

        var context = DecisionEngine.BuildContext(assessment, Answers());

        Assert.True(context.HasBattery);
        Assert.True(context.IsElectronics);
    }

    [Fact]
    public void Condition_falls_back_to_the_assessment_estimate()
    {
        var assessment = Chair() with { ConditionEstimate = "worn" };

        var context = DecisionEngine.BuildContext(assessment, Answers());

        Assert.Equal("worn", context.Condition);
    }

    [Fact]
    public void Condition_is_unknown_when_neither_source_has_it()
    {
        var context = DecisionEngine.BuildContext(Chair(), Answers());

        Assert.Equal("unknown", context.Condition);
    }

    [Theory]
    [InlineData("knækket ben", "major")]
    [InlineData("ridser", "minor")]
    public void Damage_is_inferred_from_visible_damage_when_unanswered(string visible, string expected)
    {
        var assessment = Chair() with { VisibleDamage = [visible] };

        var context = DecisionEngine.BuildContext(assessment, Answers());

        Assert.Equal(expected, context.Damage);
    }

    [Fact]
    public void Answered_damage_wins_over_inference()
    {
        var assessment = Chair() with { VisibleDamage = ["knækket ben"] };

        var context = DecisionEngine.BuildContext(assessment, Answers(("damage", "no")));

        Assert.Equal("no", context.Damage);
    }

    [Fact]
    public void Safety_risk_is_derived_from_the_safety_answer()
    {
        var context = DecisionEngine.BuildContext(Chair(), Answers(("safety", "risk")));

        Assert.True(context.SafetyRisk);
        Assert.Equal("risk", context.Safety);
    }

    [Fact]
    public void Safety_defaults_to_unknown()
    {
        var context = DecisionEngine.BuildContext(Chair(), Answers());

        Assert.Equal("unknown", context.Safety);
        Assert.False(context.SafetyRisk);
    }
}
