using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    // legacy/server.py sent this on every response. Without it a phone can keep serving a
    // stale app.js after a deploy, which looks like the change never shipped.
    OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-store",
});

app.MapGet("/config", async (IConfiguration config, IWebHostEnvironment env, CancellationToken ct) =>
{
    // Aspire injects services__api__https__0 in Azure Container Apps too, not just locally,
    // so read it in every environment. wwwroot/config.json stays as a manual override.
    var apiBaseUrl = config["services:api:https:0"] ?? config["services:api:http:0"];

    return Results.Json(new
    {
        apiBaseUrl = apiBaseUrl ?? await ReadStaticApiBaseUrlAsync(env.WebRootPath, ct),
        apiKey = config["Auth:ApiKey"],
    });
});

app.MapDefaultEndpoints();

app.Run();

static async Task<string> ReadStaticApiBaseUrlAsync(string? webRootPath, CancellationToken ct)
{
    if (webRootPath is null)
        return "";

    var path = Path.Combine(webRootPath, "config.json");
    if (!File.Exists(path))
        return "";

    using var stream = File.OpenRead(path);
    using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    return document.RootElement.TryGetProperty("apiBaseUrl", out var value) ? value.GetString() ?? "" : "";
}
