namespace Api.Decision;

public static partial class DecisionEngine
{
    static readonly string[] MajorDamageTerms =
        ["knækket", "knust", "revnet", "brændt", "laekker", "lækker", "deformeret", "gennemtæret"];

    public static DecisionContext BuildContext(
        Assessment assessment, IReadOnlyDictionary<string, string?> answers)
    {
        var category = TextHelpers.Normalize(assessment.Category);
        var categoryId = (assessment.CategoryId ?? "").ToLowerInvariant();
        var materials = assessment.Materials.Select(item => TextHelpers.Normalize(item)).ToList();
        var joinedMaterials = string.Join(" ", materials);

        var condition = Get(answers, "condition") ?? assessment.ConditionEstimate;
        var damage = Get(answers, "damage") ?? InferDamageFromAssessment(assessment);
        var safety = Get(answers, "safety") ?? "unknown";

        var selectedProducer = Get(answers, "producer");
        string producer;
        if (selectedProducer is null or "" or "detected")
            producer = assessment.Brand ?? "";
        else if (selectedProducer == "other" && !string.IsNullOrEmpty(Get(answers, "producer_name")))
            producer = Get(answers, "producer_name")!;
        else if (selectedProducer is "unknown" or "ved ikke")
            producer = "";
        else
            producer = selectedProducer;

        var model = Get(answers, "model_name") ?? assessment.Model ?? "";

        return new DecisionContext
        {
            Category = category,
            CategoryId = categoryId,
            Materials = materials,
            Works = Get(answers, "works"),
            Reason = Get(answers, "reason"),
            Age = Get(answers, "age"),
            Condition = string.IsNullOrEmpty(condition) ? "unknown" : condition,
            Damage = damage,
            Cleaning = Get(answers, "cleaning"),
            Accessories = Get(answers, "accessories"),
            Producer = TextHelpers.Normalize(producer),
            Model = TextHelpers.Normalize(model),
            ObjectName = TextHelpers.Normalize(assessment.ObjectName ?? ""),
            Subcategory = TextHelpers.Normalize(assessment.Subcategory ?? ""),
            IdentificationConfidence = assessment.Confidence,
            Safety = safety,
            SafetyRisk = safety == "risk",
            OriginalProduct = Get(answers, "original_product"),
            CleanState = Get(answers, "clean_state"),
            Unmodified = Get(answers, "unmodified"),
            Assembled = Get(answers, "assembled"),
            HasBattery = Get(answers, "battery") == "battery" || joinedMaterials.Contains("batteri"),
            IsElectronics = categoryId == "electronics"
                || category.Contains("elektronik")
                || joinedMaterials.Contains("batteri"),
            IsTextile = categoryId == "textile" || category.Contains("tekstil"),
            IsFurniture = categoryId == "furniture"
                || category.Contains("moebler")
                || category.Contains("mobler")
                || category.Contains("moebel"),
        };
    }

    internal static string InferDamageFromAssessment(Assessment assessment)
    {
        var visible = string.Join(" ", assessment.VisibleDamage.Select(item => item.ToLowerInvariant()));
        var condition = (assessment.ConditionEstimate ?? "").ToLowerInvariant();

        if (MajorDamageTerms.Any(visible.Contains))
            return "major";
        if (!string.IsNullOrEmpty(visible) || condition is "worn" or "damaged")
            return "minor";
        if (condition is "new" or "good")
            return "no";
        return "unknown";
    }

    static string? Get(IReadOnlyDictionary<string, string?> answers, string key) =>
        answers.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value) ? value : null;
}
