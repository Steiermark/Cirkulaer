using System.Net;
using Api.SaleAssist;
using Microsoft.Extensions.Logging.Abstractions;

namespace Api.Tests;

public class DbaPriceSearchTests
{
    const string Query = "Roland FP-30X Digitalklaver Klaver brugt pris Danmark";

    sealed class StubHandler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public Uri? Requested { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requested = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    static DbaPriceSearch Search(HttpMessageHandler handler) =>
        new(new HttpClient(handler), NullLogger<DbaPriceSearch>.Instance);

    static string Page() => File.ReadAllText("fixtures/dba-search-page.html");

    // dba's search pads thin results with near matches; the FP-30 is a different model.
    [Fact]
    public void Comparables_are_read_from_the_structured_data_and_other_models_are_dropped()
    {
        var comparables = DbaPriceSearch.ParseComparables(Page(), Query);

        var only = Assert.Single(comparables);
        Assert.Equal("Roland FP-30X digitalpiano sort", only.Title);
        Assert.Equal(4500, only.Price);
        Assert.Equal("https://www.dba.dk/recommerce/forsale/item/24791070", only.Url);
        Assert.Equal("https://images.dbastatic.dk/dynamic/default/item/24791070/c497b5cf-ff6b-4fcb-b45f-4a31c1239e9e", only.Image);
    }

    [Fact]
    public void Each_ad_appears_once()
    {
        var comparables = DbaPriceSearch.ParseComparables(Page(), "Roland brugt pris Danmark");

        Assert.Equal(3, comparables.Count);
        Assert.Equal(3, comparables.Select(item => item.Url).Distinct().Count());
    }

    [Fact]
    public void A_page_without_structured_data_gives_no_comparables()
    {
        Assert.Empty(DbaPriceSearch.ParseComparables("<html><body>nothing</body></html>", Query));
    }

    [Fact]
    public async Task The_query_is_sent_to_the_dba_search_page()
    {
        var handler = new StubHandler(Page());

        await Search(handler).SearchAsync(Query, includeReshopper: false, CancellationToken.None);

        Assert.Equal("https://www.dba.dk/recommerce/forsale/search?q=Roland+FP-30X+Digitalklaver+Klaver+brugt+pris+Danmark", handler.Requested!.ToString());
    }

    [Fact]
    public async Task One_comparable_gives_lav_confidence_and_a_note_with_the_count()
    {
        var signals = await Search(new StubHandler(Page())).SearchAsync(Query, includeReshopper: false, CancellationToken.None);

        Assert.Equal([4500], signals.Prices);
        Assert.Equal("lav", signals.Confidence);
        Assert.Contains("1 prisfund", signals.Note);
    }

    [Fact]
    public async Task A_failed_request_falls_back_to_the_manual_search_link()
    {
        var signals = await Search(new StubHandler("", HttpStatusCode.ServiceUnavailable))
            .SearchAsync(Query, includeReshopper: false, CancellationToken.None);

        Assert.Empty(signals.Prices);
        Assert.Equal("lav", signals.Confidence);
        Assert.StartsWith("https://www.google.com/search?q=", signals.Url);
    }
}
