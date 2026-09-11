using Api.SaleAssist;

namespace Api.Tests;

public class PriceSearchClientTests
{
    const string Query = "IKEA BILLY reol brugt pris Danmark";

    static string Page() => File.ReadAllText("fixtures/duckduckgo-page.html");

    [Fact]
    public void Query_tokens_drop_stop_words_and_short_tokens()
        => Assert.Equal(["ikea", "billy", "reol"], PriceSearchClient.ComparableQueryTokens(Query));

    [Fact]
    public void Search_queries_target_danish_secondhand_platforms()
    {
        var queries = PriceSearchClient.BuildSearchQueries(Query, includeReshopper: true);

        Assert.Equal(5, queries.Count);
        Assert.Contains("IKEA BILLY reol site:dba.dk", queries);
        Assert.Contains("IKEA BILLY reol site:guloggratis.dk", queries);
        Assert.Contains("IKEA BILLY reol site:facebook.com/marketplace", queries);
        Assert.Contains("IKEA BILLY reol site:reshopper.com", queries);
    }

    [Fact]
    public void Search_queries_skip_reshopper_when_it_is_not_relevant()
    {
        var queries = PriceSearchClient.BuildSearchQueries(Query, includeReshopper: false);

        Assert.DoesNotContain(queries, query => query.Contains("reshopper", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(4, queries.Count);
    }

    [Fact]
    public void Extract_prices_understands_tusind_form()
        => Assert.Contains(2000, PriceSearchClient.ExtractPrices("Sælges for 2 tusind kr."));

    // Deliberate deviation from the Python, which read this as 250.
    [Theory]
    [InlineData("Pris 1.250 kr. og 300 kr.", new[] { 300, 1250 })]
    [InlineData("1.250 kr", new[] { 1250 })]
    [InlineData("12.500 kr", new[] { 12500 })]
    [InlineData("100.000 kr", new[] { 100000 })]
    [InlineData("999 kr", new[] { 999 })]
    [InlineData("25 kr", new[] { 25 })]
    public void Danish_thousands_separator_is_parsed_correctly(string text, int[] expected)
        => Assert.Equal(expected, PriceSearchClient.ExtractPrices(text));

    [Fact]
    public void Prices_below_the_floor_are_dropped()
        => Assert.Empty(PriceSearchClient.ExtractPrices("kun 5 kr."));

    [Fact]
    public void Comparables_are_extracted_from_a_real_result_page()
    {
        var comparables = PriceSearchClient.ExtractComparables(Page(), Query);

        Assert.NotEmpty(comparables);
        Assert.All(comparables, item =>
        {
            Assert.True(item.Price > 0);
            Assert.True(item.Relevance >= 0.35);
            Assert.NotEmpty(item.Title);
        });
    }

    [Fact]
    public void Comparables_match_what_python_extracted_from_the_same_page()
    {
        var comparables = PriceSearchClient.ExtractComparables(Page(), Query);

        Assert.Single(comparables);
        Assert.Equal(550, comparables[0].Price);
        Assert.Equal(1.0, comparables[0].Relevance);
        Assert.StartsWith("Hvad koster samling af Ikea Billy reol?", comparables[0].Title);
    }

    [Fact]
    public void Irrelevant_titles_are_filtered_out()
        => Assert.Empty(PriceSearchClient.ExtractComparables(Page(), "trampolin havemøbler brugt pris Danmark"));

    [Fact]
    public void Duplicate_comparables_are_removed_by_url()
    {
        var comparables = PriceSearchClient.DeduplicateComparables([
            new Comparable { Title = "IKEA BILLY reol", Price = 500, Relevance = 1, Url = "https://dba.dk/reol/123" },
            new Comparable { Title = "IKEA BILLY reol igen", Price = 550, Relevance = 1, Url = "https://dba.dk/reol/123/" },
            new Comparable { Title = "IKEA BILLY hjørnereol", Price = 650, Relevance = 0.8, Url = "https://dba.dk/reol/456" },
        ]);

        Assert.Equal(2, comparables.Count);
    }

    [Fact]
    public void Estimate_uses_high_relevance_prices_when_enough_exist()
    {
        var prices = PriceSearchClient.PriceCandidatesForEstimate([
            new Comparable { Title = "IKEA BILLY reol", Price = 400, Relevance = 1, Url = "https://dba.dk/1" },
            new Comparable { Title = "IKEA BILLY reol hvid", Price = 500, Relevance = 0.8, Url = "https://dba.dk/2" },
            new Comparable { Title = "IKEA BILLY reol eg", Price = 600, Relevance = 0.7, Url = "https://dba.dk/3" },
            new Comparable { Title = "Anden reol", Price = 2000, Relevance = 0.4, Url = "https://dba.dk/4" },
        ]);

        Assert.Equal([400, 500, 600], prices);
    }

    [Fact]
    public void Html_text_is_stripped_and_unescaped()
        => Assert.Equal("Reol & bord", PriceSearchClient.CleanHtmlText("<b>Reol</b> &amp; <i> bord</i>"));

    [Fact]
    public void Redirect_urls_are_decoded_to_the_target()
        => Assert.Equal(
            "https://example.dk/reol",
            PriceSearchClient.DecodeSearchResultUrl("//duckduckgo.com/l/?uddg=https%3A%2F%2Fexample.dk%2Freol"));

    [Fact]
    public void A_plain_url_is_returned_unchanged()
        => Assert.Equal("https://example.dk/reol", PriceSearchClient.DecodeSearchResultUrl("https://example.dk/reol"));
}
