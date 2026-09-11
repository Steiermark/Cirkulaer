using System.Text.Json.Serialization;

namespace Api.SaleAssist;

public sealed record Comparable
{
    [JsonPropertyName("title")] public required string Title { get; init; }
    [JsonPropertyName("price")] public required int Price { get; init; }
    [JsonPropertyName("relevance")] public required double Relevance { get; init; }
    [JsonPropertyName("url")] public required string Url { get; init; }
    [JsonIgnore] public string? Image { get; init; }
}

public sealed record PriceSignals(
    string Url,
    IReadOnlyList<int> Prices,
    IReadOnlyList<string> Signals,
    IReadOnlyList<Comparable> Comparables,
    string Confidence,
    string Note)
{
    public static PriceSignals FromComparables(
        string query, string manualSearchUrl, bool includeReshopper, IReadOnlyList<Comparable> comparables)
    {
        var extraPlatforms = includeReshopper ? "Facebook Marketplace og Reshopper" : "Facebook Marketplace";
        var prices = comparables.Select(item => item.Price).ToList();
        var confidence = comparables.Count >= 5 ? "høj" : comparables.Count >= 3 ? "middel" : "lav";

        var note = prices.Count > 0
            ? $"Prisforslaget er baseret på en web-søgning efter: {query}. Brug også {extraPlatforms} til at sammenligne relevante annoncer. "
              + $"Der blev fundet {comparables.Count} prisfund med relevant titeltekst. "
            : $"Der blev ikke fundet tydelige danske prisangivelser i web-søgningen. Brug web-linket og {extraPlatforms} til manuel priskontrol. ";

        return new PriceSignals(
            manualSearchUrl,
            prices,
            comparables.Take(5).Select(item => item.Title).ToList(),
            comparables,
            confidence,
            note);
    }
}

public interface IPriceSearch
{
    Task<PriceSignals> SearchAsync(string query, bool includeReshopper, CancellationToken ct);
}

public interface ILookalikeFilter
{
    Task<IReadOnlyList<Comparable>> KeepLookalikesAsync(string photoDataUrl, IReadOnlyList<Comparable> comparables, CancellationToken ct);
}
