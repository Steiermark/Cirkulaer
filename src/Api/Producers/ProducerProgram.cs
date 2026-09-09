using System.Text.Json.Serialization;

namespace Api.Producers;

public sealed record ProducerProgram
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("producer")] public required string Producer { get; init; }
    [JsonPropertyName("title")] public required string Title { get; init; }
    [JsonPropertyName("scheme_types")] public required IReadOnlyList<string> SchemeTypes { get; init; }
    [JsonPropertyName("url")] public required string Url { get; init; }
    [JsonPropertyName("reward")] public required string Reward { get; init; }
    [JsonPropertyName("source_label")] public required string SourceLabel { get; init; }
    [JsonPropertyName("eligible_hints")] public required IReadOnlyList<string> EligibleHints { get; init; }
    [JsonPropertyName("excluded_hints")] public required IReadOnlyList<string> ExcludedHints { get; init; }
    [JsonPropertyName("checks")] public required IReadOnlyList<string> Checks { get; init; }
}

public sealed record ProgramCheck
{
    [JsonPropertyName("label")] public required string Label { get; init; }
    [JsonPropertyName("ok")] public required bool? Ok { get; init; }
}

public sealed record ProgramComparison
{
    [JsonPropertyName("label")] public required string Label { get; init; }
    [JsonPropertyName("value")] public required string Value { get; init; }
}

public sealed record ProgramEvaluation
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("title")] public required string Title { get; init; }
    [JsonPropertyName("producer")] public required string Producer { get; init; }
    [JsonPropertyName("scheme_types")] public required IReadOnlyList<string> SchemeTypes { get; init; }
    [JsonPropertyName("status")] public required string Status { get; init; }
    [JsonPropertyName("rank")] public required int Rank { get; init; }
    [JsonPropertyName("message")] public required string Message { get; init; }
    [JsonPropertyName("url")] public required string Url { get; init; }
    [JsonPropertyName("reward")] public required string Reward { get; init; }
    [JsonPropertyName("source_label")] public required string SourceLabel { get; init; }
    [JsonPropertyName("checks")] public required IReadOnlyList<ProgramCheck> Checks { get; init; }
    [JsonPropertyName("comparison")] public required IReadOnlyList<ProgramComparison> Comparison { get; init; }
}

public sealed record ProducerProgramResult
{
    [JsonPropertyName("status")] public required string Status { get; init; }
    [JsonPropertyName("title")] public required string Title { get; init; }
    [JsonPropertyName("message")] public required string Message { get; init; }
    [JsonPropertyName("programs")] public required IReadOnlyList<ProgramEvaluation> Programs { get; init; }
}
