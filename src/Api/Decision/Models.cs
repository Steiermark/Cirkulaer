using System.Text.Json.Serialization;

namespace Api.Decision;

public sealed record Assessment
{
    [JsonPropertyName("object_name")] public string? ObjectName { get; init; }
    [JsonPropertyName("category")] public string? Category { get; init; }
    [JsonPropertyName("category_id")] public string? CategoryId { get; init; }
    [JsonPropertyName("subcategory")] public string? Subcategory { get; init; }
    [JsonPropertyName("brand")] public string? Brand { get; init; }
    [JsonPropertyName("model")] public string? Model { get; init; }
    [JsonPropertyName("materials")] public IReadOnlyList<string> Materials { get; init; } = [];
    [JsonPropertyName("visible_damage")] public IReadOnlyList<string> VisibleDamage { get; init; } = [];
    [JsonPropertyName("condition_estimate")] public string? ConditionEstimate { get; init; }
    [JsonPropertyName("confidence")] public double? Confidence { get; init; }
    [JsonPropertyName("waste_category")] public string? WasteCategory { get; init; }
    [JsonPropertyName("uncertainty_notes")] public IReadOnlyList<string> UncertaintyNotes { get; init; } = [];
}

public sealed record DecisionContext
{
    public required string Category { get; init; }
    public required string CategoryId { get; init; }
    public required IReadOnlyList<string> Materials { get; init; }
    public string? Works { get; init; }
    public string? Reason { get; init; }
    public string? Age { get; init; }
    public required string Condition { get; init; }
    public required string Damage { get; init; }
    public string? Cleaning { get; init; }
    public string? Accessories { get; init; }
    public required string Producer { get; init; }
    public required string Model { get; init; }
    public required string ObjectName { get; init; }
    public required string Subcategory { get; init; }
    public double? IdentificationConfidence { get; init; }
    public required string Safety { get; init; }
    public required bool SafetyRisk { get; init; }
    public string? OriginalProduct { get; init; }
    public string? CleanState { get; init; }
    public string? Unmodified { get; init; }
    public string? Assembled { get; init; }
    public required bool HasBattery { get; init; }
    public required bool IsElectronics { get; init; }
    public required bool IsTextile { get; init; }
    public required bool IsFurniture { get; init; }
}

public sealed record Possibility(bool Realistic, int Score, string Why);

public sealed record ActionOption
{
    [JsonPropertyName("key")] public required string Key { get; init; }
    [JsonPropertyName("label")] public required string Label { get; init; }
    [JsonPropertyName("description")] public required string Description { get; init; }
    [JsonPropertyName("realistic")] public required bool Realistic { get; init; }
}

public sealed record ActionCheck
{
    [JsonPropertyName("label")] public required string Label { get; init; }
    [JsonPropertyName("status")] public required string Status { get; init; }
    [JsonPropertyName("description")] public required string Description { get; init; }
}

public sealed record Impact
{
    [JsonPropertyName("economy")] public required string Economy { get; init; }
    [JsonPropertyName("co2_saving")] public required string Co2Saving { get; init; }
    [JsonPropertyName("note")] public required string Note { get; init; }
}

public sealed record WasteInfo
{
    [JsonPropertyName("general_fraction")] public required string GeneralFraction { get; init; }
    [JsonPropertyName("note")] public required string Note { get; init; }
}

public sealed record Recommendation
{
    [JsonPropertyName("object_name")] public required string ObjectName { get; init; }
    [JsonPropertyName("recommended_action")] public required string RecommendedAction { get; init; }
    [JsonPropertyName("decision_path")] public required IReadOnlyList<string> DecisionPath { get; init; }
    [JsonPropertyName("confidence")] public required string Confidence { get; init; }
    [JsonPropertyName("reasoning")] public required string Reasoning { get; init; }
    [JsonPropertyName("checks")] public required IReadOnlyList<ActionCheck> Checks { get; init; }
    [JsonPropertyName("options")] public required IReadOnlyList<ActionOption> Options { get; init; }
    [JsonPropertyName("impact")] public required Impact Impact { get; init; }
    [JsonPropertyName("producer_program")] public object? ProducerProgram { get; init; }
    [JsonPropertyName("waste")] public required WasteInfo Waste { get; init; }
}
