using System.Text;
using System.Text.Json;
using Api.Ai;

namespace Api.SaleAssist;

// Grades dba's ads against the user's photo so a generic query ("Bordlampe") does not
// price the item off every lamp on the site. dba's structured data carries a thumbnail
// per ad; one Gemini call sees the photo and the thumbnails and grades each ad. Ads
// without a thumbnail cannot be judged and stay in; ads past the thumbnail cap are
// dropped, since an unjudged tail would drown the judged rows.
public sealed class LookalikeFilter(HttpClient http, IConfiguration config, ILogger<LookalikeFilter> logger) : ILookalikeFilter
{
    const int MaxThumbnails = 12;

    static readonly string[] Grades = ["same", "similar", "different"];

    public async Task<IReadOnlyList<Comparable>> KeepLookalikesAsync(
        string photoDataUrl, IReadOnlyList<Comparable> comparables, CancellationToken ct)
    {
        var graded = comparables.Where(item => item.Image is not null).Take(MaxThumbnails).ToList();
        var ungraded = comparables.Where(item => item.Image is null).ToList();

        if (graded.Count == 0)
            return comparables;

        List<string> grades;
        try
        {
            var thumbnails = await Task.WhenAll(graded.Select(item => FetchAsync(item.Image!, ct)));
            grades = await GradeAsync(photoDataUrl, thumbnails, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Lookalike grading failed; keeping all {Count} comparables", comparables.Count);
            return comparables;
        }

        if (grades.Count != graded.Count)
        {
            logger.LogWarning("Lookalike grading returned {Got} grades for {Expected} ads; keeping all", grades.Count, graded.Count);
            return comparables;
        }

        var kept = Apply(graded, grades);
        logger.LogInformation("Lookalike grading kept {Kept} of {Graded} comparables ({Grades})",
            kept.Count, graded.Count, string.Join(",", grades));

        return [.. kept, .. ungraded];
    }

    public static List<Comparable> Apply(IReadOnlyList<Comparable> comparables, IReadOnlyList<string> grades)
    {
        var same = Pick(comparables, grades, "same");
        return same.Count > 0 ? same : Pick(comparables, grades, "similar");
    }

    static List<Comparable> Pick(IReadOnlyList<Comparable> comparables, IReadOnlyList<string> grades, string grade) =>
        comparables.Where((_, index) => grades[index] == grade).ToList();

    async Task<(string MediaType, string Data)> FetchAsync(string url, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        return (response.Content.Headers.ContentType?.MediaType ?? "image/jpeg", Convert.ToBase64String(bytes));
    }

    async Task<List<string>> GradeAsync(string photoDataUrl, (string MediaType, string Data)[] thumbnails, CancellationToken ct)
    {
        var (photoType, photoData) = VisionJson.SplitDataUrl(photoDataUrl);

        var parts = new List<object>
        {
            new
            {
                text = "Det første billede er brugerens egen genstand. De følgende "
                    + thumbnails.Length
                    + " billeder er annoncer fra en brugtmarkedsplads, i rækkefølge. "
                    + "Bedøm for hver annonce om den viser den samme model som brugerens genstand (\"same\"), "
                    + "en genstand af samme type og stil som en køber ville se som et reelt alternativ (\"similar\"), "
                    + "eller noget andet (\"different\"). "
                    + "Svar udelukkende med JSON: {\"grades\": [...]} med præcis "
                    + thumbnails.Length
                    + " elementer i annoncernes rækkefølge.",
            },
            new { inline_data = new { mime_type = photoType, data = photoData } },
        };

        foreach (var (mediaType, data) in thumbnails)
            parts.Add(new { inline_data = new { mime_type = mediaType, data } });

        var body = new
        {
            contents = new[] { new { role = "user", parts } },
            generationConfig = new { responseMimeType = "application/json" },
        };

        var model = config["Ai:Providers:gemini:Model"] ?? "gemini-2.5-flash";
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("x-goog-api-key",
            config["Ai:GeminiApiKey"] ?? throw new InvalidOperationException("Ai:GeminiApiKey not configured"));

        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return ParseGrades(document.RootElement);
    }

    public static List<string> ParseGrades(JsonElement result)
    {
        var text = new StringBuilder();

        if (result.TryGetProperty("candidates", out var candidates) && candidates.ValueKind == JsonValueKind.Array)
        {
            foreach (var candidate in candidates.EnumerateArray())
            {
                if (candidate.TryGetProperty("content", out var content)
                    && content.TryGetProperty("parts", out var parts)
                    && parts.ValueKind == JsonValueKind.Array)
                {
                    foreach (var part in parts.EnumerateArray())
                    {
                        if (part.TryGetProperty("text", out var value))
                            text.Append(value.GetString());
                    }
                }
            }
        }

        using var document = VisionJson.Parse(text.ToString());
        if (!document.RootElement.TryGetProperty("grades", out var grades) || grades.ValueKind != JsonValueKind.Array)
            return [];

        return grades.EnumerateArray()
            .Select(grade => (grade.GetString() ?? "").Trim().ToLowerInvariant())
            .Select(grade => Grades.Contains(grade) ? grade : "different")
            .ToList();
    }
}
