using System.Text;
using System.Text.Json;
using Api.Decision;

namespace Api.Ai;

public sealed class AnthropicVisionProvider(HttpClient http, IConfiguration config) : IVisionProvider
{
    public string Name => "anthropic";

    public async Task<Assessment> AnalyzeAsync(IReadOnlyList<string> imageDataUrls, CancellationToken ct)
    {
        var content = new List<object>();
        foreach (var url in imageDataUrls.Take(4))
        {
            var (mediaType, data) = VisionJson.SplitDataUrl(url);
            content.Add(new
            {
                type = "image",
                source = new { type = "base64", media_type = mediaType, data },
            });
        }
        content.Add(new
        {
            type = "text",
            text = VisionPrompt.Text
                + "\n\nSvar udelukkende med et JSON-objekt der matcher dette skema:\n"
                + AssessmentSchema.Json,
        });

        var body = new
        {
            model = config["Ai:AnthropicModel"] ?? "claude-sonnet-5",
            max_tokens = 2000,
            messages = new[] { new { role = "user", content } },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("x-api-key",
            config["Ai:AnthropicApiKey"] ?? throw new InvalidOperationException("Ai:AnthropicApiKey not configured"));
        request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");

        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return ParseResponse(document.RootElement);
    }

    public static Assessment ParseResponse(JsonElement result)
    {
        var texts = new List<string>();
        if (result.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in content.EnumerateArray())
            {
                if (entry.TryGetProperty("type", out var type)
                    && type.GetString() == "text"
                    && entry.TryGetProperty("text", out var value))
                {
                    texts.Add(value.GetString() ?? "");
                }
            }
        }

        if (texts.Count == 0)
            throw new InvalidOperationException("AI returnerede ikke tekst.");

        return VisionJson.NormalizeFromText(string.Join("\n", texts));
    }
}
