using System.Text.Json;
using System.Text.RegularExpressions;
using Api.Decision;

namespace Api.Ai;

public static partial class VisionJson
{
    public static Assessment NormalizeFromText(string text)
    {
        using var document = Parse(text);
        return AssessmentNormalizer.Normalize(document.RootElement);
    }

    // Models sometimes wrap the JSON in prose; legacy/server.py:350-358 falls back to the
    // first {...} span rather than failing.
    public static JsonDocument Parse(string text)
    {
        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            var match = BracedSpan().Match(text);
            if (!match.Success)
                throw new InvalidOperationException("AI returnerede ikke gyldig JSON.");
            return JsonDocument.Parse(match.Value);
        }
    }

    public static (string MediaType, string Data) SplitDataUrl(string dataUrl)
    {
        var comma = dataUrl.IndexOf(',');
        if (!dataUrl.StartsWith("data:") || comma < 0)
            throw new InvalidOperationException("Billedet skal sendes som en data-URL.");

        var mediaType = dataUrl[5..comma].Split(';')[0];
        return (string.IsNullOrEmpty(mediaType) ? "image/jpeg" : mediaType, dataUrl[(comma + 1)..]);
    }

    [GeneratedRegex(@"\{.*\}", RegexOptions.Singleline)]
    private static partial Regex BracedSpan();
}
