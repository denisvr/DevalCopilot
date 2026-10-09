using System.Buffers;
using System.Globalization;
using System.Text;

namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The human-reason contract and the coherence rule of an explicit run abandonment (ADR-0031). A reason is trimmed with CRLF
/// normalized to LF, nonblank, well-formed UTF-16 whose Unicode scalar values include no control, format or line/paragraph-separator
/// character other than LF (classified per scalar, so supplementary-plane characters count), and at most 2 KiB of UTF-8. Abandoned
/// facts are coherent only when the exact row facts and the one Human-authored event agree; an incoherent Abandoned row never
/// permits another intent and never answers a replay.
/// </summary>
public static class RunAbandonmentPolicy
{
    public const int MaximumReasonUtf8Bytes = 2 * 1024;

    /// <summary>The caller-observable normalization: <see langword="false"/> for a missing, blank, malformed, control-bearing or
    /// oversized reason, and the exact text that is persisted and compared otherwise.</summary>
    public static bool TryNormalizeReason(string? reason, out string normalized)
    {
        normalized = string.Empty;
        if (reason is null)
        {
            return false;
        }

        var text = reason.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (text.Length == 0)
        {
            return false;
        }

        // Classify each Unicode scalar value, not each UTF-16 code unit: a valid surrogate pair is one scalar whose category (for
        // example a supplementary-plane Format character such as U+E0001) must be checked like any other. An unpaired surrogate is
        // not a scalar value at all.
        for (var index = 0; index < text.Length;)
        {
            if (Rune.DecodeFromUtf16(text.AsSpan(index), out var scalar, out var consumed) != OperationStatus.Done)
            {
                return false;
            }

            index += consumed;
            if (scalar.Value != '\n' && (Rune.IsControl(scalar)
                || Rune.GetUnicodeCategory(scalar) == UnicodeCategory.Format
                || scalar.Value is 0x2028 or 0x2029))
            {
                return false;
            }
        }

        if (Encoding.UTF8.GetByteCount(text) > MaximumReasonUtf8Bytes)
        {
            return false;
        }

        normalized = text;
        return true;
    }

    /// <summary>True only when the text is already exactly its own normalization.</summary>
    public static bool IsCanonicalReason(string? reason) =>
        TryNormalizeReason(reason, out var normalized) && string.Equals(normalized, reason, StringComparison.Ordinal);

    /// <summary>The single coherence rule shared by intake, replay, the project summary and the read-only status: an Abandoned manual
    /// run with a canonical reason, a recorded time equal to its last advance, no active participant, and exactly one Human-authored,
    /// run-scoped event carrying that same reason at that same time.</summary>
    public static bool IsCoherent(RunAbandonmentFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        if (facts.Lifecycle != RunLifecycle.Abandoned
            || facts.ExecutionMode != RunExecutionMode.ManualAgent
            || !IsCanonicalReason(facts.Reason)
            || facts.AbandonedAtUtc is not { } abandonedAtUtc
            || abandonedAtUtc != facts.LastAdvancedAtUtc
            || !facts.HasNoActiveParticipant
            || facts.AbandonedEvents.Count != 1)
        {
            return false;
        }

        var recorded = facts.AbandonedEvents[0];
        return recorded.IsHumanAndRunScoped
            && string.Equals(recorded.Reason, facts.Reason, StringComparison.Ordinal)
            && recorded.OccurredAtUtc == abandonedAtUtc;
    }
}
