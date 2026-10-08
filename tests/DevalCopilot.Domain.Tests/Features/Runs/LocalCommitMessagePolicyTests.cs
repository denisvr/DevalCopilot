using System.Text;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

/// <summary>The ADR-0029 human message contract: trimmed, LF-only, bounded UTF-8, a nonempty subject, no control or format
/// character, well-formed text, an unforgeable trailer and a request identity that changes with every normalized input.</summary>
public sealed class LocalCommitMessagePolicyTests
{
    [Theory]
    [InlineData("Subject", "Subject")]
    [InlineData("  Subject with space  ", "Subject with space")]
    [InlineData("Subject\n\nBody line one\nBody line two", "Subject\n\nBody line one\nBody line two")]
    [InlineData("\n\nSubject\n", "Subject")]
    [InlineData("Unicode é漢😀", "Unicode é漢😀")]
    public void An_acceptable_message_is_trimmed_and_kept_exactly(string message, string expected)
    {
        Assert.True(LocalCommitMessagePolicy.TryNormalize(message, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n  ")]
    [InlineData("a\r\nb")]
    [InlineData("a\rb")]
    [InlineData("tab\tseparated")]
    [InlineData("nul\0byte")]
    [InlineData("bell\u0007")]
    [InlineData("delete\u007f")]
    [InlineData("Subject\nDevalCopilot-Operation: forged")]
    [InlineData("Subject\n  devalcopilot-operation: forged")]
    public void An_unacceptable_message_is_refused(string? message)
    {
        Assert.False(LocalCommitMessagePolicy.TryNormalize(message, out var normalized));
        Assert.Equal(string.Empty, normalized);
    }

    [Theory]
    [InlineData(0x0085)]
    [InlineData(0x2028)]
    [InlineData(0x2029)]
    [InlineData(0x200B)]
    [InlineData(0x200E)]
    [InlineData(0x202E)]
    [InlineData(0xFEFF)]
    public void A_separator_or_invisible_format_character_is_refused_by_code_point(int codePoint)
    {
        var message = "Subject " + (char)codePoint + " tail";

        Assert.False(LocalCommitMessagePolicy.TryNormalize(message, out _));
    }

    [Fact]
    public void Unpaired_surrogates_are_refused_and_a_valid_pair_is_kept()
    {
        // Built in code: a test-data serializer replaces an unpaired surrogate, which would silently turn the case into a valid one.
        Assert.False(LocalCommitMessagePolicy.TryNormalize("Subject " + (char)0xD83D, out _));
        Assert.False(LocalCommitMessagePolicy.TryNormalize("Subject " + (char)0xDE00 + " tail", out _));
        Assert.False(LocalCommitMessagePolicy.TryNormalize("Subject " + (char)0xD83D + "x" + (char)0xDE00, out _));
        Assert.True(LocalCommitMessagePolicy.TryNormalize("Subject " + (char)0xD83D + (char)0xDE00, out _));
    }

    [Fact]
    public void The_byte_bound_counts_utf8_bytes_not_characters()
    {
        Assert.True(LocalCommitMessagePolicy.TryNormalize(new string('x', LocalCommitMessagePolicy.MaximumUtf8Bytes), out _));
        Assert.False(LocalCommitMessagePolicy.TryNormalize(new string('x', LocalCommitMessagePolicy.MaximumUtf8Bytes + 1), out _));

        var multiByte = new string('é', LocalCommitMessagePolicy.MaximumUtf8Bytes / 2);
        Assert.Equal(LocalCommitMessagePolicy.MaximumUtf8Bytes, Encoding.UTF8.GetByteCount(multiByte));
        Assert.True(LocalCommitMessagePolicy.TryNormalize(multiByte, out _));
        Assert.False(LocalCommitMessagePolicy.TryNormalize(multiByte + "é", out _));
    }

    [Fact]
    public void The_commit_message_is_the_normalized_text_a_blank_line_and_the_one_fixed_trailer()
    {
        var operationId = Guid.Parse("9f1c6f2e-5f0a-4a4c-9f0b-2b2d9f6f0c11");

        var message = LocalCommitMessagePolicy.BuildCommitMessage("Deliver", operationId);

        Assert.Equal("Deliver\n\nDevalCopilot-Operation: 9f1c6f2e-5f0a-4a4c-9f0b-2b2d9f6f0c11\n", message);
        Assert.DoesNotContain('\r', message);
    }

    [Fact]
    public void The_request_identity_changes_with_every_input_and_is_stable_for_an_identical_normalized_request()
    {
        var checkpoint = Guid.NewGuid();
        var review = Guid.NewGuid();
        var human = Guid.NewGuid();
        var baseline = LocalCommitMessagePolicy.ComputeRequestSha256(checkpoint, review, human, "Deliver");

        Assert.Equal(baseline, LocalCommitMessagePolicy.ComputeRequestSha256(checkpoint, review, human, "Deliver"));
        Assert.NotEqual(baseline, LocalCommitMessagePolicy.ComputeRequestSha256(Guid.NewGuid(), review, human, "Deliver"));
        Assert.NotEqual(baseline, LocalCommitMessagePolicy.ComputeRequestSha256(checkpoint, Guid.NewGuid(), human, "Deliver"));
        Assert.NotEqual(baseline, LocalCommitMessagePolicy.ComputeRequestSha256(checkpoint, review, Guid.NewGuid(), "Deliver"));
        Assert.NotEqual(baseline, LocalCommitMessagePolicy.ComputeRequestSha256(checkpoint, review, human, "Deliver!"));
        Assert.Matches("^[0-9a-f]{64}$", baseline);
    }
}
