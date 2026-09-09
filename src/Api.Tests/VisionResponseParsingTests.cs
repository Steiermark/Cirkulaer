using System.Text.Json;
using Api.Ai;

namespace Api.Tests;

public class VisionResponseParsingTests
{
    const string AssessmentJson =
        """{"object_name":"BILLY-reol","category":"Møbler og indbo","category_id":"furniture","subcategory":"Bogreol","brand":"IKEA","model":"BILLY","materials":["spånplade"],"visible_damage":[],"condition_estimate":"good","confidence":0.88,"waste_category":"Storskrald","uncertainty_notes":[]}""";

    static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Openai_response_is_parsed_into_an_assessment()
    {
        var element = Parse($$"""
            {"output":[{"content":[{"type":"output_text","text":{{JsonSerializer.Serialize(AssessmentJson)}}}]}]}
            """);

        var assessment = OpenAiVisionProvider.ParseResponse(element);

        Assert.Equal("BILLY-reol", assessment.ObjectName);
        Assert.Equal("furniture", assessment.CategoryId);
        Assert.Equal(0.88, assessment.Confidence);
        Assert.Equal(["ikea_gensalg"], assessment.ProducerProgramCandidates);
    }

    [Fact]
    public void Anthropic_response_is_parsed_into_an_assessment()
    {
        var element = Parse($$"""
            {"content":[{"type":"text","text":{{JsonSerializer.Serialize(AssessmentJson)}}}]}
            """);

        Assert.Equal("BILLY-reol", AnthropicVisionProvider.ParseResponse(element).ObjectName);
    }

    [Fact]
    public void Gemini_response_is_parsed_into_an_assessment()
    {
        var element = Parse(
            """{"candidates":[{"content":{"parts":[{"text":"""
            + JsonSerializer.Serialize(AssessmentJson)
            + """}]}}]}""");

        Assert.Equal("BILLY-reol", GeminiVisionProvider.ParseResponse(element).ObjectName);
    }

    [Fact]
    public void Text_wrapped_in_prose_still_parses()
    {
        var element = Parse(
            """{"output":[{"content":[{"type":"output_text","text":"Her er svaret: {\"object_name\":\"Stol\"} tak"}]}]}""");

        Assert.Equal("Stol", OpenAiVisionProvider.ParseResponse(element).ObjectName);
    }

    [Fact]
    public void Missing_text_raises_the_danish_error()
    {
        var element = Parse("""{"output":[]}""");

        var error = Assert.Throws<InvalidOperationException>(() => OpenAiVisionProvider.ParseResponse(element));
        Assert.Equal("AI returnerede ikke tekst.", error.Message);
    }

    [Fact]
    public void Unparseable_text_raises_the_danish_error()
    {
        var element = Parse("""{"output":[{"content":[{"type":"output_text","text":"beklager"}]}]}""");

        var error = Assert.Throws<InvalidOperationException>(() => OpenAiVisionProvider.ParseResponse(element));
        Assert.Equal("AI returnerede ikke gyldig JSON.", error.Message);
    }

    [Theory]
    [InlineData("data:image/png;base64,AAAA", "image/png", "AAAA")]
    [InlineData("data:image/jpeg;base64,BBBB", "image/jpeg", "BBBB")]
    public void Data_urls_split_into_media_type_and_payload(string url, string mediaType, string data)
    {
        var (actualType, actualData) = VisionJson.SplitDataUrl(url);

        Assert.Equal(mediaType, actualType);
        Assert.Equal(data, actualData);
    }

    [Fact]
    public void A_non_data_url_is_rejected()
        => Assert.Throws<InvalidOperationException>(
            () => VisionJson.SplitDataUrl("https://example.com/a.png"));

    [Fact]
    public void The_schema_is_strict_and_complete()
    {
        var schema = AssessmentSchema.Element;

        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            schema.GetProperty("properties").EnumerateObject().Count(),
            schema.GetProperty("required").GetArrayLength());
    }
}
