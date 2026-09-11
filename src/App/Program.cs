using System.Text.Json;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// /config picks the Api scheme from the incoming request, and ACA terminates TLS at the
// ingress — without this the container sees plain HTTP and hands an https page an http Api.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // ACA ingress IP is not known ahead of time; the container is only reachable through ingress.
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();
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
    // Match the scheme the page itself was served over. Phones cannot trust localhost
    // development certificates, so an http page must get an http Api — but an https page
    // must get an https Api, or the browser blocks the call as mixed content and the
    // preflight dies on Azure's http→https 301. Aspire injects both keys in every
    // environment, including Azure Container Apps.
    var apiHttps = config["services:api:https:0"];
    var apiHttp = config["services:api:http:0"];
    var apiBaseUrl = context.Request.IsHttps ? apiHttps ?? apiHttp : apiHttp ?? apiHttps;

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
