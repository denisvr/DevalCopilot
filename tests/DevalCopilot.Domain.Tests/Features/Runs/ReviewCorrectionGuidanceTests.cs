using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

public sealed class ReviewCorrectionGuidanceTests
{
    [Fact]
    public void Normalize_trims_unifies_line_endings_and_applies_unicode_form_c_once()
    {
        // "e" + combining acute (U+0301) becomes the single precomposed character.
        var normalized = ReviewCorrectionGuidance.Normalize("  cafe\u0301 first\r\nsecond\rthird  ");

        Assert.Equal("caf\u00e9 first\nsecond\nthird", normalized);
        Assert.Equal(normalized, ReviewCorrectionGuidance.Normalize(normalized));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \r\n\t ")]
    [InlineData("bell\u0007")]
    [InlineData("tab\tinside")]
    [InlineData("the password is x")]
    [InlineData("see C:\\Users\\me")]
    public void Normalize_rejects_blank_control_unsafe_and_invalid_unicode_input(string? raw)
    {
        Assert.Null(ReviewCorrectionGuidance.Normalize(raw));
    }

    [Fact]
    public void Normalize_rejects_an_unpaired_surrogate_instead_of_silently_replacing_it()
    {
        // Built at runtime: xUnit theory data cannot carry an unpaired surrogate through discovery.
        Assert.Null(ReviewCorrectionGuidance.Normalize(new string(['\ud800', 'a'])));
        Assert.Null(ReviewCorrectionGuidance.Normalize(new string(['a', '\udc00'])));
        Assert.Equal("a\U0001F600b", ReviewCorrectionGuidance.Normalize("a\U0001F600b"));
    }

    [Theory]
    [InlineData("Continue only after explicit human authorization.")]
    [InlineData("  Continue only after explicit human authorization.  ")]
    [InlineData("\r\nContinue only after explicit human authorization.\r\n")]
    public void Normalize_rejects_the_reserved_default_rationale_so_guided_and_bodyless_never_collide(string raw)
    {
        Assert.Null(ReviewCorrectionGuidance.Normalize(raw));
        Assert.NotNull(ReviewCorrectionGuidance.Normalize("Continue only after explicit human authorization. Also keep it small."));
    }

    [Fact]
    public void Normalize_enforces_the_maximum_length_after_normalization()
    {
        Assert.NotNull(ReviewCorrectionGuidance.Normalize(new string('a', ReviewCorrectionGuidance.MaximumLength)));
        Assert.Null(ReviewCorrectionGuidance.Normalize(new string('a', ReviewCorrectionGuidance.MaximumLength + 1)));

        // Surrounding whitespace and CRLF collapse do not count toward the bound.
        Assert.NotNull(ReviewCorrectionGuidance.Normalize("  " + new string('a', ReviewCorrectionGuidance.MaximumLength) + "\r\n"));
    }

    [Fact]
    public void Structured_content_round_trips_and_the_default_rationale_is_not_guidance()
    {
        var guided = ReviewCorrectionGuidance.BuildStructuredContentJson("Keep it small.\nAvoid new files.");
        var bodyless = ReviewCorrectionGuidance.BuildStructuredContentJson(ReviewCorrectionGuidance.DefaultRationale);

        Assert.Equal("Keep it small.\nAvoid new files.", ReviewCorrectionGuidance.TryReadRationale(guided));
        Assert.True(ReviewCorrectionGuidance.IsGuidance("Keep it small.\nAvoid new files."));
        Assert.Equal(ReviewCorrectionGuidance.DefaultRationale, ReviewCorrectionGuidance.TryReadRationale(bodyless));
        Assert.False(ReviewCorrectionGuidance.IsGuidance(ReviewCorrectionGuidance.DefaultRationale));
        CollaborationMessageContentPolicy.Validate(CollaborationMessageType.HumanInstruction, guided);
    }

    [Fact]
    public void The_bodyless_content_is_byte_identical_to_the_historical_fixed_message()
    {
        Assert.Equal(
            "{\"instruction\":\"Authorize one additional review-correction attempt.\",\"rationale\":\"Continue only after explicit human authorization.\"}",
            ReviewCorrectionGuidance.BuildStructuredContentJson(ReviewCorrectionGuidance.DefaultRationale));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"instruction\":\"Something else.\",\"rationale\":\"x\"}")]
    [InlineData("{\"instruction\":\"Authorize one additional review-correction attempt.\"}")]
    [InlineData("{\"instruction\":\"Authorize one additional review-correction attempt.\",\"rationale\":\"x\",\"extra\":\"y\"}")]
    [InlineData("{\"instruction\":\"Authorize one additional review-correction attempt.\",\"rationale\":5}")]
    [InlineData("{\"instruction\":\"Authorize one additional review-correction attempt.\",\"rationale\":\"  \"}")]
    public void TryReadRationale_rejects_anything_but_the_fixed_instruction_plus_one_rationale(string json)
    {
        Assert.Null(ReviewCorrectionGuidance.TryReadRationale(json));
    }

    private static string Json(string rationale) =>
        System.Text.Json.JsonSerializer.Serialize(new { instruction = ReviewCorrectionGuidance.FixedInstruction, rationale });

    [Theory]
    [InlineData("overlong")]
    [InlineData("unsafe")]
    [InlineData("control")]
    [InlineData("padded")]
    [InlineData("crlf")]
    [InlineData("not-nfc")]
    public void TryReadRationale_rejects_shape_valid_content_that_is_not_the_canonical_accepted_form(string kind)
    {
        var rationale = kind switch
        {
            "overlong" => new string('x', ReviewCorrectionGuidance.MaximumLength + 1),
            "unsafe" => "the secret note",
            "control" => "bell\u0007char",
            "padded" => "  padded  ",
            "crlf" => "line one\r\nline two",
            _ => "cafe\u0301",
        };

        Assert.Null(ReviewCorrectionGuidance.TryReadRationale(Json(rationale)));
    }

    [Theory]
    [InlineData("reordered")]
    [InlineData("spaced")]
    [InlineData("escaped")]
    public void TryReadRationale_rejects_a_differently_encoded_but_equivalent_document(string kind)
    {
        var instruction = ReviewCorrectionGuidance.FixedInstruction;
        var json = kind switch
        {
            "reordered" => "{\"rationale\":\"Keep it small.\",\"instruction\":\"" + instruction + "\"}",
            "spaced" => "{ \"instruction\": \"" + instruction + "\", \"rationale\": \"Keep it small.\" }",
            _ => "{\"instruction\":\"" + instruction + "\",\"rationale\":\"Keep it \\u0073mall.\"}",
        };

        Assert.Null(ReviewCorrectionGuidance.TryReadRationale(json));
    }

    [Fact]
    public void TryReadRationale_accepts_only_canonical_default_and_canonical_guidance()
    {
        Assert.Equal("Keep it small.", ReviewCorrectionGuidance.TryReadRationale(Json("Keep it small.")));
        Assert.Equal("caf\u00e9", ReviewCorrectionGuidance.TryReadRationale(Json("caf\u00e9")));
        Assert.Equal(
            ReviewCorrectionGuidance.DefaultRationale,
            ReviewCorrectionGuidance.TryReadRationale(Json(ReviewCorrectionGuidance.DefaultRationale)));
    }
}
