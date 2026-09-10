var builder = DistributedApplication.CreateBuilder(args);

var openAiApiKey = builder.AddParameter("openAiApiKey", secret: true);
var anthropicApiKey = builder.AddParameter("anthropicApiKey", secret: true);
var geminiApiKey = builder.AddParameter("geminiApiKey", secret: true);
var authApiKey = builder.AddParameter("authApiKey", secret: true);

// Provider and model are read from Api/appsettings.json; these override them per
// environment, so switching vendor or model is a config change and a revision restart
// rather than a rebuild.
var defaultProvider = builder.Configuration["Ai:DefaultProvider"];
var openAiModel = builder.Configuration["Ai:Providers:openai:Model"];
var anthropicModel = builder.Configuration["Ai:Providers:anthropic:Model"];
var geminiModel = builder.Configuration["Ai:Providers:gemini:Model"];

var api = builder.AddProject<Projects.Api>("api")
    .WithExternalHttpEndpoints()
    .WithEnvironment("Ai__OpenAiApiKey", openAiApiKey)
    .WithEnvironment("Ai__AnthropicApiKey", anthropicApiKey)
    .WithEnvironment("Ai__GeminiApiKey", geminiApiKey)
    .WithEnvironment("Auth__ApiKey", authApiKey);

if (!string.IsNullOrWhiteSpace(defaultProvider))
    api.WithEnvironment("Ai__DefaultProvider", defaultProvider);
if (!string.IsNullOrWhiteSpace(openAiModel))
    api.WithEnvironment("Ai__Providers__openai__Model", openAiModel);
if (!string.IsNullOrWhiteSpace(anthropicModel))
    api.WithEnvironment("Ai__Providers__anthropic__Model", anthropicModel);
if (!string.IsNullOrWhiteSpace(geminiModel))
    api.WithEnvironment("Ai__Providers__gemini__Model", geminiModel);

builder.AddProject<Projects.App>("app")
    .WithReference(api)
    .WithExternalHttpEndpoints()
    .WithEnvironment("Auth__ApiKey", authApiKey)
    .WaitFor(api);

builder.Build().Run();
