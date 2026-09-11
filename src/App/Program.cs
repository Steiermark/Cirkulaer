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

app.MapGet("/config", async (
    HttpContext context,
    IConfiguration config,
    IWebHostEnvironment env,
    CancellationToken ct) =>
{
    // Prefer HTTP locally because phones cannot trust localhost development HTTPS
    // certificates. Azure can still provide only HTTPS, which is returned unchanged.
    var apiBaseUrl = config["services:api:http:0"] ?? config["services:api:https:0"];

    return Results.Json(new
    {
        apiBaseUrl = ResolveClientReachableApiBaseUrl(
            apiBaseUrl ?? await ReadStaticApiBaseUrlAsync(env.WebRootPath, ct),
            context.Request),
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

static string ResolveClientReachableApiBaseUrl(string apiBaseUrl, HttpRequest request)
{
    if (!Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out var uri))
        return apiBaseUrl;

    if (!IsLoopbackHost(uri.Host) || IsLoopbackHost(request.Host.Host))
        return apiBaseUrl;

    var builder = new UriBuilder(uri)
    {
        Host = request.Host.Host,
    };
    return builder.Uri.ToString().TrimEnd('/');
}

static bool IsLoopbackHost(string? host) =>
    string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
    || string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
    || string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase);
