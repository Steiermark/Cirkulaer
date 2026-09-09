using System.Text.Json;
using System.Text.Json.Nodes;
using Api.Decision;

namespace Api.Tests;

public class DecisionParityTests
{
    // Emptied in Task 6, once ProducerPrograms.Evaluate is wired into BuildRecommendation.
    static readonly string[] NotPortedYet = ["producer_program"];

    static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public sealed record Fixture(
        List<Assessment> Assessments,
        List<FixtureCase> Cases);

    public sealed record FixtureCase(
        string Name,
        int AssessmentIndex,
        Dictionary<string, string?> Answers,
        JsonElement Expected);

    static Fixture Load()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        return JsonSerializer.Deserialize<Fixture>(
            File.ReadAllText("fixtures/decision-cases.json"), options)!;
    }

    public static TheoryData<string> CaseNames()
    {
        var data = new TheoryData<string>();
        foreach (var item in Load().Cases)
            data.Add(item.Name);
        return data;
    }

    static readonly Lazy<Dictionary<string, (Assessment Assessment, FixtureCase Case)>> ByName =
        new(() =>
        {
            var fixture = Load();
            return fixture.Cases.ToDictionary(
                item => item.Name,
                item => (fixture.Assessments[item.AssessmentIndex], item));
        });

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Csharp_output_matches_python(string name)
    {
        var (assessment, testCase) = ByName.Value[name];

        var actual = DecisionEngine.BuildRecommendation(assessment, testCase.Answers);

        var expectedNode = JsonNode.Parse(testCase.Expected.GetRawText())!.AsObject();
        var actualNode = JsonNode.Parse(JsonSerializer.Serialize(actual, Options))!.AsObject();

        foreach (var key in NotPortedYet)
        {
            expectedNode.Remove(key);
            actualNode.Remove(key);
        }

        Assert.Equal(Canonical(expectedNode), Canonical(actualNode));
    }

    static string Canonical(JsonNode node) => node.ToJsonString(Options);
}
