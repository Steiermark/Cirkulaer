using Api.Decision;
using Api.Producers;

namespace Api.Tests;

public class ProducerProgramsTests
{
    static Assessment Billy() => new()
    {
        ObjectName = "BILLY-reol",
        Category = "Møbler og indbo",
        Subcategory = "Bogreol",
        Brand = "IKEA",
        Materials = ["spaanplade"],
    };

    static ProducerProgramResult Evaluate(params (string Key, string? Value)[] pairs)
    {
        var answers = pairs.ToDictionary(pair => pair.Key, pair => pair.Value);
        return ProducerPrograms.Evaluate(DecisionEngine.BuildContext(Billy(), answers));
    }

    [Fact]
    public void Ikea_resale_is_likely_when_every_requirement_is_confirmed()
    {
        var result = Evaluate(
            ("producer", "detected"), ("works", "yes"), ("damage", "no"),
            ("accessories", "complete"), ("original_product", "yes"),
            ("clean_state", "yes"), ("unmodified", "yes"), ("assembled", "yes"));

        Assert.Equal("likely", result.Status);
        Assert.Equal("Producentordning fundet", result.Title);
        Assert.Equal("IKEA", result.Programs[0].Producer);
        Assert.Equal(3, result.Programs[0].Rank);
    }

    [Fact]
    public void Program_stays_possible_until_requirements_are_confirmed()
    {
        var result = Evaluate(
            ("producer", "detected"), ("works", "yes"),
            ("damage", "no"), ("accessories", "complete"));

        Assert.Equal("possible", result.Status);
    }

    [Fact]
    public void A_denied_requirement_makes_it_unlikely()
    {
        var result = Evaluate(
            ("producer", "detected"), ("works", "yes"), ("damage", "no"),
            ("accessories", "complete"), ("original_product", "no"));

        Assert.Equal("unlikely", result.Status);
    }

    [Fact]
    public void No_producer_yields_the_none_placeholder()
    {
        var result = Evaluate(("producer", "unknown"));

        Assert.Equal("none", result.Status);
        Assert.Equal("Producentordninger", result.Title);
        Assert.Empty(result.Programs);
    }

    [Fact]
    public void Unanswered_requirements_stay_null_rather_than_false()
    {
        var result = Evaluate(
            ("producer", "detected"), ("works", "yes"),
            ("damage", "no"), ("accessories", "complete"));

        Assert.Equal(
            [null, true, null, true, null, null, true],
            result.Programs[0].Checks.Select(check => check.Ok));
    }

    [Fact]
    public void Candidates_are_found_from_the_assessment_alone()
        => Assert.Equal(["ikea_gensalg"], ProducerPrograms.FindCandidates(Billy()));
}
