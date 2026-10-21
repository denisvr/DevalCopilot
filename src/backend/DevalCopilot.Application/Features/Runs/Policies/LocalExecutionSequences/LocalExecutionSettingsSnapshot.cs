using System.Globalization;
using System.Text;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;

/// <summary>
/// The canonical, versioned text of the run facts a local execution sequence pins by value (ADR-0060): the requested Codex and Claude
/// model and effort, the Claude turn limit, the token and account stop policies and the immutable budget policies. Every member is
/// framed (an absent value is <c>-</c>, a present text value is its UTF-16 length, a colon and the text) and separated by a newline, so
/// the encoding is unambiguous. A malformed stored value yields <see langword="null"/>: the sequence is never armed over unreadable
/// settings. The snapshot is compared at every claim; it never overrides a fresh safety guard.
/// </summary>
internal static class LocalExecutionSettingsSnapshot
{
    public const string Version = "devalcopilot.local-execution-sequence.settings.v1";

    public static async Task<string?> ReadAsync(IDevalCopilotDbContext dbContext, Guid runId, CancellationToken cancellationToken)
    {
        var run = await dbContext.Runs.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == runId, cancellationToken);
        return run is null ? null : Of(run);
    }

    public static string? Of(Run run)
    {
        var turns = run.ReadRequestedClaudeMaxTurns();
        var accountStop = run.ReadCodexAccountUsageStopPercent();
        if (turns.IsMalformed || accountStop.IsMalformed)
        {
            return null;
        }

        var builder = new StringBuilder(Version).Append('\n');
        Append(builder, "codexModel", run.RequestedCodexModel);
        Append(builder, "codexEffort", run.RequestedCodexEffort);
        Append(builder, "claudeModel", run.RequestedClaudeModel);
        Append(builder, "claudeEffort", run.RequestedClaudeEffort);
        Append(builder, "claudeTurns", turns.Value?.ToString(CultureInfo.InvariantCulture));
        Append(builder, "codexAccountStop", accountStop.Value?.ToString(CultureInfo.InvariantCulture));
        Append(builder, "codexTokenStop", run.CodexTokenStopThreshold?.ToString(CultureInfo.InvariantCulture));
        Append(builder, "claudeTokenStop", run.ClaudeTokenStopThreshold?.ToString(CultureInfo.InvariantCulture));
        Append(builder, "maximumAgentAttempts", run.MaximumAgentAttempts.ToString(CultureInfo.InvariantCulture));
        Append(builder, "maximumInvocationTicks", run.MaximumAgentInvocationTime?.Ticks.ToString(CultureInfo.InvariantCulture));
        Append(builder, "maximumCorrections", run.MaximumReviewCorrectionAttempts.ToString(CultureInfo.InvariantCulture));
        var text = builder.ToString();
        return text.Length <= LocalExecutionSequence.MaximumSettingsSnapshotLength ? text : null;
    }

    private static void Append(StringBuilder builder, string name, string? value) =>
        builder.Append(name).Append('=').Append(value is null ? "-" : value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value)
            .Append('\n');
}
