using Api.Decision;

namespace Api.SaleAssist;

public static class AdTextBuilder
{
    public static string BuildAdText(
        string objectName,
        Assessment assessment,
        IReadOnlyDictionary<string, string?> answers,
        PriceEstimate estimate)
    {
        var details = SaleQueryBuilder.BuildSaleDetails(assessment, answers);
        var featureLines = BuildFeatureLines(assessment, answers);
        var conditionLines = BuildConditionLines(answers);
        var salesPoints = BuildSalesPoints(assessment, answers);

        var lines = new List<string>
        {
            BuildAdTitle(objectName, answers),
            "",
            $"Pris: {estimate.Label}",
            "",
            $"Jeg sælger {objectName}. Den er vurderet i appen ud fra billeder, producent/model og de oplysninger, der er indtastet.",
        };

        if (salesPoints.Count > 0)
        {
            lines.AddRange(["", "Kort fortalt:"]);
            lines.AddRange(salesPoints.Select(point => $"- {point}"));
        }

        if (featureLines.Count > 0)
        {
            lines.AddRange(["", "Oplysninger:"]);
            lines.AddRange(featureLines.Select(line => $"- {line}"));
        }

        if (details.Length > 0 || conditionLines.Count > 0)
        {
            lines.AddRange(["", "Stand:"]);
            if (details.Length > 0)
                lines.Add($"- {details}.");
            lines.AddRange(conditionLines.Select(line => $"- {line}"));
        }

        lines.AddRange(
        [
            "",
            "Prisforslaget er sat ud fra lignende annoncer og bør sammenholdes med aktuel stand, alder, kvittering, servicehistorik og markedet lige nu.",
            "",
            "Kan afhentes efter aftale. Skriv gerne ved spørgsmål, hvis du vil se flere billeder, eller hvis du ønsker at aftale besigtigelse.",
        ]);

        return string.Join("\n", lines);
    }

    public static string BuildAdTitle(string objectName, IReadOnlyDictionary<string, string?> answers) =>
        PriceEstimator.IsPremiumBicycle(answers)
            ? $"{objectName} - high-end racercykel sælges"
            : $"{objectName} sælges";

    public static List<string> BuildFeatureLines(
        Assessment assessment, IReadOnlyDictionary<string, string?> answers)
    {
        var lines = new List<string>();
        var producer = SaleQueryBuilder.ProducerSearchName(answers) is { Length: > 0 } name
            ? name
            : assessment.Brand;
        var model = Get(answers, "model_name") ?? assessment.Model;

        if (!string.IsNullOrEmpty(producer))
            lines.Add($"Producent/mærke: {SaleQueryBuilder.NormalizeSearchTerms(producer)}");
        if (!string.IsNullOrEmpty(model))
            lines.Add($"Model/serie: {SaleQueryBuilder.NormalizeSearchTerms(model)}");
        if (!string.IsNullOrEmpty(assessment.Category))
            lines.Add($"Kategori: {assessment.Category}");
        if (assessment.Materials.Count > 0)
            lines.Add($"Synlige/materialemæssige oplysninger: {string.Join(", ", assessment.Materials)}");

        if (!PriceEstimator.IsPremiumBicycle(answers))
            return lines;

        var text = string.Join(" ", new[]
        {
            (producer ?? "").ToLowerInvariant(),
            (model ?? "").ToLowerInvariant(),
            (Get(answers, "producer_name") ?? "").ToLowerInvariant(),
        }).Replace("mardone", "madone").Replace("etep", "etap");

        if (text.Contains("madone"))
            lines.Add("Modeltype: Trek Madone aero-racercykel");
        if (text.Contains("slr"))
            lines.Add("SLR-serien indikerer Treks lette carbon-topplatform");
        if (text.Contains("etap") || text.Contains("axs"))
            lines.Add("Geargruppe: elektronisk SRAM eTap/AXS skal kontrolleres og nævnes i annoncen");
        lines.Add("Angiv gerne årgang, stelstørrelse, hjulsæt, servicehistorik og kvittering for at styrke annoncen");

        return lines;
    }

    public static List<string> BuildConditionLines(IReadOnlyDictionary<string, string?> answers)
    {
        var lines = new List<string>();

        switch (Get(answers, "accessories"))
        {
            case "complete": lines.Add("Tilbehør: komplet ifølge sælgers oplysninger."); break;
            case "partial": lines.Add("Tilbehør: noget følger med; skriv præcist hvad der er inkluderet."); break;
            case "missing": lines.Add("Tilbehør: noget mangler; nævn manglerne tydeligt."); break;
        }

        switch (Get(answers, "damage"))
        {
            case "minor": lines.Add("Der er mindre brugsspor/slitage; tag gerne nærbilleder af de steder, køber bør se."); break;
            case "major": lines.Add("Der er større fejl eller skader; beskriv dem tydeligt og justér prisen derefter."); break;
            case "no": lines.Add("Ingen kendte skader oplyst."); break;
        }

        return lines;
    }

    public static List<string> BuildSalesPoints(
        Assessment assessment, IReadOnlyDictionary<string, string?> answers)
    {
        if (PriceEstimator.IsPremiumBicycle(answers))
        {
            return
            [
                "Relevant for købere, der søger en let og hurtig landevejs-/aero-racercykel",
                "Producent og model er vigtige for prisen og bør stå tydeligt i titel og første linje",
                "Prisniveau afhænger især af årgang, stelstørrelse, hjul, geargruppe, stand og dokumentation",
            ];
        }

        if ((assessment.Category ?? "").ToLowerInvariant().Contains("cykel"))
        {
            return
            [
                "God til købere, der leder efter en brugbar cykel frem for et reparationsprojekt",
                "Nævn størrelse, gear, bremser og eventuelt service, hvis du kender det",
            ];
        }

        return [];
    }

    static string? Get(IReadOnlyDictionary<string, string?> answers, string key) =>
        answers.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value) ? value : null;
}
