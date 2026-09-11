using Api.Decision;
using Api.SaleAssist;

namespace Api.Tests;

public class SaleAssistLookalikeTests
{
    static Comparable Ad(int id) => new()
    {
        Title = $"Lampe {id}",
        Price = 100 * id,
        Relevance = 0.5,
        Url = $"https://www.dba.dk/recommerce/forsale/item/{id}",
        Image = "https://images.dbastatic.dk/x",
    };

    sealed class FixedSearch(params Comparable[] comparables) : IPriceSearch
    {
        public Task<PriceSignals> SearchAsync(string query, bool includeReshopper, CancellationToken ct) =>
            Task.FromResult(PriceSignals.FromComparables(query, "https://www.google.com/search?q=stub", includeReshopper, comparables));
    }

    sealed class KeepEven : ILookalikeFilter
    {
        public int Calls { get; private set; }

        public Task<Lookalikes> KeepLookalikesAsync(string objectName, string photoDataUrl, IReadOnlyList<Comparable> comparables, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new Lookalikes(comparables.Where(item => item.Price % 200 == 0).ToList(), "similar"));
        }
    }

    static readonly Assessment Lamp = new() { ObjectName = "Lampe", Category = "Møbler" };
    static readonly Dictionary<string, string?> Answers = new() { ["works"] = "yes", ["damage"] = "no" };

    [Fact]
    public async Task With_a_photo_the_comparables_and_price_follow_the_lookalikes()
    {
        var filter = new KeepEven();
        var builder = new SaleAssistBuilder(new FixedSearch(Ad(1), Ad(2), Ad(3), Ad(4), Ad(5)), filter);

        var sale = await builder.BuildAsync(Lamp, Answers, DecisionEngine.BuildRecommendation(Lamp, Answers), "data:image/jpeg;base64,AAAA", CancellationToken.None);

        Assert.Equal(1, filter.Calls);
        Assert.Equal([200, 400], sale.Comparables.Select(item => item.Price));
        Assert.Equal("lav", sale.PriceConfidence);
        Assert.Contains("Ingen annoncer med præcis samme model; prisen bygger på 2 lignende annoncer.", sale.SearchNote);
        Assert.Equal("Sæt prisen til 400 kr.", sale.Price);
    }

    [Fact]
    public async Task Without_a_photo_nothing_is_filtered()
    {
        var filter = new KeepEven();
        var builder = new SaleAssistBuilder(new FixedSearch(Ad(1), Ad(2), Ad(3)), filter);

        var sale = await builder.BuildAsync(Lamp, Answers, DecisionEngine.BuildRecommendation(Lamp, Answers), null, CancellationToken.None);

        Assert.Equal(0, filter.Calls);
        Assert.Equal(3, sale.Comparables.Count);
    }

    [Fact]
    public async Task No_comparables_means_nothing_to_grade()
    {
        var filter = new KeepEven();
        var builder = new SaleAssistBuilder(new FixedSearch(), filter);

        await builder.BuildAsync(Lamp, Answers, DecisionEngine.BuildRecommendation(Lamp, Answers), "data:image/jpeg;base64,AAAA", CancellationToken.None);

        Assert.Equal(0, filter.Calls);
    }

    [Fact]
    public async Task A_known_model_is_searched_without_the_object_name()
    {
        var search = new RecordingSearch();
        var piano = new Assessment { ObjectName = "Digitalpiano med stativ", Category = "Elektronik", Brand = "Roland", Model = "FP-30X" };

        var sale = await new SaleAssistBuilder(search, new KeepEven())
            .BuildAsync(piano, Answers, DecisionEngine.BuildRecommendation(piano, Answers), null, CancellationToken.None);

        Assert.Equal("Roland FP-30X", search.Query);
        Assert.Contains("Roland%20FP-30X%20Digitalpiano%20med%20stativ", sale.MarketplaceSearchUrl);
    }

    [Fact]
    public async Task Without_a_model_the_first_search_term_is_used()
    {
        var search = new RecordingSearch();
        var lamp = new Assessment { ObjectName = "Pendellampe", Category = "Møbler", Subcategory = "Lampe", SearchTerms = ["PH-lampe kobber", "pendel lagdelt"] };

        await new SaleAssistBuilder(search, new KeepEven())
            .BuildAsync(lamp, Answers, DecisionEngine.BuildRecommendation(lamp, Answers), null, CancellationToken.None);

        Assert.Equal("PH-lampe kobber", search.Query);
    }

    [Fact]
    public async Task Without_a_model_or_search_terms_the_sale_query_is_used()
    {
        var search = new RecordingSearch();

        await new SaleAssistBuilder(search, new KeepEven())
            .BuildAsync(Lamp, Answers, DecisionEngine.BuildRecommendation(Lamp, Answers), null, CancellationToken.None);

        Assert.Equal("Lampe brugt pris Danmark", search.Query);
    }

    sealed class RecordingSearch : IPriceSearch
    {
        public string? Query { get; private set; }

        public Task<PriceSignals> SearchAsync(string query, bool includeReshopper, CancellationToken ct)
        {
            Query = query;
            return Task.FromResult(PriceSignals.FromComparables(query, "https://www.google.com/search?q=stub", includeReshopper, []));
        }
    }
}
