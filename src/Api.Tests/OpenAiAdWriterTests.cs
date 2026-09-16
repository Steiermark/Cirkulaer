using System.Net;
using System.Text;
using System.Text.Json;
using Api.Decision;
using Api.SaleAssist;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Api.Tests;

public class OpenAiAdWriterTests
{
    const string Response = """
        {"status":"completed","output":[
          {"type":"reasoning","summary":[]},
          {"type":"message","content":[{"type":"output_text","text":"Jeg sælger min bordlampe til et hyggeligt læsehjørne."}]}
        ]}
        """;

    sealed class Handler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? Body { get; private set; }
        public string? Authorization { get; private set; }
        public string? Url { get; private set; }
        public string Json { get; init; } = Response;
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public bool Cancel { get; init; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            if (Cancel) throw new OperationCanceledException(ct);
            Body = await request.Content!.ReadAsStringAsync(ct);
            Authorization = request.Headers.Authorization?.ToString();
            Url = request.RequestUri?.ToString();
            return new HttpResponseMessage(Status) { Content = new StringContent(Json, Encoding.UTF8, "application/json") };
        }
    }

    static OpenAiAdWriter Create(Handler handler, IMemoryCache cache, string? key = "test-key") =>
        new(new HttpClient(handler), new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Ai:OpenAiApiKey"] = key, ["Ai:Providers:openai:Model"] = "configured-model",
            }).Build(), cache, NullLogger<OpenAiAdWriter>.Instance);

    [Fact]
    public async Task Calls_responses_with_facts_only_and_reuses_copy_for_price_refinement()
    {
        var handler = new Handler();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var writer = Create(handler, cache);
        var assessment = new Assessment { ObjectName = "Bordlampe", Materials = ["glas"] };
        var answers = new Dictionary<string, string?> { ["works"] = "partly", ["private_note"] = "not for the provider" };

        var text = await writer.WriteIntroductionAsync("Bordlampe", assessment, answers, CancellationToken.None);
        Assert.Equal(text, await writer.WriteIntroductionAsync("Bordlampe", assessment, answers, CancellationToken.None));
        Assert.Equal(1, handler.Calls);
        Assert.Contains("læsehjørne", text);
        Assert.Equal("https://api.openai.com/v1/responses", handler.Url);
        Assert.Equal("Bearer test-key", handler.Authorization);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("configured-model", body.RootElement.GetProperty("model").GetString());
        Assert.False(body.RootElement.GetProperty("store").GetBoolean());
        var input = body.RootElement.GetProperty("input").GetString()!;
        Assert.Contains("Fungerer kun delvist.", input);
        Assert.DoesNotContain("private_note", input);
        Assert.DoesNotContain("price", input);

        await writer.WriteIntroductionAsync("Bordlampe", assessment,
            new Dictionary<string, string?> { ["works"] = "no" }, CancellationToken.None);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Missing_key_uses_local_copy_without_an_http_call()
    {
        var handler = new Handler();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        Assert.Null(await Create(handler, cache, null).WriteIntroductionAsync("Lampe", new(), new Dictionary<string, string?>(), CancellationToken.None));
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"status\":\"incomplete\"}")]
    [InlineData("{\"status\":\"completed\",\"output\":[]}")]
    [InlineData("{\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"refusal\"}]}]}")]
    public async Task Invalid_or_missing_text_uses_local_copy(string json)
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var writer = Create(new Handler { Json = json }, cache);
        Assert.Null(await writer.WriteIntroductionAsync("Lampe", new(), new Dictionary<string, string?>(), CancellationToken.None));
    }

    [Fact]
    public async Task Provider_failure_or_timeout_uses_local_copy_but_caller_cancellation_propagates()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var failing = Create(new Handler { Status = HttpStatusCode.TooManyRequests }, cache);
        Assert.Null(await failing.WriteIntroductionAsync("Lampe", new(), new Dictionary<string, string?>(), CancellationToken.None));
        var timedOut = Create(new Handler { Cancel = true }, cache);
        Assert.Null(await timedOut.WriteIntroductionAsync("Lampe", new(), new Dictionary<string, string?>(), CancellationToken.None));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            timedOut.WriteIntroductionAsync("Lampe", new(), new Dictionary<string, string?>(), cancelled.Token));
    }
}
