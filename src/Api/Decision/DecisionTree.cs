namespace Api.Decision;

public static partial class DecisionEngine
{
    static readonly string[] CircularOrder = ["repair", "clean", "sell", "donate", "waste"];

    static readonly Dictionary<string, (string Label, string Description)> ActionText = new()
    {
        ["repair"] = ("Reparér",
            "Undersøg realistisk reparation først: typisk fejl, reservedel, "
            + "sikkerhed og forventet levetidsforlængelse."),
        ["clean"] = ("Rens/klargør",
            "For møbler kan rensning, pletbehandling eller enkel klargøring "
            + "skabe ny værdi før salg eller bortgivelse."),
        ["sell"] = ("Sælg",
            "Vælg salg, når genstanden virker eller har realistisk værdi for "
            + "en anden bruger. Producentordninger tjekkes som en del af salgsgrenen."),
        ["donate"] = ("Bortgiv",
            "Vælg bortgivelse, når genstanden stadig kan bruges, men værdien "
            + "eller salgsindsatsen er lav."),
        ["waste"] = ("Affald",
            "Sortér først som affald, når reparation, rensning, salg og "
            + "bortgivelse er vurderet som urealistiske."),
    };

    static readonly Dictionary<string, string> ReasonLabels = new()
    {
        ["defect"] = "Defekt eller virker ikke",
        ["no_need"] = "Bruger den ikke længere",
        ["replace"] = "Vil erstatte den",
        ["no_space"] = "Har ikke plads",
        ["give_away"] = "Vil give den videre",
        ["waste_assumption"] = "Mener den er affald",
        ["worn"] = "Slidt",
        ["missing_part"] = "Mangler en del",
    };

    public static Recommendation BuildRecommendation(
        Assessment assessment, IReadOnlyDictionary<string, string?> answers)
    {
        var context = BuildContext(assessment, answers);
        var possibilities = EvaluatePossibilities(context);
        var (recommendedAction, decisionPath) = ChooseAction(context, possibilities);
        var orderedActions = OrderActions(recommendedAction, possibilities);

        return new Recommendation
        {
            ObjectName = string.IsNullOrEmpty(assessment.ObjectName) ? "Ukendt genstand" : assessment.ObjectName,
            RecommendedAction = recommendedAction,
            DecisionPath = decisionPath,
            Confidence = ConfidenceLabel(context, possibilities),
            Reasoning = BuildReasoning(recommendedAction, context),
            Checks = BuildChecks(possibilities, orderedActions, recommendedAction),
            Options = orderedActions.Select(action => new ActionOption
            {
                Key = action,
                Label = ActionText[action].Label,
                Description = ActionText[action].Description,
                Realistic = possibilities[action].Realistic,
            }).ToList(),
            Impact = BuildImpact(recommendedAction),
            ProducerProgram = null,
            Waste = new WasteInfo
            {
                GeneralFraction = string.IsNullOrEmpty(assessment.WasteCategory)
                    ? "Afhænger af materiale"
                    : assessment.WasteCategory,
                Note = "Dette er generel dansk vejledning. Kommunespecifik sortering "
                     + "må først vises, når reglen kommer fra en verificeret datakilde.",
            },
        };
    }

