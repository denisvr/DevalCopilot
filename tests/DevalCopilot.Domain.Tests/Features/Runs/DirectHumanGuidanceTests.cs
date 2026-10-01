using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

public sealed class DirectHumanGuidanceTests
{
    [Fact]
    public void Normalize_trims_unifies_line_endings_and_applies_unicode_form_c_once()
    {
        var normalized = DirectHumanGuidance.Normalize("  cafe\u0301 first\r\nsecond\rthird  ");

        Assert.Equal("caf\u00e9 first\nsecond\nthird", normalized);
        Assert.Equal(normalized, DirectHumanGuidance.Normalize(normalized));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \r\n\t ")]
    [InlineData("bell\u0007")]
    [InlineData("tab\tinside")]
    [InlineData("null\0byte")]
    [InlineData("the password is x")]
    [InlineData("see C:\\Users\\me")]
    public void Normalize_rejects_blank_control_and_unsafe_input(string? raw)
    {
        Assert.Null(DirectHumanGuidance.Normalize(raw));
    }

    [Fact]
    public void Normalize_rejects_an_unpaired_surrogate_instead_of_silently_replacing_it()
    {
        Assert.Null(DirectHumanGuidance.Normalize(new string(['\ud800', 'a'])));
        Assert.Null(DirectHumanGuidance.Normalize(new string(['a', '\udc00'])));
        Assert.Equal("a\U0001F600b", DirectHumanGuidance.Normalize("a\U0001F600b"));
    }

    [Fact]
    public void The_bound_is_600_utf16_code_units_after_normalization()
    {
        Assert.Equal(600, DirectHumanGuidance.MaximumLength);
        Assert.Equal(new string('a', 600), DirectHumanGuidance.Normalize(new string('a', 600)));
        Assert.Null(DirectHumanGuidance.Normalize(new string('a', 601)));

        // Surrogate pairs count as two code units: 300 pairs fit, 301 do not.
        Assert.NotNull(DirectHumanGuidance.Normalize(string.Concat(Enumerable.Repeat("\U0001F600", 300))));
        Assert.Null(DirectHumanGuidance.Normalize(string.Concat(Enumerable.Repeat("\U0001F600", 301))));

        // Whitespace around the text does not count; line endings count once after normalization.
        Assert.NotNull(DirectHumanGuidance.Normalize("  " + new string('a', 600) + "  "));
        Assert.NotNull(DirectHumanGuidance.Normalize(string.Join("\r\n", Enumerable.Repeat("ab", 200)) + "x"));
    }

    [Fact]
    public void Normalize_keeps_a_line_feed_and_the_authorization_sentinel_stays_reserved_only_for_authorization()
    {
        Assert.Equal("one\ntwo", DirectHumanGuidance.Normalize("one\ntwo"));
        Assert.Equal(ReviewCorrectionGuidance.DefaultRationale, DirectHumanGuidance.Normalize(ReviewCorrectionGuidance.DefaultRationale));
        Assert.Null(ReviewCorrectionGuidance.Normalize(ReviewCorrectionGuidance.DefaultRationale));
    }

    [Theory]
    [InlineData("  cafe\u0301 first\r\nsecond  ")]
    [InlineData("plain advisory text")]
    [InlineData("a\U0001F600b")]
    [InlineData("the password is x")]
    [InlineData("tab\tinside")]
    [InlineData("")]
    public void The_shared_policy_gives_authorization_and_direct_guidance_the_same_outcome(string raw)
    {
        Assert.Equal(ReviewCorrectionGuidance.Normalize(raw), DirectHumanGuidance.Normalize(raw));
    }

    [Fact]
    public void Read_distinguishes_absent_valid_and_malformed_stored_text()
    {
        Assert.True(DirectHumanGuidance.Read(null).IsAbsent);

        var valid = DirectHumanGuidance.Read("Prefer the existing helper.");
        Assert.False(valid.IsMalformed);
        Assert.Equal("Prefer the existing helper.", valid.Text);

        foreach (var stored in new[] { "", "  ", " padded ", "cafe\u0301", "a\r\nb", "tab\there", new string('a', 601), "the password is x" })
        {
            var reading = DirectHumanGuidance.Read(stored);
            Assert.True(reading.IsMalformed, stored);
            Assert.Null(reading.Text);
        }

        Assert.True(DirectHumanGuidance.Read(new string(['a', '\ud800'])).IsMalformed);
    }
}
