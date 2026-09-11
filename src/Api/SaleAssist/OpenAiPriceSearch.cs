using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;

namespace Api.SaleAssist;

public sealed partial class OpenAiPriceSearch(
    HttpClient http,
    IConfiguration config,
    IMemoryCache cache,
    ILogger<OpenAiPriceSearch> logger) : IPriceSearch
{
    static readonly string[] StopWords =
        ["brugt", "pris", "danmark", "den", "det", "med", "og", "til", "moebler", "moebel", "indbo", "kategori"];

    static readonly string[] DefaultDomains = ["dba.dk", "guloggratis.dk"];

    // Not a hit-rate play: sale-assist can be re-run while the user steps back and forth
    // through the flow, and a live search costs real money each time.
    static readonly TimeSpan DedupeWindow = TimeSpan.FromMinutes(10);

    public async Task<PriceSignals> SearchAsync(string query, bool includeReshopper, CancellationToken ct)
    {
        var key = $"{query.Trim().ToLowerInvariant()}|{includeReshopper}";
        if (cache.TryGetValue<PriceSignals>(key, out var cached) && cached is not null)
            return cached;

        var manualSearchUrl = $"https://www.google.com/search?q={WebUtility.UrlEncode(query).Replace("+", "%20")}";
        var extraPlatforms = includeReshopper ? "Facebook Marketplace og Reshopper" : "Facebook Marketplace";

        List<Comparable> comparables;
        try
        {
            using var response = await http.SendAsync(BuildRequest(query), ct);
            response.EnsureSuccessStatusCode();

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            comparables = ParseComparables(document.RootElement, query);
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

        comparables = await KeepLiveLinksAsync(comparables, ct);

        var prices = comparables.Select(item => item.Price).ToList();
        var confidence = comparables.Count >= 5 ? "høj" : comparables.Count >= 3 ? "middel" : "lav";

        var note = prices.Count > 0
            ? $"Prisforslaget er baseret på en web-søgning efter: {query}. Brug også {extraPlatforms} til at sammenligne relevante annoncer. "
              + $"Der blev fundet {comparables.Count} prisfund med relevant titeltekst. "
            : $"Der blev ikke fundet tydelige danske prisangivelser i web-søgningen. Brug web-linket og {extraPlatforms} til manuel priskontrol. ";

        var signals = new PriceSignals(
            manualSearchUrl,
            prices,
            comparables.Take(5).Select(item => item.Title).ToList(),
            comparables.Take(8).ToList(),
            confidence,
            note);

        if (comparables.Count > 0)
            cache.Set(key, signals, DedupeWindow);

        return signals;
    }

    // The prompt forbids inventing urls; this is what enforces it. Only an explicit "gone"
    // drops a row — a block, a timeout or a 5xx keeps it, because GulogGratis sits behind
    // Cloudflare and answers datacenter IPs with a challenge. Treating that as proof of a
    // dead link would silently discard the source we most rely on.
    //
    // The cover is therefore uneven, by choice: dba.dk answers HEAD honestly (200 for a
    // real ad, 404 for an invented id, measured 2026-09-11), so fabrication there is
    // caught. guloggratis.dk returns 403 to us either way, so its rows pass unverified.
    // Spoofing a browser agent would even that up and is not worth doing to get around
    // someone's deliberate block.
    async Task<List<Comparable>> KeepLiveLinksAsync(List<Comparable> comparables, CancellationToken ct)
    {
        if (comparables.Count == 0)
            return comparables;

        var checks = await Task.WhenAll(comparables.Select(item => IsGoneAsync(item.Url, ct)));
        var live = comparables.Where((_, index) => !checks[index]).ToList();

        if (live.Count != comparables.Count)
            logger.LogWarning("Dropped {Count} price comparables whose link was gone", comparables.Count - live.Count);

        return live;
    }

    async Task<bool> IsGoneAsync(string url, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));

            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await http.SendAsync(request, timeout.Token);

            return response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogDebug(exception, "Could not verify comparable link {Url}; keeping it", url);
            return false;
        }
    }

    HttpRequestMessage BuildRequest(string query)
    {
        var body = new
        {
            model = config["Ai:PriceSearch:Model"] ?? "gpt-5",
            max_tool_calls = int.TryParse(config["Ai:PriceSearch:MaxToolCalls"], out var max) ? max : 10,
            reasoning = new { effort = "low" },
            tools = new[]
            {
                new
                {
                    type = "web_search",
                    filters = new { allowed_domains = AllowedDomains() },
                },
            },
            input = new[]
            {
                new
                {
                    role = "user",
                    content = new[] { new { type = "input_text", text = PriceSearchPrompt.For(query) } },
                },
            },
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "price_comparables",
                    strict = true,
                    schema = ComparableSchema.Element,
                },
            },
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Authorization",
            $"Bearer {config["Ai:OpenAiApiKey"] ?? throw new InvalidOperationException("Ai:OpenAiApiKey not configured")}");

        return request;
    }

    string[] AllowedDomains()
    {
        var configured = config.GetSection("Ai:PriceSearch:AllowedDomains").Get<string[]>();
        return configured is { Length: > 0 } ? configured : DefaultDomains;
    }

    public static List<Comparable> ParseComparables(JsonElement result, string query)
    {
        var text = ExtractOutputText(result);
        if (text.Length == 0)
            return [];

        // The model can answer in prose instead of the schema — a refusal, or "no ads found".
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            return [];
        }

        using var _ = document;
        if (!document.RootElement.TryGetProperty("comparables", out var items)
            || items.ValueKind != JsonValueKind.Array)
            return [];

        var queryTokens = ComparableQueryTokens(query);
        var comparables = new List<Comparable>();

        foreach (var item in items.EnumerateArray())
        {
            var title = item.GetProperty("title").GetString() ?? "";
            var relevance = Relevance(title, queryTokens);
            var price = item.GetProperty("price").GetInt32();
            var url = item.GetProperty("url").GetString() ?? "";

            if (relevance < 0.35 || price is < 25 or > 100000 || url.Length == 0)
                continue;

            comparables.Add(new Comparable
            {
                Title = title,
                Price = price,
                Relevance = Math.Round(relevance, 2),
                Url = url,
            });
        }

        return comparables;
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

    static string ExtractOutputText(JsonElement result)
    {
        if (!result.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
            return "";

        var texts = new List<string>();

        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var entry in content.EnumerateArray())
            {
                if (entry.TryGetProperty("type", out var type)
                    && type.GetString() == "output_text"
                    && entry.TryGetProperty("text", out var value))
                {
                    texts.Add(value.GetString() ?? "");
                }
            }
        }

        return string.Join("\n", texts);
    }
}