    internal static Dictionary<string, Possibility> EvaluatePossibilities(DecisionContext context)
    {
        var works = context.Works;
        var reason = context.Reason;
        var damage = context.Damage;
        var age = context.Age;
        var accessories = context.Accessories;
        var cleaning = context.Cleaning;

        var repair = works is "no" or "partly" or "unknown"
            && damage is not ("major" or "worn_out")
            && !(context.IsTextile && damage is "major" or "worn_out");
        if (context.IsElectronics)
            repair = repair || works is "partly" or "unknown";
        if (context.IsFurniture && works == "yes")
            repair = false;

        var clean = context.IsFurniture
            && works is "yes" or "unknown"
            && damage is not ("major" or "worn_out")
            && cleaning is "light" or "deep" or "unknown";

        var sell = works == "yes" && damage is not ("major" or "worn_out") && reason != "give_away";
        if (works == "partly" && context.IsElectronics && damage != "major")
            sell = true;
        if (accessories == "complete" && age is "newer" or "mid")
            sell = sell || works is "yes" or "partly";
        if (clean)
            sell = true;

        var donate = works is "yes" or "partly" or "unknown" && damage != "major";
        if (reason == "give_away")
            donate = true;
        if (works == "no" && repair)
            donate = false;

        if (context.SafetyRisk)
            repair = clean = sell = donate = false;

        var waste = context.SafetyRisk || !(repair || clean || sell || donate);

        var ageModifier = age switch
        {
            "newer" => 8,
            "mid" => 3,
            "old" => -8,
            "unknown" => -2,
            _ => -2,
        };

        var scores = new Dictionary<string, int>
        {
            ["repair"] = 72 + (works == "partly" ? 8 : 0) + ageModifier,
            ["clean"] = 70 + (cleaning is "light" or "deep" ? 8 : 0),
            ["sell"] = 72 + ageModifier + (accessories == "complete" ? 8 : 0),
            ["donate"] = 62 + (reason == "give_away" ? 8 : 0),
            ["waste"] = context.SafetyRisk ? 95 : 70,
        };

        return new Dictionary<string, Possibility>
        {
            ["repair"] = new(repair, repair ? scores["repair"] : 0,
                "Reparation kontrolleres først, hvis fejl og stand gør det realistisk."),
            ["clean"] = new(clean, clean ? scores["clean"] : 0,
                "For møbler kan rensning eller klargøring skabe værdi før salg eller bortgivelse."),
            ["sell"] = new(sell, sell ? scores["sell"] : 0,
                "Salg vurderes, hvis genstanden virker eller har restværdi. Producentordninger kontrolleres her."),
            ["donate"] = new(donate, donate ? scores["donate"] : 0,
                "Bortgivelse vurderes, hvis andre sandsynligvis kan bruge genstanden."),
            ["waste"] = new(waste, waste ? scores["waste"] : 0,
                "Affald vælges kun, når reparation, rensning, salg og bortgivelse ikke er realistiske."),
        };
    }

    internal static (string Action, List<string> Path) ChooseAction(
        DecisionContext context, Dictionary<string, Possibility> possibilities)
    {
        var reason = context.Reason;
        var works = context.Works;

        if (context.SafetyRisk)
            return ("waste", ["Mulig sikkerhedsrisiko", "Undgå videre brug", "Sikker aflevering"]);

        if (reason is "defect" or "missing_part")
        {
            if (possibilities["repair"].Realistic)
                return ("repair", ["Defekt", "Kontrollér reparation", "Reparér"]);
            return ("waste", ["Defekt", "Reparation ikke realistisk", "Affald"]);
        }

        if (reason is "no_need" or "no_space")
            return PreferCleanSellOrDonate(possibilities,
                [ReasonLabels.GetValueOrDefault(reason!, "Behov")]);

        if (reason == "replace")
        {
            if (works == "yes")
                return PreferCleanSellOrDonate(possibilities, ["Vil erstatte", "Virker"]);
            if (possibilities["repair"].Realistic)
                return ("repair", ["Vil erstatte", "Virker ikke", "Kontrollér reparation"]);
            return FallbackAfterRepair(possibilities, ["Vil erstatte", "Reparation ikke realistisk"]);
        }

        if (reason == "give_away")
        {
            if (possibilities["clean"].Realistic)
                return ("clean", ["Vil give videre", "Rens/klargør", "Bortgiv"]);
            if (possibilities["donate"].Realistic)
                return ("donate", ["Vil give videre", "Bortgiv"]);
            return FallbackAfterRepair(possibilities, ["Vil give videre", "Bortgiv ikke realistisk"]);
        }

        if (reason == "waste_assumption")
        {
            if (possibilities["repair"].Realistic)
                return ("repair", ["Mener den er affald", "Kontrollér reparation", "Reparér"]);
            if (possibilities["clean"].Realistic)
                return ("clean", ["Mener den er affald", "Kontrollér rensning", "Rens/klargør"]);
            if (possibilities["sell"].Realistic)
                return ("sell", ["Mener den er affald", "Kontrollér salg", "Sælg"]);
            if (possibilities["donate"].Realistic)
                return ("donate", ["Mener den er affald", "Kontrollér bortgivelse", "Bortgiv"]);
            return ("waste", ["Mener den er affald", "Alle cirkulære muligheder er nej", "Affald"]);
        }

        if (works == "yes")
            return PreferCleanSellOrDonate(possibilities, ["Virker"]);
        if (possibilities["repair"].Realistic)
            return ("repair", ["Uklar årsag", "Kontrollér reparation"]);
        return FallbackAfterRepair(possibilities, ["Uklar årsag"]);
    }

