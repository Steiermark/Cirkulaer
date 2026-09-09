using Api.Decision;
using Api.Producers;

namespace Api.Ai;

// Local stand-in used when no provider key is configured, so the app runs without one.
// Matches on the uploaded filename, exactly as legacy/server.py:184-284 did.
public static class TestAssessments
{
    sealed record Hint(
        string[] Keywords,
        string ObjectName,
        string Category,
        string? Subcategory,
        string? Brand,
        string? Model,
        string[] Materials,
        string WasteCategory,
        double Confidence);

    static readonly Hint[] Hints =
    [
        new(["billy"], "BILLY-reol", "Møbler og indbo", "Bogreol", "IKEA", "BILLY",
            ["spånplade", "træfiberplade"], "Storskrald eller genbrugsplads", 0.96),
        new(["reol", "stol", "bord", "ikea", "skab", "moebel", "møbel"], "Møbel",
            "Møbler og indbo", "Møbel", null, null,
            ["træ", "metal"], "Storskrald eller genbrugsplads", 0.42),
        new(["boremaskine", "drill", "bosch", "makita", "dewalt"], "Akkuboremaskine",
            "Elektronik og værktøj", "Elværktøj", null, null,
            ["plast", "metal", "batteri"], "Småt elektronik", 0.44),
        new(["telefon", "iphone", "samsung", "mobil"], "Mobiltelefon",
            "Elektronik", "Telefon", null, null,
            ["glas", "metal", "batteri"], "Småt elektronik", 0.44),
        new(["jakke", "bukser", "troeje", "trøje", "sko", "tekstil"], "Tekstil eller tøj",
            "Tekstiler", "Tøj", null, null,
            ["tekstil"], "Tekstilaffald", 0.4),
        new(["cykel", "bike", "bicycle", "mountainbike", "racercykel", "elcykel"], "Cykel",
            "Cykel og fritid", "Cykel", null, null,
            ["metal", "gummi", "plast"], "Jern og metal eller storskrald", 0.46),
    ];

    static readonly Hint Fallback = new([], "Ukendt testgenstand", "Blandet genstand",
        null, null, null, [], "Afhænger af materiale og lokal ordning", 0.25);

    public static Assessment Build(string filename)
    {
        var normalized = filename.ToLowerInvariant();
        var match = Hints.FirstOrDefault(hint => hint.Keywords.Any(normalized.Contains)) ?? Fallback;

        // The generic furniture hint infers the brand from the filename rather than carrying one.
        var brand = match.Brand;
        if (match.ObjectName == "Møbel")
            brand = normalized.Contains("ikea") || normalized.Contains("billy") ? "IKEA" : null;

        var assessment = new Assessment
        {
            ObjectName = match.ObjectName,
            Category = match.Category,
            CategoryId = TextHelpers.CanonicalCategoryId(match.Category),
            Subcategory = match.Subcategory,
            Brand = brand,
            Model = match.Model,
            Materials = match.Materials,
            VisibleDamage = [],
            ConditionEstimate = "unknown",
            Confidence = match.Confidence,
            WasteCategory = match.WasteCategory,
            UncertaintyNotes =
            [
                "Testversion: billedet er uploadet, men der er ikke brugt rigtig AI-genkendelse endnu.",
                "Genstanden er kun groft foreslået ud fra filnavn, så bekræft oplysningerne i spørgsmålene.",
            ],
            AnalysisMode = "test",
        };

        return assessment with { ProducerProgramCandidates = ProducerPrograms.FindCandidates(assessment) };
    }
}
