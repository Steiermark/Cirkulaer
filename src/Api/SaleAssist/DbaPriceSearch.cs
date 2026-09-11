using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Api.SaleAssist;

// Reads dba.dk's own search page. Its schema.org ItemList is server-rendered with title,
// price and item url, so no model sits between the ad and the comparable. OpenAI
// web_search was tried first and could not do this: its index holds dba search pages
// but no item pages, and open_page serves only from its crawl cache, so a niche item
// returned zero rows after ~20s and $0.13. Measured 2026-09-11.
public sealed partial class DbaPriceSearch(HttpClient http, ILogger<DbaPriceSearch> logger) : IPriceSearch
{
    static readonly string[] StopWords =
        ["brugt", "pris", "danmark", "den", "det", "med", "og", "til", "moebler", "moebel", "indbo", "kategori"];

    public async Task<PriceSignals> SearchAsync(string query, bool includeReshopper, CancellationToken ct)
    {
        var manualSearchUrl = $"https://www.google.com/search?q={WebUtility.UrlEncode(query).Replace("+", "%20")}";

        List<Comparable> comparables;
        try
        {
            using var response = await http.GetAsync(SearchUrl(query), ct);
            response.EnsureSuccessStatusCode();

            comparables = ParseComparables(await response.Content.ReadAsStringAsync(ct), query);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Price search failed for {Query}; falling back to the heuristic estimate", query);

            return new PriceSignals(
                Url: manualSearchUrl,
                Prices: [],
                Signals: [],
                Comparables: [],
                Confidence: "lav",
                Note: "Net-søgningen kunne ikke gennemføres fra prototypen. Linket åbner en manuel søgning efter lignende genstande.");
        }

        var signals = PriceSignals.FromComparables(query, manualSearchUrl, includeReshopper, comparables);
        logger.LogInformation("Price search for {Query} returned {Count} comparables at {Confidence}", query, comparables.Count, signals.Confidence);
        return signals;
    }

    public static string SearchUrl(string query) =>
        $"https://www.dba.dk/recommerce/forsale/search?q={WebUtility.UrlEncode(query.Trim())}";

    public static List<Comparable> ParseComparables(string html, string query)
    {
        var queryTokens = ComparableQueryTokens(query);
        var comparables = new List<Comparable>();
        var seen = new HashSet<string>();

        foreach (Match block in LdJsonScript().Matches(html))
        {
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(block.Groups[1].Value);
            }
            catch (JsonException)
            {
                continue;
            }

            using var _ = document;
            foreach (var product in Products(document.RootElement))
            {
                var title = product.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "";
                var url = product.TryGetProperty("url", out var link) ? link.GetString() ?? "" : "";
                var image = product.TryGetProperty("image", out var thumbnail) && thumbnail.ValueKind == JsonValueKind.String
                    ? thumbnail.GetString()
                    : null;
                var relevance = Relevance(title, queryTokens);

                if (!product.TryGetProperty("offers", out var offers)
                    || !offers.TryGetProperty("price", out var priceValue)
                    || !int.TryParse(priceValue.GetString(), out var price))
                    continue;

                if (relevance < 0.35 || price is < 25 or > 100000 || url.Length == 0 || !seen.Add(url))
                    continue;

                comparables.Add(new Comparable
                {
                    Title = title,
                    Price = price,
                    Relevance = Math.Round(relevance, 2),
                    Url = url,
                    Image = image,
                });
            }
        }

        return comparables;
    }

    // The list sits under mainEntity on the CollectionPage block; a bare ItemList is
    // accepted too so a layout change on dba's side degrades to fewer rows, not none.
    static IEnumerable<JsonElement> Products(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            yield break;

        var list = root.TryGetProperty("mainEntity", out var mainEntity) ? mainEntity : root;

        if (list.ValueKind != JsonValueKind.Object
            || !list.TryGetProperty("itemListElement", out var items)
            || items.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var entry in items.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.Object
                && entry.TryGetProperty("item", out var item)
                && item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty("@type", out var type)
                && type.GetString() == "Product")
                yield return item;
        }
    }

    static double Relevance(string title, List<string> queryTokens)
    {
        var titleTokens = WordToken().Matches(title.ToLowerInvariant())
            .Select(token => token.Value)
            .ToHashSet();

        return queryTokens.Count(token => titleTokens.Contains(token)) / (double)Math.Max(1, queryTokens.Count);
    }

    public static List<string> ComparableQueryTokens(string query)
    {
        var normalized = SaleQueryBuilder.NormalizeSearchTerms(query ?? "").ToLowerInvariant();

        return WordToken().Matches(normalized)
            .Select(match => match.Value)
            .Where(token => (token.Length >= 3 || token.All(char.IsDigit)) && !StopWords.Contains(token))
            .ToList();
    }

    [GeneratedRegex(@"[a-z0-9æøå]+")]
    private static partial Regex WordToken();

    [GeneratedRegex(@"<script[^>]*application/ld\+json[^>]*>(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex LdJsonScript();
}
