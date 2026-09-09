using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Tests;

public class EndpointContractTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    static readonly object Chair = new
    {
        object_name = "Kontorstol",
        category = "Møbler og indbo",
        materials = new[] { "metal" },
    };

    HttpClient Client() => factory.CreateClient();

    static async Task<JsonElement> BodyOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    public async Task Recommend_returns_a_recommendation_envelope()
    {
        var response = await Client().PostAsJsonAsync("/api/recommend", new
        {
            assessment = Chair,
            answers = new { works = "yes", reason = "no_need" },
        });

        response.EnsureSuccessStatusCode();
        var body = await BodyOf(response);
        Assert.True(body.TryGetProperty("recommendation", out var recommendation));
        Assert.Equal("Kontorstol", recommendation.GetProperty("object_name").GetString());
        Assert.True(recommendation.TryGetProperty("producer_program", out _));
    }

    [Fact]
    public async Task Recommend_rejects_a_non_object_assessment_with_the_danish_message()
    {
        var response = await Client().PostAsJsonAsync("/api/recommend", new { assessment = "not an object" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await BodyOf(response);
        Assert.Equal("Assessment og svar skal sendes som objekter.", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Malformed_json_returns_400_not_500()
    {
        var response = await Client().PostAsync("/api/recommend",
            new StringContent("{ not json", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await BodyOf(response)).TryGetProperty("error", out _));
    }

    [Fact]
    public async Task Sale_assist_returns_every_field_app_js_reads()
    {
        var response = await Client().PostAsJsonAsync("/api/sale-assist", new
        {
            assessment = Chair,
            answers = new { works = "yes", reason = "no_need", damage = "no" },
            recommendation = new { },
        });

        response.EnsureSuccessStatusCode();
        var sale = (await BodyOf(response)).GetProperty("sale");

        foreach (var field in new[]
        {
            "object_name", "details", "price", "price_note", "search_note", "search_url",
            "marketplace_search_url", "reshopper_relevant", "reshopper_url", "reshopper_note",
            "ad_text", "marketplace_note", "signals", "comparables", "price_confidence",
        })
        {
            Assert.True(sale.TryGetProperty(field, out _), $"missing {field}");
        }
    }

    [Fact]
    public async Task Analyze_rejects_a_payload_with_no_images()
    {
        var response = await Client().PostAsJsonAsync("/api/analyze", new { images = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Upload 1-4 gyldige billeder.",
            (await BodyOf(response)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Analyze_rejects_a_non_image_data_url()
    {
        var response = await Client().PostAsJsonAsync("/api/analyze", new
        {
            images = new[] { new { filename = "a.txt", imageDataUrl = "data:text/plain;base64,AAAA" } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Analyze_falls_back_to_a_flagged_test_analysis_without_keys()
    {
        var response = await Client().PostAsJsonAsync("/api/analyze", new
        {
            filename = "billy.jpg",
            images = new[] { new { filename = "billy.jpg", imageDataUrl = "data:image/jpeg;base64,AAAA" } },
        });

        response.EnsureSuccessStatusCode();
        var body = await BodyOf(response);

        Assert.Equal("test", body.GetProperty("mode").GetString());
        Assert.Contains("Testversion", body.GetProperty("message").GetString());
        Assert.Equal("BILLY-reol", body.GetProperty("assessment").GetProperty("object_name").GetString());
    }

    // Kestrel's default cap is ~30 MB; four base64 phone photos exceed it easily.
    [Fact]
    public async Task A_large_upload_reaches_the_handler_rather_than_413()
    {
        var payload = $$"""
            {"filename":"big.jpg","images":[{"filename":"big.jpg","imageDataUrl":"data:image/jpeg;base64,{{new string('A', 40 * 1024 * 1024)}}"}]}
            """;

        var response = await Client().PostAsync("/api/analyze",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        Assert.NotEqual(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    // The gate is a no-op unless Auth:ApiKey is configured, so prove it both ways.
    WebApplicationFactory<Program> WithApiKey(string key) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:ApiKey"] = key })));

    [Fact]
    public async Task A_request_without_the_api_key_is_rejected_when_one_is_configured()
    {
        var response = await WithApiKey("secret").CreateClient()
            .PostAsJsonAsync("/api/recommend", new { assessment = Chair, answers = new { } });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_request_with_the_wrong_api_key_is_rejected()
    {
        var client = WithApiKey("secret").CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "wrong");

        var response = await client.PostAsJsonAsync("/api/recommend",
            new { assessment = Chair, answers = new { } });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_request_with_the_right_api_key_is_allowed()
    {
        var client = WithApiKey("secret").CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "secret");

        var response = await client.PostAsJsonAsync("/api/recommend",
            new { assessment = Chair, answers = new { } });

        response.EnsureSuccessStatusCode();
    }
}
