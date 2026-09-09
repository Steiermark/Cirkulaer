using System.Text.Json.Serialization;
using Api.Decision;

namespace Api.SaleAssist;

public sealed record SaleAssistResult
{
    [JsonPropertyName("object_name")] public required string ObjectName { get; init; }
    [JsonPropertyName("details")] public required string Details { get; init; }
    [JsonPropertyName("price")] public required string Price { get; init; }
    [JsonPropertyName("price_note")] public required string PriceNote { get; init; }
    [JsonPropertyName("search_note")] public required string SearchNote { get; init; }
    [JsonPropertyName("search_url")] public required string SearchUrl { get; init; }
    [JsonPropertyName("marketplace_search_url")] public required string MarketplaceSearchUrl { get; init; }
    [JsonPropertyName("reshopper_relevant")] public required bool ReshopperRelevant { get; init; }
    [JsonPropertyName("reshopper_url")] public required string ReshopperUrl { get; init; }
    [JsonPropertyName("reshopper_note")] public required string ReshopperNote { get; init; }
    [JsonPropertyName("ad_text")] public required string AdText { get; init; }
    [JsonPropertyName("marketplace_note")] public required string MarketplaceNote { get; init; }
    [JsonPropertyName("signals")] public required IReadOnlyList<string> Signals { get; init; }
    [JsonPropertyName("comparables")] public required IReadOnlyList<Comparable> Comparables { get; init; }
    [JsonPropertyName("price_confidence")] public required string PriceConfidence { get; init; }
}

public sealed class SaleAssistBuilder(IPriceSearch search)
{
    const string MarketplaceNote =
        "Direkte oprettelse på Facebook Marketplace kræver officiel adgang. "
        + "Facebook Marketplace bruges her som manuel priskontrol via søgelink. "
        + "I denne prototype kan annoncen kopieres og Marketplace åbnes manuelt.";

    public async Task<SaleAssistResult> BuildAsync(
        Assessment assessment,
        IReadOnlyDictionary<string, string?> answers,
        Recommendation recommendation,
        CancellationToken ct)
    {
        var query = SaleQueryBuilder.BuildSaleQuery(assessment, answers);
        var marketplaceUrl = SaleQueryBuilder.BuildMarketplaceSearchUrl(query);
        var reshopperRelevant = SaleQueryBuilder.IsReshopperRelevant(assessment);
        var signals = await search.SearchAsync(query, reshopperRelevant, ct);
        var estimate = PriceEstimator.Estimate(assessment, answers, signals.Prices);
        var objectName = SaleQueryBuilder.BuildSaleObjectName(assessment, answers);

        return new SaleAssistResult
        {
            ObjectName = objectName,
            Details = SaleQueryBuilder.BuildSaleDetails(assessment, answers),
            Price = estimate.Label,
            PriceNote = estimate.Note,
            SearchNote = signals.Note,
            SearchUrl = signals.Url,
            MarketplaceSearchUrl = marketplaceUrl,
            ReshopperRelevant = reshopperRelevant,
            ReshopperUrl = SaleQueryBuilder.BuildReshopperUrl(),
            ReshopperNote = SaleQueryBuilder.BuildReshopperNote(assessment),
            AdText = AdTextBuilder.BuildAdText(objectName, assessment, answers, estimate),
            MarketplaceNote = MarketplaceNote,
            Signals = signals.Signals,
            Comparables = signals.Comparables,
            PriceConfidence = signals.Confidence,
        };
    }
}
