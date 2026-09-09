using Api.Decision;

namespace Api.SaleAssist;

public sealed record PriceEstimate(string Label, string Note);

public static class PriceEstimator
{
    static readonly string[] PremiumProducers =
        ["trek", "specialized", "cannondale", "pinarello", "cervelo", "bmc", "canyon"];

    static readonly string[] PremiumTerms =
        ["madone", "slr", "etap", "axs", "di2", "dura ace", "ultegra", "carbon", "racercykel"];

    public static PriceEstimate Estimate(
        Assessment assessment, IReadOnlyDictionary<string, string?> answers, IReadOnlyList<int> prices)
    {
        var text = string.Join(" ", new[]
        {
            (assessment.Category ?? "").ToLowerInvariant(),
            (assessment.ObjectName ?? "").ToLowerInvariant(),
            (assessment.Subcategory ?? "").ToLowerInvariant(),
            (Get(answers, "producer_name") ?? "").ToLowerInvariant(),
            (Get(answers, "model_name") ?? "").ToLowerInvariant(),
        });

        if (text.Contains("cykel") || text.Contains("bike"))
            return EstimateBicyclePrice(answers, prices);

        var category = (assessment.Category ?? "").ToLowerInvariant();
        var filtered = TrimPriceOutliers(prices, 50, 100000);

        if (filtered.Count > 0)
        {
            var midpoint = filtered[filtered.Count / 2]
                * AgePriceFactor(Get(answers, "age"))
                * DamagePriceFactor(answers);
            var low = RoundToNearest25(midpoint * 0.8);
            var high = RoundToNearest25(midpoint * 1.15);
            var quick = RoundToNearest25(midpoint * 0.7);

            return new PriceEstimate(
                $"Sæt prisen til {RoundToNearest50(midpoint)} kr.",
                $"Pris sat ud fra medianen af lignende webfund. Realistisk spænd: {low}-{high} kr. Hurtigt salg kan fx ligge omkring {quick} kr. Kontrollér aktive annoncer og stand før publicering.");
        }

        var (baseLow, baseHigh) = category.Contains("møbel") || category.Contains("møbler")
            ? (200, 900)
            : category.Contains("elektronik") ? (150, 700) : (100, 500);

        double lowValue = baseLow;
        double highValue = baseHigh;

        if (Get(answers, "damage") == "minor")
        {
            lowValue = RoundToNearest25(lowValue * 0.75);
            highValue = RoundToNearest25(highValue * 0.75);
        }
        if (Get(answers, "damage") == "major" || Get(answers, "works") == "partly")
        {
            lowValue = RoundToNearest25(lowValue * 0.5);
            highValue = RoundToNearest25(highValue * 0.55);
        }

        var ageFactor = AgePriceFactor(Get(answers, "age"));
        var finalLow = RoundToNearest25(lowValue * ageFactor);
        var finalHigh = RoundToNearest25(highValue * ageFactor);

        return new PriceEstimate(
            $"Sæt prisen til {RoundToNearest50((finalLow + finalHigh) / 2.0)} kr.",
            $"Foreløbigt prototypeestimat, fordi der ikke blev fundet nok tydelige priser online. Realistisk spænd: {finalLow}-{finalHigh} kr.");
    }

    public static PriceEstimate EstimateBicyclePrice(
        IReadOnlyDictionary<string, string?> answers, IReadOnlyList<int> prices)
    {
        if (IsPremiumBicycle(answers))
            return EstimatePremiumBicyclePrice(answers, prices);

        var filtered = TrimPriceOutliers(prices, 450, 15000);
        var working = Get(answers, "works") == "yes";
        var minorOrBetter = Get(answers, "damage") is "no" or "minor" or null or "unknown";
        var complete = Get(answers, "accessories") is "complete" or "irrelevant" or null;
        var knownModel = !string.IsNullOrWhiteSpace(SaleQueryBuilder.ProducerSearchName(answers))
            || !string.IsNullOrWhiteSpace(Get(answers, "model_name"));

        if (filtered.Count > 0)
        {
            var midpoint = filtered[filtered.Count / 2]
                * AgePriceFactor(Get(answers, "age"))
                * DamagePriceFactor(answers);
            var (lowFactor, highFactor, quickFactor) = (0.82, 1.28, 0.72);

            if (working && minorOrBetter && complete)
            {
                midpoint = Math.Max(midpoint, knownModel ? 1900 : 1500);
                (lowFactor, highFactor, quickFactor) = (0.8, 1.35, 0.68);
            }

            return new PriceEstimate(
                $"Sæt prisen til {RoundToNearest50(midpoint)} kr.",
                $"Pris sat ud fra medianen af lignende cykelpriser og justeret efter stand, komplethed og modeloplysninger. Realistisk spænd: {RoundToNearest50(midpoint * lowFactor)}-{RoundToNearest50(midpoint * highFactor)} kr. Hurtigt salg kan fx ligge omkring {RoundToNearest50(midpoint * quickFactor)} kr.; meget slidte cykler kan ligge lavere.");
        }

        var (low, high, quick) = working && minorOrBetter && complete
            ? knownModel ? (1500, 3000, 1200) : (1200, 2400, 900)
            : Get(answers, "works") == "partly" || Get(answers, "damage") == "major"
                ? (400, 1100, 300)
                : (800, 1800, 600);

        var ageFactor = AgePriceFactor(Get(answers, "age"));
        var finalLow = RoundToNearest50(low * ageFactor);
        var finalHigh = RoundToNearest50(high * ageFactor);
        var finalQuick = RoundToNearest50(quick * ageFactor);

        return new PriceEstimate(
            $"Sæt prisen til {RoundToNearest50((finalLow + finalHigh) / 2.0)} kr.",
            $"Cykelestimat baseret på kategori, stand og om producent/model er kendt. Realistisk spænd: {finalLow}-{finalHigh} kr. Hurtigt salg kan fx ligge omkring {finalQuick} kr.");
    }

