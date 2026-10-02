using System.Text;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Policies.VerificationDiagnosis;
using DevalCopilot.Domain.Features.Projects;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.CreateVerificationDiagnosisAttempt;

/// <summary>
/// Reads and bounds the verified, redacted failure-output excerpts a verification diagnosis seals into its manifest
/// (ADR-0018). Every failed stream is read through <see cref="IArtifactStore.VerifyAndReadSealedAsync"/>, so containment,
/// byte length, and content hash are verified against the row the verification result recorded before any byte is used: a
/// missing or unverifiable failed stream refuses the claim and is never turned into an invented empty log. Excerpts are
/// deterministic prefixes — at most <see cref="MaxBytesPerStream"/> UTF-8 bytes per stream and
/// <see cref="MaxTotalBytes"/> in total, allocated in command order (standard output before standard error) — and each one is
/// labeled with whether the capture itself was truncated or of unknown completeness, and whether the excerpt is complete,
/// shortened locally, empty, or omitted by the total budget. Passed executions need none. The excerpts carry no executable
/// path, argument, storage path, or hash.
/// </summary>
internal static class VerificationFailureExcerpts
{
    public const int MaxBytesPerStream = 2 * 1024;
    public const int MaxTotalBytes = 12 * 1024;

    public const string StateComplete = "complete";
    public const string StateShortened = "shortened";
    public const string StateEmpty = "empty";
    public const string StateOmittedByBudget = "omittedByBudget";

    /// <summary>One verified stream prefix, read once before budgeting.</summary>
    internal sealed record RawStream(
        Guid ExecutionId,
        string Stream,
        long CapturedByteLength,
        bool? CaptureTruncated,
        string PrefixText,
        bool PrefixCoversAll);

    internal sealed record StreamExcerpt(
        string Stream,
        long CapturedBytes,
        string CaptureTruncation,
        string ExcerptState,
        int ExcerptBytes,
        string Text);

    public static async Task<(IReadOnlyList<RawStream>? Streams, Error? Error)> ReadVerifiedPrefixesAsync(
        IArtifactStore artifactStore, VerificationDiagnosisEvidence.Selection selection, CancellationToken cancellationToken)
    {
        var streams = new List<RawStream>();
        foreach (var entry in selection.Entries.Where(entry => entry.Failed))
        {
            foreach (var (name, output) in new[] { ("standardOutput", entry.StandardOutput!), ("standardError", entry.StandardError!) })
            {
                var window = await artifactStore.VerifyAndReadSealedAsync(
                    output.RelativeStoragePath, output.ByteLength, output.ContentHash, 0, MaxBytesPerStream, cancellationToken);
                if (window.Status != SealedReadStatus.Ok)
                {
                    return (null, Error.Conflict(
                        VerificationDiagnosisEvidence.OutputUnavailableCode,
                        $"The sealed output of the failed verification command '{entry.Command.Name}' could not be verified."));
                }

                streams.Add(new RawStream(
                    entry.Execution.Id,
                    name,
                    output.ByteLength,
                    output.CaptureOutcome == VerificationOutputCaptureOutcome.RecoveredAfterHostInterruption ? null : output.Truncated,
                    window.Text,
                    window.NextOffset >= output.ByteLength));
            }
        }

        return (streams, null);
    }

    /// <summary>Allocates the total excerpt budget over the already-read prefixes, in order; deterministic and idempotent.</summary>
    public static IReadOnlyDictionary<(Guid ExecutionId, string Stream), StreamExcerpt> Allocate(
        IReadOnlyList<RawStream> streams, int totalBudgetBytes)
    {
        var remaining = totalBudgetBytes;
        var result = new Dictionary<(Guid, string), StreamExcerpt>();
        foreach (var stream in streams)
        {
            var truncation = stream.CaptureTruncated switch { true => "truncated", false => "notTruncated", null => "unknown" };
            string text;
            string state;
            if (stream.CapturedByteLength == 0)
            {
                (text, state) = (string.Empty, StateEmpty);
            }
            else if (remaining <= 0)
            {
                (text, state) = (string.Empty, StateOmittedByBudget);
            }
            else
            {
                var prefixBytes = Encoding.UTF8.GetByteCount(stream.PrefixText);
                text = TruncateUtf8(stream.PrefixText, Math.Min(Math.Min(prefixBytes, MaxBytesPerStream), remaining));
                var used = Encoding.UTF8.GetByteCount(text);
                remaining -= used;
                state = used == prefixBytes && stream.PrefixCoversAll ? StateComplete : StateShortened;
            }

            result[(stream.ExecutionId, stream.Stream)] = new StreamExcerpt(
                stream.Stream, stream.CapturedByteLength, truncation, state, Encoding.UTF8.GetByteCount(text), text);
        }

        return result;
    }

    /// <summary>The longest prefix of <paramref name="text"/> that is at most <paramref name="maxBytes"/> UTF-8 bytes, cut only
    /// between whole code points.</summary>
    internal static string TruncateUtf8(string text, int maxBytes)
    {
        var builder = new StringBuilder();
        var bytes = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var runeBytes = rune.Utf8SequenceLength;
            if (bytes + runeBytes > maxBytes)
            {
                break;
            }

            builder.Append(rune.ToString());
            bytes += runeBytes;
        }

        return builder.ToString();
    }
}
