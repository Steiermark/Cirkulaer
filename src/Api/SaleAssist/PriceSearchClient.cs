using System.Net;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Web;

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

public sealed partial class PriceSearchClient(HttpClient http, ILogger<PriceSearchClient> logger) : IPriceSearch
{
    static readonly string[] StopWords =
        ["brugt", "pris", "danmark", "den", "det", "med", "og", "til", "moebler", "moebel", "indbo", "kategori"];

    public async Task<PriceSignals> SearchAsync(string query, bool includeReshopper, CancellationToken ct)
    {
        var encoded = WebUtility.UrlEncode(query).Replace("+", "%20");
        var url = $"https://duckduckgo.com/html/?q={Uri.EscapeDataString(query).Replace("%20", "+")}";
        var extraPlatforms = includeReshopper ? "Facebook Marketplace og Reshopper" : "Facebook Marketplace";

        string page;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 CirkulaerPrototype/1.0");
            using var response = await http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            page = await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception exception)
        {
            // Expected from Azure datacenter IPs. Degrades quality, never availability —
            // see the spec's "Price search: known limitation" section before "fixing" this.
            logger.LogWarning(exception, "Price search failed for {Query}; falling back to the heuristic estimate", query);

            return new PriceSignals(
                Url: $"https://www.google.com/search?q={encoded}",
                Prices: [],
                Signals: [],
                Comparables: [],
                Confidence: "lav",
                Note: "Net-søgningen kunne ikke gennemføres fra prototypen. Linket åbner en manuel søgning efter lignende genstande.");
        }

        var comparables = ExtractComparables(page, query);

        // A 200 that parses to nothing is the datacenter-IP symptom: DuckDuckGo serves a
        // different page rather than blocking, so without this it is indistinguishable
        // from a genuine no-results search.
        if (comparables.Count == 0)
            logger.LogWarning("Price search returned {Bytes} bytes but no comparables for {Query}", page.Length, query);

        var signals = comparables.Take(5).Select(item => item.Title).ToList();
        var prices = comparables.Select(item => item.Price).ToList();
        var confidence = comparables.Count >= 5 ? "høj" : comparables.Count >= 3 ? "middel" : "lav";

        var note = prices.Count > 0
            ? $"Prisforslaget er baseret på en web-søgning efter: {query}. Brug også {extraPlatforms} til at sammenligne relevante annoncer. "
              + $"Der blev fundet {comparables.Count} prisfund med relevant titeltekst. "
            : $"Der blev ikke fundet tydelige danske prisangivelser i web-søgningen. Brug web-linket og {extraPlatforms} til manuel priskontrol. ";

        return new PriceSignals(url, prices, signals, comparables.Take(8).ToList(), confidence, note);
    }

    public static List<Comparable> ExtractComparables(string page, string query)
    {
        var titleMatches = TitleAnchor().Matches(page).Take(20).ToList();
        var allMatches = TitleAnchor().Matches(page);
        var queryTokens = ComparableQueryTokens(query);
        var comparables = new List<Comparable>();

        for (var index = 0; index < titleMatches.Count; index++)
        {
            var match = titleMatches[index];
            var segmentEnd = index + 1 < allMatches.Count
                ? allMatches[index + 1].Index
                : Math.Min(page.Length, match.Index + match.Length + 2500);

            var segment = page[match.Index..segmentEnd];
            var title = CleanHtmlText(match.Groups[2].Value);
            var prices = ExtractPrices(CleanHtmlText(segment));

            if (title.Length == 0 || prices.Count == 0)
                continue;

            var titleTokens = WordToken().Matches(title.ToLowerInvariant())
                .Select(token => token.Value)
                .ToHashSet();
            var matched = queryTokens.Count(token => titleTokens.Contains(token));
            var relevance = matched / (double)Math.Max(1, queryTokens.Count);

            if (relevance < 0.35)
                continue;

            comparables.Add(new Comparable
            {
                Title = title,
                Price = prices[0],
                Relevance = Math.Round(relevance, 2),
                Url = DecodeSearchResultUrl(HttpUtility.HtmlDecode(match.Groups[1].Value)),
            });
        }

        return comparables;
    }

    public static List<string> ComparableQueryTokens(string query)
    {
        var normalized = SaleQueryBuilder.NormalizeSearchTerms(query ?? "").ToLowerInvariant();

        return WordToken().Matches(normalized)
            .Select(match => match.Value)
            .Where(token => (token.Length >= 3 || token.All(char.IsDigit)) && !StopWords.Contains(token))
            .ToList();
    }

    public static string CleanHtmlText(string value) =>
        HttpUtility.HtmlDecode(Whitespace().Replace(Tag().Replace(value, " "), " ")).Trim();

    public static string DecodeSearchResultUrl(string url)
    {
        var separator = url.IndexOf('?');
        if (separator < 0)
            return url;

        var target = HttpUtility.ParseQueryString(url[(separator + 1)..])["uddg"];
        return string.IsNullOrEmpty(target) ? url : target;
    }

    // Deliberate fix, not a port: the original required two leading digits, so the Danish
    // thousands separator broke it and "1.250 kr" read as 250, biasing every estimate down.
    public static List<int> ExtractPrices(string text)
    {
        var prices = new List<int>();

        foreach (Match match in CurrencyAmount().Matches(text))
        {
            var digits = NonDigit().Replace(match.Groups[1].Value, "");
            if (int.TryParse(digits, out var value) && value is >= 25 and <= 100000)
                prices.Add(value);
        }

        foreach (Match match in ThousandsWord().Matches(text))
        {
            if (int.TryParse(match.Groups[1].Value, out var raw))
            {
                var value = raw * 1000;
                if (value is >= 1000 and <= 100000)
                    prices.Add(value);
            }
        }

        return prices.Take(30).Order().ToList();
    }

    [GeneratedRegex("""<a[^>]*class="[^"]*result__a[^"]*"[^>]*href="([^"]+)"[^>]*>(.*?)</a>""",
        RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex TitleAnchor();

    [GeneratedRegex(@"[a-z0-9æøå]+")]
    private static partial Regex WordToken();

    [GeneratedRegex(@"<.*?>", RegexOptions.Singleline)]
    private static partial Regex Tag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"(?<!\d)(\d{1,6}(?:[\.,]\d{3})?)\s*(?:kr\.?|dkk|,-)", RegexOptions.IgnoreCase)]
    private static partial Regex CurrencyAmount();

    [GeneratedRegex(@"(?<!\d)(\d{1,3})\s*(?:tusind|t\.kr\.?|k)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ThousandsWord();

    [GeneratedRegex(@"\D")]
    private static partial Regex NonDigit();
}
