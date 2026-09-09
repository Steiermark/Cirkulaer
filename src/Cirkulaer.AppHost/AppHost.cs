var builder = DistributedApplication.CreateBuilder(args);

var openAiApiKey = builder.AddParameter("openAiApiKey", secret: true);
var anthropicApiKey = builder.AddParameter("anthropicApiKey", secret: true);
var geminiApiKey = builder.AddParameter("geminiApiKey", secret: true);
var authApiKey = builder.AddParameter("authApiKey", secret: true);

var api = builder.AddProject<Projects.Api>("api")
    .WithExternalHttpEndpoints()
    .WithEnvironment("Ai__OpenAiApiKey", openAiApiKey)
    .WithEnvironment("Ai__AnthropicApiKey", anthropicApiKey)
    .WithEnvironment("Ai__GeminiApiKey", geminiApiKey)
    .WithEnvironment("Auth__ApiKey", authApiKey);

builder.AddProject<Projects.App>("app")
    .WithReference(api)
    .WithExternalHttpEndpoints()
    .WithEnvironment("Auth__ApiKey", authApiKey)
    .WaitFor(api);

builder.Build().Run();
