using System.Text;
using DevalCopilot.Infrastructure.Features.Processes;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Processes;

public sealed class StreamingOutputRedactorTests
{
    private static string RunToCompletion(StreamingOutputRedactor redactor, params byte[][] chunks)
    {
        var output = new MemoryStream();
        foreach (var chunk in chunks)
        {
            output.Write(redactor.ProcessChunk(chunk));
        }

        output.Write(redactor.Flush());
        return Encoding.UTF8.GetString(output.ToArray());
    }

    [Theory]
    [InlineData("token is ghp_abcdefghijklmnopqrstuvwxyz0123456789AB here")]
    [InlineData("Authorization: Bearer abcDEF012345.-_79 end")]
    [InlineData("AKIAABCDEFGHIJKLMNOP is the key")]
    [InlineData("xoxb-1234567890-abcdefghij token")]
    [InlineData("github_pat_11ABCDEFG0123456789012345678901234567890abcdefghijklmnop end")]
    public void A_supported_pattern_fully_contained_in_one_chunk_is_redacted(string text)
    {
        var redactor = new StreamingOutputRedactor();
        var result = RunToCompletion(redactor, Encoding.UTF8.GetBytes(text));

        Assert.Contains("[REDACTED]", result);
        Assert.DoesNotContain("ghp_abcdefghijklmnopqrstuvwxyz0123456789AB", result);
        Assert.DoesNotContain("abcDEF012345.-_79", result);
        Assert.DoesNotContain("AKIAABCDEFGHIJKLMNOP", result);
        Assert.DoesNotContain("xoxb-1234567890-abcdefghij", result);
        Assert.DoesNotContain("github_pat_11ABCDEFG0123456789012345678901234567890abcdefghijklmnop", result);
    }

    [Fact]
    public void A_pattern_split_exactly_at_the_chunk_boundary_is_still_redacted()
    {
        const string secret = "ghp_abcdefghijklmnopqrstuvwxyz0123456789AB";
        var full = $"before {secret} after";
        var splitIndex = full.IndexOf(secret, StringComparison.Ordinal) + secret.Length / 2;

        var firstChunk = Encoding.UTF8.GetBytes(full[..splitIndex]);
        var secondChunk = Encoding.UTF8.GetBytes(full[splitIndex..]);

        var redactor = new StreamingOutputRedactor();
        var result = RunToCompletion(redactor, firstChunk, secondChunk);

        Assert.Contains("[REDACTED]", result);
        Assert.DoesNotContain(secret, result);
        Assert.Contains("before ", result);
        Assert.Contains(" after", result);
    }

    [Fact]
    public void A_pattern_fed_one_byte_at_a_time_across_many_chunks_is_still_redacted()
    {
        const string secret = "Bearer abcDEF012345.-_79";
        var text = $"prefix {secret} suffix";

        var redactor = new StreamingOutputRedactor();
        var output = new MemoryStream();
        foreach (var b in Encoding.UTF8.GetBytes(text))
        {
            output.Write(redactor.ProcessChunk([b]));
        }

        output.Write(redactor.Flush());
        var result = Encoding.UTF8.GetString(output.ToArray());

        Assert.Contains("[REDACTED]", result);
        Assert.DoesNotContain(secret, result);
        Assert.Contains("prefix ", result);
        Assert.Contains(" suffix", result);
    }

    [Fact]
    public void Multiple_supported_patterns_in_the_same_stream_are_all_redacted()
    {
        var text = "ghp_abcdefghijklmnopqrstuvwxyz0123456789AB and AKIAABCDEFGHIJKLMNOP together";

        var redactor = new StreamingOutputRedactor();
        var result = RunToCompletion(redactor, Encoding.UTF8.GetBytes(text));

        Assert.Equal(2, result.Split("[REDACTED]").Length - 1);
    }

