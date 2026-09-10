using Api.Ai;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Tests;

public class VisionProviderFactoryTests
{
    static VisionProviderFactory Factory(params (string Key, string Value)[] settings)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value))
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddHttpClient();
        services.AddSingleton<LocalTestProvider>();
        services.AddSingleton<OpenAiVisionProvider>();
        services.AddSingleton<AnthropicVisionProvider>();
        services.AddSingleton<GeminiVisionProvider>();

        var provider = services.BuildServiceProvider();
        return new VisionProviderFactory(provider, config);
    }

    [Fact]
    public void With_no_keys_it_falls_back_to_the_local_test_provider()
    {
        var factory = Factory();

        Assert.False(factory.AnyProviderConfigured);
        Assert.IsType<LocalTestProvider>(factory.Resolve(null));
    }

    [Fact]
    public void DefaultProvider_from_config_selects_the_provider()
    {
        var factory = Factory(
            ("Ai:OpenAiApiKey", "a"),
            ("Ai:AnthropicApiKey", "b"),
            ("Ai:DefaultProvider", "anthropic"));

        Assert.Equal("anthropic", factory.Resolve(null).Name);
    }

    [Fact]
    public void An_explicit_request_beats_the_configured_default()
    {
        var factory = Factory(
            ("Ai:OpenAiApiKey", "a"),
            ("Ai:GeminiApiKey", "c"),
            ("Ai:DefaultProvider", "openai"));

        Assert.Equal("gemini", factory.Resolve("gemini").Name);
    }

    [Fact]
    public void A_configured_default_without_a_key_falls_back_to_one_that_has_a_key()
    {
        var factory = Factory(
            ("Ai:AnthropicApiKey", "b"),
            ("Ai:DefaultProvider", "openai"));

        Assert.Equal("anthropic", factory.Resolve(null).Name);
    }

    [Fact]
    public void A_request_for_an_unconfigured_provider_falls_back_rather_than_failing()
    {
        var factory = Factory(("Ai:OpenAiApiKey", "a"), ("Ai:DefaultProvider", "openai"));

        Assert.Equal("openai", factory.Resolve("gemini").Name);
    }

    [Fact]
    public void Available_providers_reflect_which_keys_are_set()
    {
        var factory = Factory(("Ai:OpenAiApiKey", "a"), ("Ai:GeminiApiKey", "c"));

        Assert.Equal(["openai", "gemini"], factory.AvailableProviders);
    }
}
