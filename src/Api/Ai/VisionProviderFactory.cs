using Api.Decision;

namespace Api.Ai;

// Stands in when no provider key is configured, so the app runs without one.
// legacy/server.py:142-150 kept the same behaviour.
public sealed class LocalTestProvider : IVisionProvider
{
    public string Name => "local";

    public Task<Assessment> AnalyzeAsync(IReadOnlyList<string> imageDataUrls, CancellationToken ct) =>
        Task.FromResult(TestAssessments.Build(""));

    public Assessment Build(string filename) => TestAssessments.Build(filename);
}

public sealed class VisionProviderFactory(IServiceProvider services, IConfiguration config)
{
    public bool AnyProviderConfigured => Configured().Count > 0;

    public IReadOnlyList<string> AvailableProviders => Configured();

    public IVisionProvider Resolve(string? requested)
    {
        var available = Configured();
        if (available.Count == 0)
            return services.GetRequiredService<LocalTestProvider>();

        var name = Pick(requested, available);
        return name switch
        {
            "openai" => services.GetRequiredService<OpenAiVisionProvider>(),
            "anthropic" => services.GetRequiredService<AnthropicVisionProvider>(),
            "gemini" => services.GetRequiredService<GeminiVisionProvider>(),
            _ => services.GetRequiredService<LocalTestProvider>(),
        };
    }

    string Pick(string? requested, IReadOnlyList<string> available)
    {
        if (!string.IsNullOrEmpty(requested) && available.Contains(requested))
            return requested;

        var configured = config["Ai:DefaultProvider"];
        if (!string.IsNullOrEmpty(configured) && available.Contains(configured))
            return configured;

        return available[0];
    }

    List<string> Configured()
    {
        var names = new List<string>();
        if (!string.IsNullOrWhiteSpace(config["Ai:OpenAiApiKey"])) names.Add("openai");
        if (!string.IsNullOrWhiteSpace(config["Ai:AnthropicApiKey"])) names.Add("anthropic");
        if (!string.IsNullOrWhiteSpace(config["Ai:GeminiApiKey"])) names.Add("gemini");
        return names;
    }
}
