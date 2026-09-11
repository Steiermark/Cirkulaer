using System.Net;
using System.Text.Json;
using Api.SaleAssist;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Api.Tests;

public class LookalikeFilterTests
{
    const string Photo = "data:image/jpeg;base64,AAAA";
    const string Name = "Roland FP-30X";

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
        var (kept, match) = LookalikeFilter.Apply([Ad(1), Ad(2), Ad(3)], ["similar", "same", "different"]);

        Assert.Equal([2], kept.Select(item => item.Price / 100));
        Assert.Equal("same", match);
    }

    [Fact]
    public void Similar_is_used_when_nothing_is_the_same_product()
    {
        var (kept, match) = LookalikeFilter.Apply([Ad(1), Ad(2), Ad(3)], ["similar", "different", "similar"]);

        Assert.Equal([1, 3], kept.Select(item => item.Price / 100));
        Assert.Equal("similar", match);
    }

    // A broad median beats the category bands, and the note says which it was.
    [Fact]
    public void Nothing_alike_falls_back_to_every_row()
    {
        var (kept, match) = LookalikeFilter.Apply([Ad(1), Ad(2)], ["different", "different"]);

        Assert.Equal(2, kept.Count);
        Assert.Equal("all", match);
    }

    [Fact]
    public async Task Nothing_alike_returns_rows_past_the_cap_too()
    {
        var ads = Enumerable.Range(1, 15).Select(id => Ad(id)).ToList();
        var grades = string.Join(",", Enumerable.Repeat("\"different\"", 12));
        var handler = new Handler(GeminiText($$"""{"grades":[{{grades}}]}"""));

        var kept = await Filter(handler).KeepLookalikesAsync(Name, Photo, ads, CancellationToken.None);

        Assert.Equal(15, kept.Comparables.Count);
        Assert.Equal("all", kept.Match);
    }

    [Fact]
    public async Task Grades_come_back_in_ad_order_and_ads_without_a_thumbnail_are_kept()
    {
        var handler = new Handler(GeminiText("""{"grades":["different","same"]}"""));

        var kept = await Filter(handler).KeepLookalikesAsync(Name, Photo, [Ad(1), Ad(2), Ad(3, image: null)], CancellationToken.None);

        Assert.Equal([2, 3], kept.Comparables.Select(item => item.Price / 100));
        Assert.Equal(2, handler.Fetched.Count);
        Assert.Contains("AAAA", handler.GeminiRequest);
        Assert.Contains("Roland FP-30X", handler.GeminiRequest);
        Assert.Contains("1. Lampe 1", handler.GeminiRequest);
        Assert.Contains("2. Lampe 2", handler.GeminiRequest);
    }

    [Fact]
    public async Task Ads_past_the_thumbnail_cap_are_dropped_rather_than_kept_unjudged()
    {
        var ads = Enumerable.Range(1, 15).Select(id => Ad(id)).ToList();
        var grades = string.Join(",", Enumerable.Repeat("\"different\"", 11).Append("\"same\""));
        var handler = new Handler(GeminiText($$"""{"grades":[{{grades}}]}"""));

        var kept = await Filter(handler).KeepLookalikesAsync(Name, Photo, ads, CancellationToken.None);

        Assert.Equal([12], kept.Comparables.Select(item => item.Price / 100));
    }

    // A failed grading must not throw the search result away.
    [Fact]
    public async Task A_failed_grading_keeps_every_comparable()
    {
        var handler = new Handler("", HttpStatusCode.ServiceUnavailable);

        var kept = await Filter(handler).KeepLookalikesAsync(Name, Photo, [Ad(1), Ad(2)], CancellationToken.None);

        Assert.Equal(2, kept.Comparables.Count);
    }

    [Fact]
    public async Task A_grade_list_of_the_wrong_length_keeps_every_comparable()
    {
        var handler = new Handler(GeminiText("""{"grades":["same"]}"""));

        var kept = await Filter(handler).KeepLookalikesAsync(Name, Photo, [Ad(1), Ad(2)], CancellationToken.None);

        Assert.Equal(2, kept.Comparables.Count);
    }
}
