using System.Text.Json;
using Api.Decision;

namespace Api.Producers;

public static class ProducerPrograms
{
    static readonly IReadOnlyList<ProducerProgram> Programs = Load();

    static IReadOnlyList<ProducerProgram> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "data", "producer-programs.json");
        return JsonSerializer.Deserialize<List<ProducerProgram>>(File.ReadAllText(path))!;
    }

    public static IReadOnlyList<string> FindCandidates(Assessment assessment)
    {
        var producer = TextHelpers.Normalize(assessment.Brand ?? "");
        var text = TextHelpers.Normalize(string.Join(" ", new[]
        {
            assessment.ObjectName ?? "",
            assessment.Category ?? "",
            assessment.Subcategory ?? "",
        }));

        return Programs
            .Where(program => producer == program.Producer)
            .Where(program => program.EligibleHints.Any(text.Contains)
                           && !program.ExcludedHints.Any(text.Contains))
            .Select(program => program.Id)
            .ToList();
    }

    static IReadOnlyList<ProducerProgram> FindForContext(DecisionContext context)
    {
        var producer = context.Producer;
        if (string.IsNullOrEmpty(producer) || producer is "unknown" or "ved ikke" or "no" or "anden")
            return [];

        return Programs.Where(program => program.Producer == producer).ToList();
    }

    public static ProducerProgramResult Evaluate(DecisionContext context)
    {
        var programs = FindForContext(context);
        if (programs.Count == 0)
        {
            return new ProducerProgramResult
            {
                Status = "none",
                Title = "Producentordninger",
                Message = "Der er ikke fundet en konkret producentordning endnu. "
                        + "I næste fase kan modulet slå op i en database med reparation, "
                        + "reservedele, buy-back, trade-in, take-back og refurbishment.",
                Programs = [],
            };
        }

        var evaluated = programs.Select(program => EvaluateSingle(program, context)).ToList();
        var best = evaluated.OrderByDescending(item => item.Rank).First();

        return new ProducerProgramResult
        {
            Status = best.Status,
            Title = "Producentordning fundet",
            Message = best.Message,
            Programs = evaluated,
        };
    }

    static ProgramEvaluation EvaluateSingle(ProducerProgram program, DecisionContext context)
    {
        var text = string.Join(" ", new[] { context.ObjectName, context.Category, context.Subcategory });
        var hasEligibleHint = program.EligibleHints.Any(text.Contains);
        var hasExcludedHint = program.ExcludedHints.Any(text.Contains);
        var goodCondition = context.Works == "yes" && context.Damage is "no" or "minor";
        var complete = context.Accessories is "complete" or "irrelevant" or null;

        var original = TriState(context.OriginalProduct);
        var clean = TriState(context.CleanState);
        var unmodified = TriState(context.Unmodified);
        var assembled = TriState(context.Assembled);
        bool?[] eligibilityChecks = [original, clean, unmodified, assembled];

        var likely = hasEligibleHint
            && !hasExcludedHint
            && goodCondition
            && complete
            && eligibilityChecks.All(value => value is true);

        var possible = !hasExcludedHint
            && context.Works is "yes" or "unknown"
            && !eligibilityChecks.Any(value => value is false);

        var (status, rank, message) = likely
            ? ("likely", 3, $"{program.Title} ser ud til potentielt at være relevant. "
                + "Hent en officiel vurdering og sammenlign med almindeligt privat salg.")
            : possible
                ? ("possible", 2, $"{program.Title} kan muligvis være relevant, men stand, kategori, "
                    + "original mærkning og komplethed skal bekræftes hos producenten.")
                : ("unlikely", 1, $"{program.Title} ligner ikke en oplagt vej ud fra svarene. "
                    + "Fortsæt med almindelig salgs- eller bortgivelsesvurdering.");

        return new ProgramEvaluation
        {
            Id = program.Id,
            Title = program.Title,
            Producer = program.Producer.ToUpperInvariant(),
            SchemeTypes = program.SchemeTypes,
            Status = status,
            Rank = rank,
            Message = message,
            Url = program.Url,
            Reward = program.Reward,
            SourceLabel = program.SourceLabel,
            Checks =
            [
                new() { Label = "Originalt producentprodukt", Ok = original },
                new() { Label = "God/salgbar stand", Ok = goodCondition },
                new() { Label = "Rent", Ok = clean },
                new() { Label = "Komplet", Ok = complete },
                new() { Label = "Uændret", Ok = unmodified },
                new() { Label = "Korrekt samlet", Ok = assembled },
                new() { Label = "Mulig omfattet kategori", Ok = hasEligibleHint && !hasExcludedHint },
            ],
            Comparison =
            [
                new() { Label = program.Title, Value = program.Reward },
                new() { Label = "Privat salg", Value = "ofte højere pris, mere arbejde" },
                new() { Label = "Hurtigt salg", Value = "lavere pris, hurtigere afhændelse" },
            ],
        };
    }

    static bool? TriState(string? value) => value switch
    {
        "yes" => true,
        "no" => false,
        _ => null,
    };
}
