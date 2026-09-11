using System.Text.Json.Serialization;

namespace Api.SaleAssist;

public sealed record Comparable
{
    [JsonPropertyName("title")] public required string Title { get; init; }
    [JsonPropertyName("price")] public required int Price { get; init; }
    [JsonPropertyName("relevance")] public required double Relevance { get; init; }
    [JsonPropertyName("url")] public required string Url { get; init; }
}

public sealed record PriceSignals(
    string Url,
    IReadOnlyList<int> Prices,
    IReadOnlyList<string> Signals,
    IReadOnlyList<Comparable> Comparables,
    string Confidence,
    string Note);

public interface IPriceSearch
{
    Task<PriceSignals> SearchAsync(string query, bool includeReshopper, CancellationToken ct);
}
