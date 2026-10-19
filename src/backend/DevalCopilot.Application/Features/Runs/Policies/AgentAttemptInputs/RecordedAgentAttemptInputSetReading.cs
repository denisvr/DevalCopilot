namespace DevalCopilot.Application.Features.Runs.Policies.AgentAttemptInputs;

/// <summary>The outcome of reading one attempt's recorded input set: not found (an unknown run or a missing, foreign or non-Agent
/// attempt), unavailable (no input is returned and which proof failed is not disclosed) or the whole proved set, which is empty only
/// for a Planner.</summary>
internal sealed record RecordedAgentAttemptInputSetReading(bool Found, RecordedAgentAttemptInputSet? Set)
{
    public static readonly RecordedAgentAttemptInputSetReading NotFound = new(false, null);

    public static readonly RecordedAgentAttemptInputSetReading Unavailable = new(true, null);

    public static RecordedAgentAttemptInputSetReading Proven(RecordedAgentAttemptInputSet set) => new(true, set);
}
