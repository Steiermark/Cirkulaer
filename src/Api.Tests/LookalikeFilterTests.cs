using System.Net;
using System.Text.Json;
using Api.SaleAssist;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Api.Tests;

public class LookalikeFilterTests
{
    const string Photo = "data:image/jpeg;base64,AAAA";

    static Comparable Ad(int id, string? image = "https://images.dbastatic.dk/x") => new()
    {
        Title = $"Lampe {id}",
        Price = 100 * id,
        Relevance = 0.5,
        Url = $"https://www.dba.dk/recommerce/forsale/item/{id}",
        Image = image,
    };

    static string GeminiText(string json) =>
        JsonSerializer.Serialize(new
        {
            candidates = new[] { new { content = new { parts = new[] { new { text = json } } } } },
        });

    sealed class Handler(string geminiBody, HttpStatusCode geminiStatus = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<string> Fetched { get; } = [];
        public string? GeminiRequest { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            if (url.Contains("generativelanguage.googleapis.com"))
            {
                GeminiRequest = await request.Content!.ReadAsStringAsync(ct);
                return new HttpResponseMessage(geminiStatus) { Content = new StringContent(geminiBody) };
            }

            Fetched.Add(url);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([0xFF, 0xD8, 0xFF]) { Headers = { ContentType = new("image/jpeg") } },
            };
        }
    }

    static LookalikeFilter Filter(HttpMessageHandler handler) =>
        new(new HttpClient(handler),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Ai:GeminiApiKey"] = "test-key" })
                .Build(),
            NullLogger<LookalikeFilter>.Instance);

    [Fact]
    public void Same_product_wins_over_similar()
    {
        var kept = LookalikeFilter.Apply([Ad(1), Ad(2), Ad(3)], ["similar", "same", "different"]);

        Assert.Equal([2], kept.Select(item => item.Price / 100));
    }

    [Fact]
    public void Similar_is_used_when_nothing_is_the_same_product()
    {
        var kept = LookalikeFilter.Apply([Ad(1), Ad(2), Ad(3)], ["similar", "different", "similar"]);

        Assert.Equal([1, 3], kept.Select(item => item.Price / 100));
    }

    [Fact]
    public void Nothing_alike_gives_no_comparables()
    {
        Assert.Empty(LookalikeFilter.Apply([Ad(1), Ad(2)], ["different", "different"]));
    }

    [Fact]
    public async Task Grades_come_back_in_ad_order_and_ads_without_a_thumbnail_are_kept()
    {
        var handler = new Handler(GeminiText("""{"grades":["different","same"]}"""));

        var kept = await Filter(handler).KeepLookalikesAsync(Photo, [Ad(1), Ad(2), Ad(3, image: null)], CancellationToken.None);

        Assert.Equal([2, 3], kept.Select(item => item.Price / 100));
        Assert.Equal(2, handler.Fetched.Count);
        Assert.Contains("AAAA", handler.GeminiRequest);
    }

    [Fact]
    public async Task Ads_past_the_thumbnail_cap_are_dropped_rather_than_kept_unjudged()
    {
        var ads = Enumerable.Range(1, 15).Select(id => Ad(id)).ToList();
        var grades = string.Join(",", Enumerable.Repeat("\"different\"", 11).Append("\"same\""));
        var handler = new Handler(GeminiText($$"""{"grades":[{{grades}}]}"""));

        var kept = await Filter(handler).KeepLookalikesAsync(Photo, ads, CancellationToken.None);

        Assert.Equal([12], kept.Select(item => item.Price / 100));
    }

    // A failed grading must not throw the search result away.
    [Fact]
    public async Task A_failed_grading_keeps_every_comparable()
    {
        var handler = new Handler("", HttpStatusCode.ServiceUnavailable);

        var kept = await Filter(handler).KeepLookalikesAsync(Photo, [Ad(1), Ad(2)], CancellationToken.None);

        Assert.Equal(2, kept.Count);
    }

    [Fact]
    public async Task A_grade_list_of_the_wrong_length_keeps_every_comparable()
    {
        var handler = new Handler(GeminiText("""{"grades":["same"]}"""));

        var kept = await Filter(handler).KeepLookalikesAsync(Photo, [Ad(1), Ad(2)], CancellationToken.None);

        Assert.Equal(2, kept.Count);
    }
}