    [Fact]
    public void An_unsupported_secret_shape_is_not_redacted_by_this_pass()
    {
        // Documents the honest limitation: only the fixed, listed pattern shapes are detected.
        const string customSecret = "my-custom-api-key=zzz-not-a-known-shape-12345";

        var redactor = new StreamingOutputRedactor();
        var result = RunToCompletion(redactor, Encoding.UTF8.GetBytes(customSecret));

        Assert.Equal(customSecret, result);
        Assert.DoesNotContain("[REDACTED]", result);
    }

    [Fact]
    public void Ordinary_text_with_no_secret_passes_through_unchanged()
    {
        const string text = "line 1\nline 2: build succeeded\nexit code 0\n";

        var redactor = new StreamingOutputRedactor();
        var result = RunToCompletion(redactor, Encoding.UTF8.GetBytes(text));

        Assert.Equal(text, result);
    }

    [Fact]
    public void Flush_with_nothing_pending_returns_empty()
    {
        var redactor = new StreamingOutputRedactor();
        Assert.Empty(redactor.Flush());
    }

    [Fact]
    public void A_multi_byte_utf8_character_split_across_a_chunk_boundary_is_reconstructed_correctly()
    {
        // "café" — the "é" is a 2-byte UTF-8 sequence; split the raw bytes inside it.
        var bytes = Encoding.UTF8.GetBytes("café result");
        var splitIndex = Encoding.UTF8.GetByteCount("caf") + 1; // lands inside the 2-byte 'é'

        var redactor = new StreamingOutputRedactor();
        var result = RunToCompletion(redactor, bytes[..splitIndex], bytes[splitIndex..]);

        Assert.Equal("café result", result);
    }

    [Fact]
    public void A_pattern_beginning_just_before_a_real_mid_stream_emit_boundary_is_still_redacted()
    {
        // Long enough filler (over the carry-over window) that the first ProcessChunk call
        // actually emits something, rather than holding everything back for Flush — this is
        // the specific code path the carry-over window exists to protect.
        var filler = new string('A', 200);
        const string secret = "ghp_abcdefghijklmnopqrstuvwxyz0123456789AB";
        var text = filler + secret + " tail";

        // Splits 10 characters into the secret, so its still-incomplete prefix sits near the
        // very end of the first chunk — exactly where a naive implementation could accidentally
        // emit it before the rest of the token ever arrives.
        var splitIndex = filler.Length + 10;
        var firstChunk = Encoding.UTF8.GetBytes(text[..splitIndex]);
        var secondChunk = Encoding.UTF8.GetBytes(text[splitIndex..]);

        var redactor = new StreamingOutputRedactor();
        var firstEmit = redactor.ProcessChunk(firstChunk);

        // The still-incomplete secret prefix must not have been emitted early.
        Assert.DoesNotContain("ghp_abcdefghijklmnopqrstuvwxyz012", Encoding.UTF8.GetString(firstEmit));

        var output = new MemoryStream();
        output.Write(firstEmit);
        output.Write(redactor.ProcessChunk(secondChunk));
        output.Write(redactor.Flush());
        var result = Encoding.UTF8.GetString(output.ToArray());

        Assert.Contains("[REDACTED]", result);
        Assert.DoesNotContain(secret, result);
        Assert.Contains(filler, result);
        Assert.Contains(" tail", result);
    }

    [Fact]
    public void Repeated_small_chunks_never_lose_or_duplicate_ordinary_bytes()
    {
        const string text = "abcdefghijklmnopqrstuvwxyz0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var bytes = Encoding.UTF8.GetBytes(text);

        var redactor = new StreamingOutputRedactor();
        var output = new MemoryStream();
        for (var i = 0; i < bytes.Length; i += 3)
        {
            output.Write(redactor.ProcessChunk(bytes.AsSpan(i, Math.Min(3, bytes.Length - i))));
        }

        output.Write(redactor.Flush());
        Assert.Equal(text, Encoding.UTF8.GetString(output.ToArray()));
    }
}
