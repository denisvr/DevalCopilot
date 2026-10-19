namespace DevalCopilot.Application.Features.Runs.Policies.AgentAttemptOutputs;

/// <summary>The outcome of reading one attempt's recorded output set: not found (an unknown run or a missing, foreign or non-Agent
/// attempt), unavailable (no output is returned and which proof failed is not disclosed) or the whole proved set, which may be empty.</summary>
internal sealed record RecordedAgentAttemptOutputSetReading(bool Found, RecordedAgentAttemptOutputSet? Set)
{
    public static readonly RecordedAgentAttemptOutputSetReading NotFound = new(false, null);

    public static readonly RecordedAgentAttemptOutputSetReading Unavailable = new(true, null);

    public static RecordedAgentAttemptOutputSetReading Proven(RecordedAgentAttemptOutputSet set) => new(true, set);
}
