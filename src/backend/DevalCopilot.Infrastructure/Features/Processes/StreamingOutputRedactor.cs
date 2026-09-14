using System.Text;
using System.Text.RegularExpressions;

namespace DevalCopilot.Infrastructure.Features.Processes;

/// <summary>
/// A bounded, stateful, streaming redactor: scrubs a fixed, documented set of secret-shaped
/// patterns from captured process output before any byte reaches disk, an artifact API, or UI
/// state. Raw bytes exist only transiently, inside one <see cref="ProcessChunk"/> call.
///
/// <para>
/// Supported fixed patterns (longest first, in bytes): a Bearer authorization value (up to 107),
/// a GitHub fine-grained personal access token (up to 93), a Slack token (up to 83), a GitHub
/// classic personal access token (40), an AWS access key id (20). <see cref="MaxSupportedPatternLength"/>
/// is a documented, generous upper bound (128 bytes) over all of them.
/// </para>
///
/// <para>
/// Cross-chunk correctness: every accepted chunk is prefixed with a carry-over of the previous
/// chunk's last <c>MaxSupportedPatternLength - 1</c> bytes before matching, and the same amount
/// is held back (never emitted) after matching — the minimum window that guarantees a supported
/// pattern beginning anywhere in the held-back tail cannot be split by a flush boundary, since a
/// complete match is at most <see cref="MaxSupportedPatternLength"/> bytes and an in-progress
/// (not-yet-complete) one is therefore at most one byte shorter. <see cref="Flush"/> must be
/// called exactly once, after the last chunk, to redact and release the final carry-over.
/// </para>
///
/// <para>
/// A second, independent carry-over protects UTF-8 correctness: a raw chunk boundary can split
/// a multi-byte codepoint, so the still-incomplete trailing bytes of each chunk are held back,
/// undecoded, until enough of the next chunk arrives to complete them — never decoded (and
/// potentially replacement-charred) prematurely.
/// </para>
///
/// <para>
/// Honest limitation: only these fixed shapes are detected. An unknown or custom secret format,
/// or a supported pattern longer than its stated bound, is not redacted by this pass. This is
/// defense in depth — the primary control remains that no ambient environment or real credential
/// is ever passed to a probed or executed child process in the first place.
/// </para>
/// </summary>
internal sealed partial class StreamingOutputRedactor
{
    /// <summary>Generous upper bound over every supported pattern's maximum length, in bytes.
    /// One less than this is the carry-over window on both sides of a flush boundary.</summary>
    internal const int MaxSupportedPatternLength = 128;

    private const int CarryOverLength = MaxSupportedPatternLength - 1;
    private const string RedactedMarker = "[REDACTED]";

    private static readonly Regex[] Patterns =
    [
        BearerAuthorizationValue(),
        GitHubFineGrainedToken(),
        SlackToken(),
        GitHubPersonalAccessToken(),
        AwsAccessKeyId(),
    ];

    private byte[] _carryOver = [];

    /// <summary>
    /// Accepts the next raw chunk, redacts it together with the retained carry-over, and returns
    /// only the portion now safe to emit — which may be empty while too little data has
    /// accumulated to guarantee no supported pattern is mid-match at the tail.
    /// </summary>
    public byte[] ProcessChunk(ReadOnlySpan<byte> chunk)
    {
        var working = new byte[_carryOver.Length + chunk.Length];
        _carryOver.CopyTo(working.AsSpan());
        chunk.CopyTo(working.AsSpan(_carryOver.Length));

        // A raw chunk boundary can split a multi-byte UTF-8 codepoint. Decoding before that
        // trailing incomplete sequence is complete would corrupt it (the default decoder
        // replaces an incomplete tail with U+FFFD immediately, and once replaced the original
        // bytes are unrecoverable) — so only the decodable prefix is decoded and redacted now;
        // the still-incomplete raw tail is carried over untouched, ahead of the next chunk.
        var decodableLength = Utf8Boundary.FindDecodableLength(working);
        var incompleteRawTail = working[decodableLength..];

        var redacted = Encoding.UTF8.GetBytes(Redact(Encoding.UTF8.GetString(working, 0, decodableLength)));

        if (redacted.Length <= CarryOverLength)
        {
            _carryOver = Concat(redacted, incompleteRawTail);
            return [];
        }

        var cutIndex = AlignToUtf8CharacterBoundary(redacted, redacted.Length - CarryOverLength);
        var emit = redacted[..cutIndex];
        _carryOver = Concat(redacted[cutIndex..], incompleteRawTail);
        return emit;
    }

    /// <summary>Redacts and releases whatever remains held back. Call exactly once, after the
    /// source stream has reached its natural end — at which point any still-incomplete trailing
    /// bytes in the carry-over are genuinely malformed (there is no more data coming to
    /// complete them), not merely chunk-boundary artifacts, so they are decoded leniently here
    /// like any other invalid byte sequence.</summary>
    public byte[] Flush()
    {
        var result = Encoding.UTF8.GetBytes(Redact(Encoding.UTF8.GetString(_carryOver)));
        _carryOver = [];
        return result;
    }

    private static byte[] Concat(byte[] first, byte[] second)
    {
        if (second.Length == 0)
        {
            return first;
        }

        var combined = new byte[first.Length + second.Length];
        first.CopyTo(combined, 0);
        second.CopyTo(combined, first.Length);
        return combined;
    }

    private static string Redact(string text)
    {
        foreach (var pattern in Patterns)
        {
            text = pattern.Replace(text, RedactedMarker);
        }

        return text;
    }

    /// <summary>
    /// Walks the cut point backward at most 3 bytes while it lands on a UTF-8 continuation byte
    /// (<c>10xxxxxx</c>), so a multi-byte codepoint is never split between the emitted portion
    /// and the new carry-over. <paramref name="buffer"/> is a fresh UTF-8 encoding of a valid
    /// string, so this can only ever need to move backward, never forward.
    /// </summary>
    private static int AlignToUtf8CharacterBoundary(byte[] buffer, int index)
    {
        var steps = 0;
        while (index > 0 && steps < 3 && (buffer[index] & 0b1100_0000) == 0b1000_0000)
        {
            index--;
            steps++;
        }

        return index;
    }

    [GeneratedRegex(@"Bearer [A-Za-z0-9\-_.=]{8,100}")]
    private static partial Regex BearerAuthorizationValue();

    [GeneratedRegex(@"github_pat_[A-Za-z0-9_]{20,82}")]
    private static partial Regex GitHubFineGrainedToken();

    [GeneratedRegex(@"xox[baprs]-[A-Za-z0-9-]{10,72}")]
    private static partial Regex SlackToken();

    [GeneratedRegex(@"ghp_[A-Za-z0-9]{36}")]
    private static partial Regex GitHubPersonalAccessToken();

    [GeneratedRegex(@"AKIA[0-9A-Z]{16}")]
    private static partial Regex AwsAccessKeyId();
}
