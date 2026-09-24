using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetRunCockpit;

/// <summary>One-pass, constant-memory aggregation over dispatched Agent attempts. An absent or
/// untrusted row contributes only to the unknown count, never to a partial sum.</summary>
internal sealed class RunCockpitTokenUsageAccumulator
{
    private int _known;
    private int _unknown;
    private long _input;
    private long _output;
    private long? _cacheCreation;
    private long? _cacheRead;

    public void Add(AgentTokenUsageEvidence? usage)
    {
        if (usage is null)
        {
            _unknown = checked(_unknown + 1);
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

    public RunCockpitTokenUsageSummary ToSummary() => new(
        _known == 0 && _unknown == 0
            ? RunTokenUsageCompleteness.NoDispatchedAttempts
            : _unknown == 0
                ? RunTokenUsageCompleteness.Complete
                : RunTokenUsageCompleteness.Partial,
        _known, _unknown, _input, _output, _cacheCreation, _cacheRead);
}
