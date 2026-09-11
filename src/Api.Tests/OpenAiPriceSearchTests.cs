using System.Net;
using System.Text.Json;
using Api.SaleAssist;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Api.Tests;

public class OpenAiPriceSearchTests
{
    const string Query = "IKEA BILLY reol brugt pris Danmark";

    sealed class StubHandler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    static OpenAiPriceSearch Search(StubHandler handler) =>
        new(new HttpClient(handler),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Ai:OpenAiApiKey"] = "test-key" })
                .Build(),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<OpenAiPriceSearch>.Instance);

    static JsonElement Response() =>
        JsonDocument.Parse(File.ReadAllText("fixtures/openai-web-search-response.json")).RootElement;

    [Fact]
    public void Comparables_are_read_from_the_message_item()
    {
        var comparables = OpenAiPriceSearch.ParseComparables(Response(), Query);

        Assert.Equal([150, 500, 350], comparables.Select(item => item.Price));
    }

    static JsonElement Message(string comparablesJson) =>
        JsonDocument.Parse($$"""
            { "output": [ { "type": "message", "content": [
                { "type": "output_text", "text": {{JsonSerializer.Serialize($$"""{"comparables":{{comparablesJson}}}""")}} } ] } ] }
            """).RootElement;

    // The DuckDuckGo scrape shipped a furniture-assembly fee as a bookshelf price.
    [Fact]
    public void Titles_that_do_not_match_the_query_are_dropped()
    {
        var response = Message("""
            [{"title":"Hvad koster samling af reol? Priser fra 550 kr - Handyhand","price":550,"url":"https://handyhand.dk/x"},
             {"title":"IKEA BILLY reol hvid","price":300,"url":"https://www.dba.dk/recommerce/forsale/item/1"}]
            """);

        var comparables = OpenAiPriceSearch.ParseComparables(response, Query);

        Assert.Equal([300], comparables.Select(item => item.Price));
    }

    [Fact]
    public async Task Three_comparables_give_middel_confidence()
    {
        var handler = new StubHandler(File.ReadAllText("fixtures/openai-web-search-response.json"));

        var signals = await Search(handler).SearchAsync(Query, includeReshopper: false, CancellationToken.None);

        Assert.Equal([150, 500, 350], signals.Prices);
        Assert.Equal("middel", signals.Confidence);
    }

    [Fact]
    public async Task A_failed_call_degrades_instead_of_throwing()
    {
        var handler = new StubHandler("upstream exploded", HttpStatusCode.InternalServerError);

        var signals = await Search(handler).SearchAsync(Query, includeReshopper: false, CancellationToken.None);

        Assert.Empty(signals.Prices);
        Assert.Equal("lav", signals.Confidence);
        Assert.StartsWith("Net-søgningen kunne ikke gennemføres", signals.Note);
    }

    [Fact]
    public async Task Repeating_a_query_does_not_pay_for_a_second_search()
    {
        var handler = new StubHandler(File.ReadAllText("fixtures/openai-web-search-response.json"));
        var search = Search(handler);

        await search.SearchAsync(Query, includeReshopper: false, CancellationToken.None);
        await search.SearchAsync(Query, includeReshopper: false, CancellationToken.None);

        Assert.Equal(1, handler.Calls);
    }

    // A refusal or a plain-prose answer must degrade to the heuristic, never 500 the endpoint.
    [Fact]
    public void Output_that_is_not_json_yields_no_comparables()
    {
        var response = JsonDocument.Parse("""
            { "output": [ { "type": "message", "content": [
                { "type": "output_text", "text": "Beklager, jeg kunne ikke finde annoncer." } ] } ] }
            """).RootElement;

        Assert.Empty(OpenAiPriceSearch.ParseComparables(response, Query));
    }

    // A price the user cannot click through to verify is the failure mode this replaced.
    [Fact]
    public void Comparables_without_a_url_are_dropped()
    {
        var response = Message("""
            [{"title":"IKEA BILLY reol","price":250,"url":""},
             {"title":"IKEA BILLY reol","price":300,"url":"https://www.dba.dk/recommerce/forsale/item/3"}]
            """);

        var comparables = OpenAiPriceSearch.ParseComparables(response, Query);

        Assert.Equal([300], comparables.Select(item => item.Price));
    }

    [Fact]
    public void Prices_outside_the_sanity_band_are_dropped()
    {
        var response = Message("""
            [{"title":"IKEA BILLY reol","price":5,"url":"https://www.dba.dk/recommerce/forsale/item/1"},
             {"title":"IKEA BILLY reol","price":200000,"url":"https://www.dba.dk/recommerce/forsale/item/2"},
             {"title":"IKEA BILLY reol","price":300,"url":"https://www.dba.dk/recommerce/forsale/item/3"}]
            """);

        var comparables = OpenAiPriceSearch.ParseComparables(response, Query);

        Assert.Equal([300], comparables.Select(item => item.Price));
    }
}
