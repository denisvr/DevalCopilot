using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetRunCockpit;

/// <summary>One-pass, constant-memory aggregation over dispatched Agent attempts. A still-
/// <see cref="AttemptStatus.Running"/> attempt always contributes only to the pending count, even
/// when its persisted row unexpectedly already carries seemingly-valid usage fields — non-terminal
/// evidence is never trusted, regardless of what is physically stored. A terminal attempt without
/// known usage contributes only to the terminal-unknown count, never to a partial sum.</summary>
internal sealed class RunCockpitTokenUsageAccumulator
{
    private int _known;
    private int _pending;
    private int _terminalUnknown;
    private long _input;
    private long _output;
    private long? _cacheCreation;
    private long? _cacheRead;

    public void Add(AttemptStatus status, AgentTokenUsageEvidence? usage)
    {
        if (status == AttemptStatus.Running)
        {
            // A Running attempt has not concluded, so any usage value already sitting in its
            // persisted row cannot be trusted yet — it is discarded here rather than summed, even
            // if it happens to already be well-formed.
            _pending = checked(_pending + 1);
            return;
        }

        if (usage is null)
        {
            _terminalUnknown = checked(_terminalUnknown + 1);
            return;
        }

        _known = checked(_known + 1);
        _input = checked(_input + usage.InputTokens);
        _output = checked(_output + usage.OutputTokens);
        if (usage.CacheCreationInputTokens is { } creation)
        {
            _cacheCreation = checked((_cacheCreation ?? 0) + creation);
        }

        if (usage.CacheReadInputTokens is { } read)
        {
            _cacheRead = checked((_cacheRead ?? 0) + read);
        }
    }

    public RunCockpitTokenUsageSummary ToSummary()
    {
        var unknown = checked(_pending + _terminalUnknown);
        var completeness = _known == 0 && unknown == 0
            ? RunTokenUsageCompleteness.NoDispatchedAttempts
            : unknown == 0
                ? RunTokenUsageCompleteness.Complete
                : _terminalUnknown == 0
                    ? RunTokenUsageCompleteness.PendingEvidence
                    : RunTokenUsageCompleteness.Partial;

        return new RunCockpitTokenUsageSummary(
            completeness, _known, unknown, _pending, _terminalUnknown, _input, _output, _cacheCreation, _cacheRead);
    }
}
