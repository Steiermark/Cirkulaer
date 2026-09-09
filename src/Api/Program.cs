using System.Threading.RateLimiting;
using Api.Ai;
using Api.Endpoints;
using Api.SaleAssist;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Photos arrive base64-encoded, up to four per request, which inflates them ~33%.
// Python's http.server had no body cap; Kestrel defaults to ~30 MB and would 413.
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 100L * 1024 * 1024);

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // ACA ingress IP is not known ahead of time; the container is only reachable through ingress.
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var requestsPerMinute = builder.Configuration.GetValue("RateLimit:RequestsPerMinute", 20);
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = requestsPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await context.HttpContext.Response.WriteAsync("Too many requests.", ct);
    };
});

builder.Services.AddHttpClient<OpenAiVisionProvider>(client => client.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddHttpClient<AnthropicVisionProvider>(client => client.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddHttpClient<GeminiVisionProvider>(client => client.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddHttpClient<PriceSearchClient>(client => client.Timeout = TimeSpan.FromSeconds(12));

builder.Services.AddSingleton<LocalTestProvider>();
builder.Services.AddSingleton<VisionProviderFactory>();
builder.Services.AddScoped<IPriceSearch>(services => services.GetRequiredService<PriceSearchClient>());
builder.Services.AddScoped<SaleAssistBuilder>();

var app = builder.Build();

app.UseForwardedHeaders();
app.UseRateLimiter();
app.UseApiKeyAuth();

app.MapDefaultEndpoints();
app.MapApiEndpoints();

app.Run();

public partial class Program;
