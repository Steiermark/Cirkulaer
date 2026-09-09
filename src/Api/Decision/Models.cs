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
