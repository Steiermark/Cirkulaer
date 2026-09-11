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

// App and Api are separate container apps, so every browser call is cross-origin and the
// X-Api-Key header forces a preflight.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyMethod().AllowAnyHeader();

        if (allowedOrigins is null || allowedOrigins.Length == 0 || allowedOrigins.Contains("*"))
            policy.AllowAnyOrigin();
        else
            policy.WithOrigins(allowedOrigins);
    }));

builder.Services.AddHttpClient<OpenAiVisionProvider>(client => client.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddHttpClient<AnthropicVisionProvider>(client => client.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddHttpClient<GeminiVisionProvider>(client => client.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddHttpClient<DbaPriceSearch>(client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddHttpClient<LookalikeFilter>(client => client.Timeout = TimeSpan.FromSeconds(30));

builder.Services.AddSingleton<LocalTestProvider>();
builder.Services.AddSingleton<VisionProviderFactory>();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<IPriceSearch>(services => services.GetRequiredService<DbaPriceSearch>());
builder.Services.AddScoped<ILookalikeFilter>(services => services.GetRequiredService<LookalikeFilter>());
builder.Services.AddScoped<SaleAssistBuilder>();

var app = builder.Build();

app.UseForwardedHeaders();
app.UseRateLimiter();
app.UseCors();
app.UseApiKeyAuth();

app.MapDefaultEndpoints();
app.MapApiEndpoints();

app.Run();

public partial class Program;
