using System.Text.Json;
using Api.Ai;
using Api.Decision;
using Api.SaleAssist;

namespace Api.Endpoints;

public static class ApiEndpoints
{
    public static void MapApiEndpoints(this WebApplication app)
    {
        app.MapPost("/api/analyze", Analyze).DisableAntiforgery();
        app.MapPost("/api/recommend", Recommend);
        app.MapPost("/api/sale-assist", SaleAssist);
    }

    static async Task<IResult> Analyze(
        HttpContext context, VisionProviderFactory factory, CancellationToken ct)
    {
        JsonElement payload;
        try
        {
            payload = await ReadJsonAsync(context, ct);
        }
        catch (JsonException)
        {
            return Error("Ugyldig JSON i forespørgslen.");
        }

        var (imageDataUrls, filenames) = ReadImages(payload);
        if (imageDataUrls.Count == 0 || imageDataUrls.Any(url => !url.StartsWith("data:image/")))
            return Error("Upload 1-4 gyldige billeder.");

        if (!factory.AnyProviderConfigured)
        {
            return Results.Json(new
            {
                assessment = TestAssessments.Build(string.Join(" ", filenames)),
                mode = "test",
                message = "Testversion: billederne er modtaget og vist, men objektet "
                        + "er vurderet med lokal testlogik, fordi OPENAI_API_KEY mangler.",
            });
        }

        try
        {
            var provider = factory.Resolve(ReadString(payload, "provider"));
            var assessment = await provider.AnalyzeAsync(imageDataUrls, ct);
            return Results.Json(new { assessment, mode = "ai" });
        }
        catch (HttpRequestException exception)
        {
            return Results.Json(
                new { error = "OpenAI-kaldet fejlede.", detail = exception.Message },
                statusCode: 502);
        }
        catch (Exception exception) when (exception is TaskCanceledException or OperationCanceledException)
        {
            // Polly cancels the attempt before HttpClient's own timeout; without this the
            // request surfaces as a 500 with an empty body, which app.js cannot render.
            return Results.Json(
                new { error = "AI-analysen tog for lang tid. Prøv igen med et mindre billede." },
                statusCode: 504);
        }
        catch (InvalidOperationException exception)
        {
            return Error(exception.Message);
        }
    }

    static async Task<IResult> Recommend(HttpContext context, CancellationToken ct)
    {
        JsonElement payload;
        try
        {
            payload = await ReadJsonAsync(context, ct);
        }
        catch (JsonException)
        {
            return Error("Ugyldig JSON i forespørgslen.");
        }

        if (!IsObject(payload, "assessment") || !IsObject(payload, "answers"))
            return Error("Assessment og svar skal sendes som objekter.");

        var recommendation = DecisionEngine.BuildRecommendation(
            ReadAssessment(payload), ReadAnswers(payload));

        return Results.Json(new { recommendation });
    }

    static async Task<IResult> SaleAssist(
        HttpContext context, SaleAssistBuilder builder, CancellationToken ct)
    {
        JsonElement payload;
        try
        {
            payload = await ReadJsonAsync(context, ct);
        }
        catch (JsonException)
        {
            return Error("Ugyldig JSON i forespørgslen.");
        }

        if (!IsObject(payload, "assessment"))
            return Error("Assessment skal sendes som objekt.");
        if (HasNonObject(payload, "answers") || HasNonObject(payload, "recommendation"))
            return Error("Svar og anbefaling skal sendes som objekter.");

        var assessment = ReadAssessment(payload);
        var answers = ReadAnswers(payload);
        var recommendation = DecisionEngine.BuildRecommendation(assessment, answers);

        var photo = ReadImages(payload).DataUrls.FirstOrDefault(url => url.StartsWith("data:image/"));

        var sale = await builder.BuildAsync(assessment, answers, recommendation, photo, ct);
        return Results.Json(new { sale });
    }

    static async Task<JsonElement> ReadJsonAsync(HttpContext context, CancellationToken ct)
    {
        using var document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: ct);
        return document.RootElement.Clone();
    }

    static IResult Error(string message) => Results.Json(new { error = message }, statusCode: 400);

    static bool IsObject(JsonElement payload, string name) =>
        payload.ValueKind == JsonValueKind.Object
        && payload.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Object;

    static bool HasNonObject(JsonElement payload, string name) =>
        payload.ValueKind == JsonValueKind.Object
        && payload.TryGetProperty(name, out var value)
        && value.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null or JsonValueKind.Undefined);

    static Assessment ReadAssessment(JsonElement payload) =>
        payload.TryGetProperty("assessment", out var value)
            ? value.Deserialize<Assessment>() ?? new Assessment()
            : new Assessment();

    // Answers are free-form in the Python. Absent is meaningful and distinct from "unknown",
    // so only present string values are carried over.
    static Dictionary<string, string?> ReadAnswers(JsonElement payload)
    {
        var answers = new Dictionary<string, string?>();
        if (!payload.TryGetProperty("answers", out var value) || value.ValueKind != JsonValueKind.Object)
            return answers;

        foreach (var property in value.EnumerateObject())
        {
            answers[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => property.Value.ToString(),
                _ => null,
            };
        }

        return answers;
    }

    static string? ReadString(JsonElement payload, string name) =>
        payload.ValueKind == JsonValueKind.Object
        && payload.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    // app.js sends { filename, images: [{filename, imageDataUrl}], imageDataUrls: [...] }.
    // legacy/server.py:121-133 prefers `images`, falling back to the flat filename/imageDataUrl pair.
    static (List<string> DataUrls, List<string> Filenames) ReadImages(JsonElement payload)
    {
        var dataUrls = new List<string>();
        var filenames = new List<string>();

        if (payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("images", out var images)
            && images.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in images.EnumerateArray().Take(4))
            {
                dataUrls.Add(ReadString(item, "imageDataUrl") ?? "");
                filenames.Add(ReadString(item, "filename") ?? "");
            }

            return (dataUrls, filenames);
        }

        dataUrls.Add(ReadString(payload, "imageDataUrl") ?? "");
        filenames.Add(ReadString(payload, "filename") ?? "");
        return (dataUrls, filenames);
    }
}
