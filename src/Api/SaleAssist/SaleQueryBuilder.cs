using System.Text.RegularExpressions;
using Api.Decision;

namespace Api.SaleAssist;

public static class SaleQueryBuilder
{
    static readonly (string Old, string New)[] Replacements =
    [
        ("mardone", "Madone"), ("Mardone", "Madone"),
        ("etep", "eTap"), ("Etep", "eTap"), ("ETEP", "eTap"),
        ("trek", "Trek"), ("slr", "SLR"),
    ];

    static readonly string[] ExcludedProducers = ["unknown", "other", "detected", "ved ikke", "anden"];

    static readonly string[] ReshopperRelevant =
    [
        "barn", "børn", "boern", "baby", "legetøj", "legetoej", "barnevogn",
        "klapvogn", "autostol", "børnetøj", "boernetoej", "ventetøj", "ventetoej",
        "tøj", "toej", "tekstil", "møbel", "moebel", "møbler", "moebler", "bolig",
    ];

    static readonly string[] ReshopperExcluded =
        ["cykel", "elektronik", "værktøj", "vaerktoej", "batteri", "maling", "farligt"];

    public static string BuildSaleQuery(Assessment assessment, IReadOnlyDictionary<string, string?> answers)
    {
        var parts = Compact(
        [
            ProducerSearchName(answers),
            Get(answers, "model_name"),
            assessment.Brand,
            assessment.Model,
            assessment.ObjectName,
            assessment.Subcategory,
        ]);

        return $"{string.Join(" ", parts)} brugt pris Danmark".Trim();
    }

    // dba's search narrows hard on every descriptive word and loosens on the "brugt pris
    // Danmark" suffix (measured 2026-09-11: "Roland FP-30X" 6 rows, with suffix 50+ of
    // any Roland, with "Digitalpiano med stativ" 2). A known model is searched bare. The
    // full query stays on the manual links, where breadth does no harm.
    public static string BuildPriceQuery(Assessment assessment, IReadOnlyDictionary<string, string?> answers)
    {
        var model = Get(answers, "model_name") ?? assessment.Model;
        if (string.IsNullOrWhiteSpace(model))
            return BuildSaleQuery(assessment, answers);

        return string.Join(" ", Compact([ProducerSearchName(answers), assessment.Brand, model]));
    }

    public static string BuildSaleObjectName(Assessment assessment, IReadOnlyDictionary<string, string?> answers)
    {
        var parts = Compact(
        [
            ProducerSearchName(answers),
            Get(answers, "model_name"),
            assessment.Brand,
            assessment.Model,
            assessment.ObjectName,
        ]);

        var joined = string.Join(" ", parts);
        return string.IsNullOrEmpty(joined) ? "Genstand" : joined;
    }

    public static string BuildSaleDetails(Assessment assessment, IReadOnlyDictionary<string, string?> answers)
    {
        var details = new List<string>();

        if (!string.IsNullOrEmpty(assessment.Category))
            details.Add(assessment.Category);

        switch (Get(answers, "works"))
        {
            case "yes": details.Add("virker"); break;
            case "partly": details.Add("virker delvist"); break;
            case "no": details.Add("virker ikke"); break;
        }

        switch (Get(answers, "damage"))
        {
            case "no": details.Add("ingen kendte skader"); break;
            case "minor": details.Add("mindre skader/slitage"); break;
            case "major": details.Add("store skader"); break;
        }

        return string.Join(" · ", details);
    }

    public static string NormalizeSearchTerms(string value)
    {
        foreach (var (old, replacement) in Replacements)
            value = value.Replace(old, replacement);
        return value;
    }

    public static string ProducerSearchName(IReadOnlyDictionary<string, string?> answers)
    {
        var producerName = (Get(answers, "producer_name") ?? "").Trim();
        if (producerName.Length > 0)
            return producerName;

        var producer = (Get(answers, "producer") ?? "").Trim();
        if (producer == "ikea")
            return "IKEA";
        if (producer.Length > 0 && !ExcludedProducers.Contains(producer))
            return producer;
        return "";
    }

    public static string BuildMarketplaceSearchUrl(string query) =>
        $"https://www.facebook.com/marketplace/search/?query={Uri.EscapeDataString((query ?? "").Trim())}";

    public static string BuildReshopperUrl() => "https://reshopper.com/da";

    // Matches raw lowercased text, listing both Danish spellings, rather than Normalize output.
    public static bool IsReshopperRelevant(Assessment assessment)
    {
        var text = $"{(assessment.Category ?? "").ToLowerInvariant()} "
                 + $"{(assessment.ObjectName ?? "").ToLowerInvariant()} "
                 + $"{(assessment.Subcategory ?? "").ToLowerInvariant()}";

        return ReshopperRelevant.Any(text.Contains) && !ReshopperExcluded.Any(text.Contains);
    }

    public static string BuildReshopperNote(Assessment assessment) =>
        IsReshopperRelevant(assessment)
            ? "Reshopper vises, fordi genstanden ser ud til at passe til børn, mor eller bolig. Søg manuelt i appen med producent, model og genstandens navn."
            : "Reshopper er skjult, fordi platformen primært er relevant for børn, mor og bolig.";

    static List<string> Compact(IReadOnlyList<string?> parts)
    {
        var seen = new HashSet<string>();
        var clean = new List<string>();

        foreach (var part in parts)
        {
            var value = NormalizeSearchTerms((part ?? "").Trim());
            var key = value.ToLowerInvariant();
            if (value.Length > 0 && seen.Add(key))
                clean.Add(value);
        }

        return CompactRedundantParts(clean);
    }

    public static List<string> CompactRedundantParts(IReadOnlyList<string> parts)
    {
        var lowered = parts.Select(part => part.ToLowerInvariant()).ToList();
        var compact = new List<string>();

        for (var index = 0; index < parts.Count; index++)
        {
            var key = lowered[index];
            var containedByLonger = lowered
                .Where((other, otherIndex) => otherIndex != index && other.Length > key.Length)
                .Any(other => Regex.IsMatch(other, $@"(?<![a-z0-9]){Regex.Escape(key)}(?![a-z0-9])"));

            if (!containedByLonger)
                compact.Add(parts[index]);
        }

        return compact;
    }

    static string? Get(IReadOnlyDictionary<string, string?> answers, string key) =>
        answers.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value) ? value : null;
}