    static (string Action, List<string> Path) PreferCleanSellOrDonate(
        Dictionary<string, Possibility> possibilities, List<string> path)
    {
        if (possibilities["clean"].Realistic)
            return ("clean", [.. path, "Rens/klargør", "Vurder salg"]);
        if (possibilities["sell"].Realistic)
            return ("sell", [.. path, "Vurder salg", "Tjek producentordninger", "Sælg"]);
        if (possibilities["donate"].Realistic)
            return ("donate", [.. path, "Salg lavt", "Bortgiv"]);
        return FallbackAfterRepair(possibilities, [.. path, "Salg/bortgiv ikke oplagt"]);
    }

    static (string Action, List<string> Path) FallbackAfterRepair(
        Dictionary<string, Possibility> possibilities, List<string> path)
    {
        foreach (var action in new[] { "clean", "sell", "donate", "waste" })
        {
            if (!possibilities[action].Realistic)
                continue;
            var label = ActionText[action].Label;
            if (action == "sell")
                return (action, [.. path, "Tjek producentordninger", label]);
            return (action, [.. path, label]);
        }
        return ("waste", [.. path, "Affald"]);
    }

    internal static List<string> OrderActions(
        string recommendedAction, Dictionary<string, Possibility> possibilities)
    {
        if (recommendedAction == "waste")
            return ["waste", "donate", "sell"];

        var realistic = CircularOrder
            .Where(action => possibilities[action].Realistic && action != recommendedAction)
            .OrderByDescending(action => possibilities[action].Score)
            .ThenBy(action => Array.IndexOf(CircularOrder, action))
            .ToList();
        var notRealistic = CircularOrder.Where(action => !possibilities[action].Realistic).ToList();

        return [recommendedAction, .. realistic, .. notRealistic];
    }

    static string BuildReasoning(string action, DecisionContext context)
    {
        if (context.SafetyRisk)
            return "Svarene tyder på en mulig sikkerhedsrisiko. Genstanden bør ikke sælges, "
                 + "bortgives eller forsøges repareret uden faglig vurdering; vælg sikker aflevering.";

        var reason = context.Reason is not null && ReasonLabels.TryGetValue(context.Reason, out var label)
            ? label
            : "Svarene";

        return action switch
        {
            "repair" => $"{reason} peger på, at reparation skal kontrolleres først. "
                + "Genstanden behandles derfor som en ressource, indtil reparation viser sig urealistisk.",
            "clean" => $"{reason} og møbelkategorien peger på, at rensning eller klargøring "
                + "kan skabe værdi før salg eller bortgivelse. Det er derfor næste bedste cirkulære handling.",
            "sell" => $"{reason} og svarene tyder på, at genstanden stadig kan have "
                + "brugsværdi og økonomisk værdi for en anden. Hvis producenten har en relevant ordning, vises den som en salgsmulighed.",
            "donate" => $"{reason} gør bortgivelse til den mest direkte cirkulære vej, "
                + "fordi genstanden sandsynligvis kan bruges videre.",
            _ => "Affald anbefales, når genstanden er defekt og reparation ikke er realistisk, eller når reparation, rensning, salg og bortgivelse ikke er realistiske. "
                + "Lokale sorteringsregler skal stadig verificeres.",
        };
    }

    static List<ActionCheck> BuildChecks(
        Dictionary<string, Possibility> possibilities,
        List<string> orderedActions,
        string recommendedAction) =>
        orderedActions.Select(action => new ActionCheck
        {
            Label = ActionText[action].Label,
            Status = action == recommendedAction || possibilities[action].Realistic
                ? "realistisk"
                : "ikke realistisk",
            Description = possibilities[action].Why,
        }).ToList();

    static Impact BuildImpact(string action) => new()
    {
        Economy = action switch
        {
            "repair" => "mulig udgift",
            "clean" => "lav udgift / højere værdi",
            "sell" => "mulig indtægt",
            "donate" => "0 kr.",
            _ => "0 kr.",
        },
        Co2Saving = "Ikke beregnet",
        Note = "CO2-effekten vises først, når produkt-, materiale- og levetidsdata kan dokumenteres.",
    };

    static string ConfidenceLabel(DecisionContext context, Dictionary<string, Possibility> possibilities)
    {
        string?[] required =
            [context.Reason, context.Works, context.Damage, context.Age, context.Accessories, context.Safety];

        if (required.Any(value => value is null or "" or "unknown"))
            return "Lav";

        if (context.IdentificationConfidence is { } confidence)
        {
            if (confidence < 0.5)
                return "Lav";
            if (confidence < 0.75)
                return "Middel";
        }

        return possibilities.Values.Count(item => item.Realistic) > 2 ? "Middel" : "Høj";
    }
}
