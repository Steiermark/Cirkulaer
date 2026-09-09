using System.Text.Json;
using System.Text.Json.Nodes;
using Api.Decision;
using Api.SaleAssist;

namespace Api.Tests;

public class SaleAssistParityTests
{
    // The live search is stubbed, so these keys carry stub values and are excluded.
    // Everything else — including price and price_note — is deterministic.
    static readonly string[] SearchDerived =
        ["search_note", "search_url", "signals", "comparables", "price_confidence"];

    static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public sealed record Fixture(List<Assessment> Assessments, List<FixtureCase> Cases);

    public sealed record FixtureCase(
        string Name, int AssessmentIndex, Dictionary<string, string?> Answers, JsonElement Expected);

    static Fixture Load() =>
        JsonSerializer.Deserialize<Fixture>(
            File.ReadAllText("fixtures/sale-cases.json"),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })!;

    static readonly Lazy<Dictionary<string, (Assessment Assessment, FixtureCase Case)>> ByName =
        new(() =>
        {
            var fixture = Load();
            return fixture.Cases.ToDictionary(
                item => item.Name,
                item => (fixture.Assessments[item.AssessmentIndex], item));
        });

    public static TheoryData<string> CaseNames()
    {
        var data = new TheoryData<string>();
        foreach (var item in Load().Cases)
            data.Add(item.Name);
        return data;
    }

    sealed class EmptyPriceSearch : IPriceSearch
    {
        public Task<PriceSignals> SearchAsync(string query, bool includeReshopper, CancellationToken ct) =>
            Task.FromResult(new PriceSignals(
                Url: "https://www.google.com/search?q=stub",
                Prices: [], Signals: [], Comparables: [], Confidence: "lav", Note: "stub"));
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public async Task Csharp_output_matches_python(string name)
    {
        var (assessment, testCase) = ByName.Value[name];
        var recommendation = DecisionEngine.BuildRecommendation(assessment, testCase.Answers);

        var actual = await new SaleAssistBuilder(new EmptyPriceSearch())
            .BuildAsync(assessment, testCase.Answers, recommendation, CancellationToken.None);

        var expectedNode = JsonNode.Parse(testCase.Expected.GetRawText())!.AsObject();
        var actualNode = JsonNode.Parse(JsonSerializer.Serialize(actual, Options))!.AsObject();

        foreach (var key in SearchDerived)
        {
            expectedNode.Remove(key);
            actualNode.Remove(key);
        }

        Assert.Equal(expectedNode.ToJsonString(Options), actualNode.ToJsonString(Options));
    }
}
