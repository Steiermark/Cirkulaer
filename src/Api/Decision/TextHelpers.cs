namespace Api.Decision;

public static class TextHelpers
{
    public static string Normalize(string? value) =>
        (value ?? "").ToLowerInvariant()
            .Replace("æ", "ae")
            .Replace("ø", "oe")
            .Replace("å", "aa");

    // Matches raw lowercased text, listing both Danish spellings, rather than running on
    // Normalize output. Keep the two apart: the first branch to match wins.
    public static string CanonicalCategoryId(string? category)
    {
        var value = (category ?? "").ToLowerInvariant();

        if (value.Contains("elektronik") || value.Contains("værktøj") || value.Contains("vaerktoej"))
            return "electronics";
        if (value.Contains("møbl") || value.Contains("moebl") || value.Contains("mobl"))
            return "furniture";
        if (value.Contains("cykel") || value.Contains("bike"))
            return "bicycle";
        if (value.Contains("tekstil") || value.Contains("tøj") || value.Contains("toej"))
            return "textile";
        if (value.Contains("farligt") || value.Contains("kemi"))
            return "hazardous";
        return "other";
    }
}