    public static bool IsPremiumBicycle(IReadOnlyDictionary<string, string?> answers)
    {
        var text = string.Join(" ", new[]
        {
            (Get(answers, "producer") ?? "").ToLowerInvariant(),
            (Get(answers, "producer_name") ?? "").ToLowerInvariant(),
            (Get(answers, "model_name") ?? "").ToLowerInvariant(),
        }).Replace("mardone", "madone").Replace("etep", "etap");

        return PremiumProducers.Any(text.Contains) && PremiumTerms.Any(text.Contains);
    }

    public static PriceEstimate EstimatePremiumBicyclePrice(
        IReadOnlyDictionary<string, string?> answers, IReadOnlyList<int> prices)
    {
        var filtered = TrimPriceOutliers(prices, 12000, 80000);
        var working = Get(answers, "works") == "yes";
        var majorIssue = Get(answers, "damage") == "major" || Get(answers, "works") == "partly";

        if (filtered.Count > 0)
        {
            var midpoint = filtered[filtered.Count / 2]
                * AgePriceFactor(Get(answers, "age"))
                * DamagePriceFactor(answers);
            var low = RoundToNearest500(midpoint * (majorIssue ? 0.75 : 0.85));
            var high = RoundToNearest500(midpoint * (majorIssue ? 1.1 : 1.25));
            var quick = RoundToNearest500(midpoint * (majorIssue ? 0.65 : 0.75));

            return new PriceEstimate(
                $"Sæt prisen til {RoundToNearest500(midpoint)} kr.",
                $"Pris sat ud fra medianen af lignende premium-racercykler fundet online. Realistisk spænd: {low}-{high} kr. Hurtigt salg kan fx ligge omkring {quick} kr.; årgang, størrelse, hjul, geargruppe og dokumentation betyder meget.");
        }

        var (low2, high2, quick2) = majorIssue
            ? (12000, 22000, 10000)
            : working ? (24000, 45000, 22000) : (18000, 35000, 16000);

        var ageFactor = AgePriceFactor(Get(answers, "age"));
        var finalLow = RoundToNearest500(low2 * ageFactor);
        var finalHigh = RoundToNearest500(high2 * ageFactor);
        var finalQuick = RoundToNearest500(quick2 * ageFactor);

        return new PriceEstimate(
            $"Sæt prisen til {RoundToNearest500((finalLow + finalHigh) / 2.0)} kr.",
            $"Premium-racercykelestimat baseret på producent/model, fordi der ikke blev fundet nok brugbare webpriser. Realistisk spænd: {finalLow}-{finalHigh} kr. Kontrollér især årgang, stelstørrelse, hjulsæt, SRAM/Shimano-gruppe og stand. Hurtigt salg kan fx ligge omkring {finalQuick} kr.");
    }

    public static double AgePriceFactor(string? age) => age switch
    {
        "newer" => 1.05,
        "mid" => 1.0,
        "old" => 0.75,
        "unknown" => 0.9,
        _ => 1.0,
    };

    public static double DamagePriceFactor(IReadOnlyDictionary<string, string?> answers)
    {
        if (Get(answers, "damage") == "major" || Get(answers, "works") == "partly")
            return 0.55;
        if (Get(answers, "damage") == "minor")
            return 0.82;
        return 1.0;
    }

    public static List<int> TrimPriceOutliers(IReadOnlyList<int> prices, int minimum, int maximum)
    {
        var filtered = prices.Where(price => price >= minimum && price <= maximum).Order().ToList();
        return filtered.Count >= 5 ? filtered[1..^1] : filtered;
    }

    // Python's round() is banker's rounding, and Math.Round's default matches it.
    public static int RoundToNearest25(double value) => (int)(Math.Round(value / 25) * 25);

    public static int RoundToNearest50(double value) => (int)(Math.Round(value / 50) * 50);

    public static int RoundToNearest500(double value) => (int)(Math.Round(value / 500) * 500);

    static string? Get(IReadOnlyDictionary<string, string?> answers, string key) =>
        answers.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value) ? value : null;
}
