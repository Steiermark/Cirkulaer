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

        public Task<IReadOnlyList<Comparable>> KeepLookalikesAsync(string photoDataUrl, IReadOnlyList<Comparable> comparables, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<Comparable>>(comparables.Where(item => item.Price % 200 == 0).ToList());
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
        Assert.Contains("2 prisfund", sale.SearchNote);
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

    // One or two rows are not "too many"; grading them costs a call and gains nothing.
    [Fact]
    public async Task Fewer_than_three_comparables_are_not_graded()
    {
        var filter = new KeepEven();
        var builder = new SaleAssistBuilder(new FixedSearch(Ad(1), Ad(2)), filter);

        await builder.BuildAsync(Lamp, Answers, DecisionEngine.BuildRecommendation(Lamp, Answers), "data:image/jpeg;base64,AAAA", CancellationToken.None);

        Assert.Equal(0, filter.Calls);
    }
}
