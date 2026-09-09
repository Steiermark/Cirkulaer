using System.Text.Json;
using Api.Ai;

namespace Api.Tests;

public class AssessmentNormalizerTests
{
    [Fact]
    public void Billy_fixture_identifies_ikea_brand_and_model()
    {
        var assessment = TestAssessments.Build("billy");

        Assert.Equal("BILLY-reol", assessment.ObjectName);
        Assert.Equal("IKEA", assessment.Brand);
        Assert.Equal("BILLY", assessment.Model);
        Assert.Equal("Bogreol", assessment.Subcategory);
    }

    [Fact]
    public void Billy_fixture_finds_the_ikea_producer_program()
        => Assert.Equal(["ikea_gensalg"], TestAssessments.Build("billy").ProducerProgramCandidates);

    [Fact]
    public void Generic_furniture_infers_ikea_from_the_filename()
    {
        Assert.Equal("IKEA", TestAssessments.Build("ikea-stol.jpg").Brand);
        Assert.Null(TestAssessments.Build("stol.jpg").Brand);
    }

    [Fact]
    public void Unmatched_filename_falls_back()
    {
        var assessment = TestAssessments.Build("dsc_0001.jpg");

        Assert.Equal("Ukendt testgenstand", assessment.ObjectName);
        Assert.Equal(0.25, assessment.Confidence);
    }

    [Fact]
    public void Test_assessments_are_flagged_as_test_mode()
        => Assert.Equal("test", TestAssessments.Build("billy").AnalysisMode);

    [Fact]
    public void Empty_payload_gets_danish_defaults()
    {
        var assessment = AssessmentNormalizer.Normalize(JsonDocument.Parse("{}").RootElement);

        Assert.Equal("Ukendt genstand", assessment.ObjectName);
        Assert.Equal("Ukendt kategori", assessment.Category);
        Assert.Equal("Ukendt fraktion", assessment.WasteCategory);
        Assert.Equal("unknown", assessment.ConditionEstimate);
        Assert.Equal(0.0, assessment.Confidence);
        Assert.Equal(
            ["AI-vurderingen indeholder usikkerhed og bør bekræftes af brugeren."],
            assessment.UncertaintyNotes);
    }

    [Fact]
    public void Category_id_is_derived_when_the_model_omits_it()
    {
        var assessment = AssessmentNormalizer.Normalize(
            JsonDocument.Parse("""{"category":"Møbler og indbo"}""").RootElement);

        Assert.Equal("furniture", assessment.CategoryId);
    }

    [Theory]
    [InlineData("1.5", 1.0)]
    [InlineData("-0.2", 0.0)]
    [InlineData("0.87", 0.87)]
    public void Confidence_is_clamped(string raw, double expected)
    {
        var assessment = AssessmentNormalizer.Normalize(
            JsonDocument.Parse($$"""{"confidence":{{raw}}}""").RootElement);

        Assert.Equal(expected, assessment.Confidence);
    }

    [Fact]
    public void Non_numeric_confidence_becomes_zero()
    {
        var assessment = AssessmentNormalizer.Normalize(
            JsonDocument.Parse("""{"confidence":"høj"}""").RootElement);

        Assert.Equal(0.0, assessment.Confidence);
    }

    [Fact]
    public void Non_array_lists_become_empty()
    {
        var assessment = AssessmentNormalizer.Normalize(
            JsonDocument.Parse("""{"materials":"træ","visible_damage":null}""").RootElement);

        Assert.Empty(assessment.Materials);
        Assert.Empty(assessment.VisibleDamage);
    }
}
