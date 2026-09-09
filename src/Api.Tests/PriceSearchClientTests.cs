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
    public void Extract_prices_understands_tusind_form()
        => Assert.Contains(2000, PriceSearchClient.ExtractPrices("Sælges for 2 tusind kr."));

    // Preserved bug from the original: the Danish thousands separator is mishandled,
    // so "1.250 kr." reads as 250. Verified against legacy/server.py.
    [Fact]
    public void Danish_thousands_separator_is_mishandled_as_in_the_original()
        => Assert.Equal([250, 300], PriceSearchClient.ExtractPrices("Pris 1.250 kr. og 300 kr."));

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
