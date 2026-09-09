using System.Text;
using System.Text.Json;
using Api.Decision;

namespace Api.Ai;

public sealed class GeminiVisionProvider(HttpClient http, IConfiguration config) : IVisionProvider
{
    public string Name => "gemini";

    public async Task<Assessment> AnalyzeAsync(IReadOnlyList<string> imageDataUrls, CancellationToken ct)
    {
        var parts = new List<object>
        {
            new
            {
                text = VisionPrompt.Text
                    + "\n\nSvar udelukkende med et JSON-objekt der matcher dette skema:\n"
                    + AssessmentSchema.Json,
            },
        };

        foreach (var url in imageDataUrls.Take(4))
        {
            var (mediaType, data) = VisionJson.SplitDataUrl(url);
            parts.Add(new { inline_data = new { mime_type = mediaType, data } });
        }

        var body = new
        {
            contents = new[] { new { role = "user", parts } },
            generationConfig = new { responseMimeType = "application/json" },
        };

        var model = config["Ai:GeminiModel"] ?? "gemini-2.5-flash";
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
        return ParseResponse(document.RootElement);
    }

    public static Assessment ParseResponse(JsonElement result)
    {
        var texts = new List<string>();

        if (result.TryGetProperty("candidates", out var candidates) && candidates.ValueKind == JsonValueKind.Array)
        {
            foreach (var candidate in candidates.EnumerateArray())
            {
                if (!candidate.TryGetProperty("content", out var content)
                    || !content.TryGetProperty("parts", out var parts)
                    || parts.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var value))
                        texts.Add(value.GetString() ?? "");
                }
            }
        }

        if (texts.Count == 0)
            throw new InvalidOperationException("AI returnerede ikke tekst.");

        return VisionJson.NormalizeFromText(string.Join("\n", texts));
    }
}
