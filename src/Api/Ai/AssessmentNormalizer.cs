using System.Text.Json;
using Api.Decision;
using Api.Producers;

namespace Api.Ai;

public static class AssessmentNormalizer
{
    const string DefaultUncertaintyNote =
        "AI-vurderingen indeholder usikkerhed og bør bekræftes af brugeren.";

    public static Assessment Normalize(JsonElement data)
    {
        var confidence = data.TryGetProperty("confidence", out var raw)
            && raw.ValueKind is JsonValueKind.Number
            && raw.TryGetDouble(out var parsed)
                ? parsed
                : 0.0;

        var category = Text(data, "category");
        var notes = StringList(data, "uncertainty_notes");

        var assessment = new Assessment
        {
            ObjectName = Text(data, "object_name") is { Length: > 0 } name ? name : "Ukendt genstand",
            Category = category is { Length: > 0 } ? category : "Ukendt kategori",
            CategoryId = Text(data, "category_id") is { Length: > 0 } id
                ? id
                : TextHelpers.CanonicalCategoryId(category),
            Subcategory = Text(data, "subcategory"),
            Brand = Text(data, "brand"),
            Model = Text(data, "model"),
            Materials = StringList(data, "materials"),
            VisibleDamage = StringList(data, "visible_damage"),
            ConditionEstimate = Text(data, "condition_estimate") is { Length: > 0 } condition
                ? condition
                : "unknown",
            Confidence = Math.Max(0.0, Math.Min(1.0, confidence)),
            WasteCategory = Text(data, "waste_category") is { Length: > 0 } waste
                ? waste
                : "Ukendt fraktion",
            UncertaintyNotes = notes.Count > 0 ? notes : [DefaultUncertaintyNote],
        };

        return assessment with { ProducerProgramCandidates = ProducerPrograms.FindCandidates(assessment) };
    }

    static string? Text(JsonElement data, string name) =>
        data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    static List<string> StringList(JsonElement data, string name)
    {
        if (!data.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            return [];

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!)
            .ToList();
    }
}
