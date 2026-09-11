namespace Api.SaleAssist;

public static class PriceSearchPrompt
{
    // Do not "tighten" this prompt. Demanding direct links and forbidding repeated page
    // opens made the model satisfy the format instead of doing the work: it invented
    // plausible dba.dk item ids that all 404. Measured 2026-09-11. The looser wording below
    // is slower and returns fewer rows, but the links it returns resolve.
    public static string For(string query) =>
        $"""
        Find aktuelle danske brugtannoncer for: {query}

        Returnér op til 8 konkrete annoncer, hver med den faktiske udbudspris i hele kroner
        (DKK) og et direkte link til annoncen. Medtag kun annoncer hvor du har set en
        konkret pris. Ingen servicepriser eller samlepriser - kun genstanden selv.

        Gæt aldrig en url og konstruér den aldrig ud fra et mønster. Brug kun links til
        sider du faktisk har åbnet. Hellere færre annoncer end links der ikke virker.
        """;
}
