using System.Text;
using System.Text.Json;
using Api.Decision;

namespace Api.Ai;

public sealed class OpenAiVisionProvider(HttpClient http, IConfiguration config) : IVisionProvider
{
    public string Name => "openai";

    public async Task<Assessment> AnalyzeAsync(IReadOnlyList<string> imageDataUrls, CancellationToken ct)
    {
        var model = config["Ai:Model"] ?? "gpt-5";

        var input = new List<object>
        {
            new
            {
                role = "user",
                content = new List<object> { new { type = "input_text", text = VisionPrompt.Text } }
                    .Concat(imageDataUrls.Take(4).Select(url => (object)new { type = "input_image", image_url = url }))
                    .ToList(),
            },
        };

        var body = new
        {
            model,
            input,
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "circular_object_assessment",
                    strict = true,
                    schema = AssessmentSchema.Element,
                },
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Authorization",
            $"Bearer {config["Ai:OpenAiApiKey"] ?? throw new InvalidOperationException("Ai:OpenAiApiKey not configured")}");

        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return ParseResponse(document.RootElement);
    }

    public static Assessment ParseResponse(JsonElement result) =>
        VisionJson.NormalizeFromText(ExtractResponseText(result));

    static string ExtractResponseText(JsonElement result)
    {
        var texts = new List<string>();

        if (result.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
        {
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
        }

        if (texts.Count == 0)
            throw new InvalidOperationException("AI returnerede ikke tekst.");

        return string.Join("\n", texts);
    }
}
