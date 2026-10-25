using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// ADR-0065: the versioned canonical confirmation of one exact ownership release. The digest covers the entire witness, observation,
/// terminal result, sequence, step, source and admission tuple and the UTC expiry the Human was shown (at most two minutes after the
/// read). It is an exact-intent check only, never a credential or process proof: the host recomputes it from fresh facts under the final
/// write lock and refuses a stale, mismatched or expired candidate without retargeting it.
/// </summary>
public static class LocalExecutionOwnershipReleaseConfirmation
{
    public const int CurrentVersion = 1;

    /// <summary>The longest a shown confirmation may remain valid after it was read.</summary>
    public static readonly TimeSpan MaximumLifetime = TimeSpan.FromMinutes(2);

    /// <summary>The expiry shown for a candidate read at <paramref name="readAtUtc"/>: the full lifetime, in UTC, truncated to whole
    /// seconds so its canonical text is short and exact.</summary>
    public static DateTimeOffset ExpiryOf(DateTimeOffset readAtUtc)
    {
        var expiry = readAtUtc.ToUniversalTime() + MaximumLifetime;
        return new DateTimeOffset(expiry.Ticks - (expiry.Ticks % TimeSpan.TicksPerSecond), TimeSpan.Zero);
    }

    /// <summary>Whether <paramref name="expiresAtUtc"/> is still valid at <paramref name="nowUtc"/> and no more than the maximum lifetime
    /// away: a client cannot extend its own confirmation.</summary>
    public static bool IsLive(DateTimeOffset expiresAtUtc, DateTimeOffset nowUtc) =>
        expiresAtUtc.Offset == TimeSpan.Zero && nowUtc < expiresAtUtc && expiresAtUtc - nowUtc <= MaximumLifetime;

    /// <summary>The lowercase hexadecimal SHA-256 of the versioned canonical text of <paramref name="facts"/> and the shown expiry.</summary>
    public static string Digest(LocalExecutionOwnershipReleaseFacts facts, DateTimeOffset expiresAtUtc)
    {
        var text = new StringBuilder();
        Line(text, "version", CurrentVersion.ToString(CultureInfo.InvariantCulture));
        Line(text, "expiresAtUtc", Time(expiresAtUtc));
        Line(text, "witnessId", Id(facts.WitnessId));
        Line(text, "observationId", Id(facts.ObservationId));
        Line(text, "family", facts.Family.ToString());
        Line(text, "operationId", Id(facts.OperationId));
        Line(text, "sequenceId", Id(facts.SequenceId));
        Line(text, "runId", Id(facts.RunId));
        Line(text, "projectId", Id(facts.ProjectId));
        Line(text, "workspaceId", Id(facts.WorkspaceId));
        Line(text, "sequenceState", facts.SequenceState.ToString());
        Line(text, "admissionMode", facts.AdmissionMode.ToString());
        Line(text, "admissionId", Id(facts.AdmissionId));
        Line(text, "stepId", Id(facts.StepId));
        Line(text, "kind", facts.Kind.ToString());
        Line(text, "ordinal", Number(facts.Ordinal));
        Line(text, "pass", Number(facts.PassNumber));
        Line(text, "recipeOrdinal", facts.RecipeOrdinal is { } recipe ? Number(recipe) : "-");
        Line(text, "predecessorStepId", Id(facts.PredecessorStepId));
        Line(text, "sourceCheckpointId", Id(facts.SourceCheckpointId));
        Line(text, "sourceFingerprintSha256", facts.SourceFingerprintSha256);
        Line(text, "reportMessageId", Id(facts.ReportMessageId));
        Line(text, "subjectAttemptId", Id(facts.SubjectAttemptId));
        Line(text, "agentResponseContract", facts.AgentResponseContract?.ToString() ?? "-");
        Line(text, "recipeCommandId", Id(facts.RecipeCommandId));
        Line(text, "operationStatus", facts.OperationStatus);
        Line(text, "agentOutcome", facts.AgentOutcome?.ToString() ?? "-");
        Line(text, "completionFingerprintSha256", facts.CompletionFingerprintSha256 ?? "-");
        Line(text, "processOutcome", facts.ProcessOutcome.ToString());
        Line(text, "semanticExitCode", facts.SemanticExitCode is { } semantic ? Number(semantic) : "-");
        Line(text, "processDurationTicks", facts.ProcessDurationTicks.ToString(CultureInfo.InvariantCulture));
        Line(text, "physicalExitCode", Number(facts.PhysicalExitCode));
        Line(text, "observedElapsedTicks", facts.ObservedElapsedTicks.ToString(CultureInfo.InvariantCulture));
        Line(text, "dispatchedAtUtc", Time(facts.DispatchedAtUtc));
        Line(text, "recordedAtUtc", Time(facts.RecordedAtUtc));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))).ToLowerInvariant();
    }

    /// <summary>The canonical UTC round-trip text of a time (seven fractional digits and a trailing Z), the form the confirmation expiry
    /// and every release time travel in.</summary>
    public static string Time(DateTimeOffset value) => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    /// <summary>Parses exactly the canonical UTC round-trip text produced by <see cref="Time"/>; any other spelling (another offset,
    /// fewer fractional digits, surrounding white space) is refused.</summary>
    public static bool TryParseTime(string? text, out DateTimeOffset value)
    {
        value = default;
        if (text is not { Length: > 0 and <= 40 }
            || !DateTime.TryParseExact(text, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            || parsed.Kind != DateTimeKind.Utc)
        {
            return false;
        }

        var candidate = new DateTimeOffset(parsed, TimeSpan.Zero);
        if (!string.Equals(Time(candidate), text, StringComparison.Ordinal))
        {
            return false;
        }

        value = candidate;
        return true;
    }

    private static string Id(Guid? value) => value is { } id ? id.ToString("D", CultureInfo.InvariantCulture) : "-";

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static void Line(StringBuilder text, string name, string value) => text.Append(name).Append('=').Append(value).Append('\n');
}
