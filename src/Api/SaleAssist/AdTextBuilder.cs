using Api.Decision;

namespace Api.SaleAssist;

public static class AdTextBuilder
{
    public static string BuildAdText(
        string objectName,
        Assessment assessment,
        IReadOnlyDictionary<string, string?> answers,
        PriceEstimate estimate,
        string? introduction = null)
    {
        var lines = new List<string>
        {
            $"{objectName} sælges",
            "",
            introduction ?? $"Jeg sælger {objectName}. Måske er det lige den, du leder efter?",
        };

        var features = BuildFeatureLines(assessment, answers);
        if (features.Count > 0)
        {
            lines.Add("");
            lines.AddRange(features);
        }

        var condition = BuildConditionLines(answers);
        if (Get(answers, "damage") != "no")
            condition.AddRange(assessment.VisibleDamage.Select(damage => $"- {damage}"));
        if (condition.Count > 0)
        {
            lines.AddRange(["", "Stand og tilbehør:"]);
            lines.AddRange(condition);
        }

        // Price provenance belongs in PriceNote, never in the copyable ad.
        const string pricePrefix = "Sæt prisen til ";
        var price = estimate.Label.StartsWith(pricePrefix, StringComparison.Ordinal)
            ? estimate.Label[pricePrefix.Length..]
            : estimate.Label;
        lines.AddRange(["", $"Pris: {price}", "", "Interesseret? Skriv gerne for at høre mere eller aftale en handel."]);
        return string.Join("\n", lines);
    }

    public static List<string> BuildFeatureLines(
        Assessment assessment, IReadOnlyDictionary<string, string?> answers)
    {
        var lines = new List<string>();
        var producer = SaleQueryBuilder.ProducerSearchName(answers) is { Length: > 0 } name
            ? name
            : assessment.Brand;
        var model = Get(answers, "model_name") ?? assessment.Model;
        if (!string.IsNullOrWhiteSpace(producer)) lines.Add($"Mærke: {producer}");
        if (!string.IsNullOrWhiteSpace(model)) lines.Add($"Model: {model}");
        if (assessment.Materials.Count > 0) lines.Add($"Materialer: {string.Join(", ", assessment.Materials)}");
        return lines;
    }

    public static List<string> BuildConditionLines(IReadOnlyDictionary<string, string?> answers)
    {
        var lines = new List<string>();
        switch (Get(answers, "works"))
        {
            case "yes": lines.Add("Fungerer som den skal."); break;
            case "partly": lines.Add("Fungerer kun delvist."); break;
            case "no": lines.Add("Virker ikke og sælges til reparation eller reservedele."); break;
        }
        switch (Get(answers, "damage"))
        {
            case "no": lines.Add("Ingen kendte skader."); break;
            case "minor": lines.Add("Har mindre brugsspor eller slitage."); break;
            case "major": lines.Add("Har større fejl eller skader."); break;
        }
        switch (Get(answers, "accessories"))
        {
            case "complete": lines.Add("Alt tilbehør medfølger."); break;
            case "partial": lines.Add("Noget tilbehør medfølger."); break;
            case "missing": lines.Add("Der mangler tilbehør."); break;
        }
        return lines;
    }

    static string? Get(IReadOnlyDictionary<string, string?> answers, string key) =>
        answers.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
}
