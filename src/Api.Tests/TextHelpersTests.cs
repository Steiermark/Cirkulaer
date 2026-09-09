using Api.Decision;

namespace Api.Tests;

public class TextHelpersTests
{
    [Theory]
    [InlineData("Møbler og indbo", "moebler og indbo")]
    [InlineData("Elektronik og værktøj", "elektronik og vaerktoej")]
    [InlineData("Blå", "blaa")]
    [InlineData("ÆØÅ", "aeoeaa")]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void Normalize_matches_python(string? input, string expected)
        => Assert.Equal(expected, TextHelpers.Normalize(input));

    [Theory]
    [InlineData("Møbler og indbo", "furniture")]
    [InlineData("Elektronik og værktøj", "electronics")]
    [InlineData("Cykel", "bicycle")]
    [InlineData("Tekstil og tøj", "textile")]
    [InlineData("Farligt affald", "hazardous")]
    [InlineData("Noget helt andet", "other")]
    [InlineData(null, "other")]
    public void CanonicalCategoryId_maps_danish_categories(string? input, string expected)
        => Assert.Equal(expected, TextHelpers.CanonicalCategoryId(input));

    [Fact]
    public void CanonicalCategoryId_prefers_electronics_when_both_terms_appear()
        => Assert.Equal("electronics", TextHelpers.CanonicalCategoryId("Elektronik og møbler"));

    // The bicycle branch matches the substring "cykel", so the Danish plural "Cykler"
    // ("cykl" + "er") falls through to "other". Verified against legacy/server.py.
    [Fact]
    public void CanonicalCategoryId_misses_the_danish_plural_cykler()
        => Assert.Equal("other", TextHelpers.CanonicalCategoryId("Cykler"));
}
