using Api.Decision;

namespace Api.Ai;

public interface IVisionProvider
{
    string Name { get; }

    Task<Assessment> AnalyzeAsync(IReadOnlyList<string> imageDataUrls, CancellationToken ct);
}

public static class VisionPrompt
{
    // Copied verbatim from legacy/server.py:286-295 plus the design-classic sentence,
    // added 2026-09-11 so a recognisable PH-lampe or Y-stol gets a model and a usable
    // price query. Product copy — do not reword.
    public const string Text =
        "Du analyserer 1-4 fotos af den samme fysiske genstand for en dansk cirkulær "
        + "økonomi-assistent. Brug alle vinkler samlet. Hvis et foto viser en mærkeplade, "
        + "etiket eller original mærkning, skal du bruge den til at identificere producent, mærke og model. "
        + "Kendte designklassikere og udbredte serier må identificeres på deres form og detaljer, hvis du er sikker. "
        + "Gæt ikke på mærke eller model, hvis det ikke tydeligt fremgår. "
        + "Kommunale affaldsregler må ikke opfindes. Brug kun en generel dansk "
        + "affaldsfraktion, og skriv usikkerheder eksplicit. "
        + "Vælg category_id fra den faste liste i skemaet. "
        + "Skriv alle tekstfelter på dansk.";
}
