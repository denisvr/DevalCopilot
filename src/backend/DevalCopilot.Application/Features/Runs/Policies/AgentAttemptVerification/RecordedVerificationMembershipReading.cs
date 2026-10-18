namespace DevalCopilot.Application.Features.Runs.Policies.AgentAttemptVerification;

/// <summary>The outcome of reading one attempt's recorded verification membership: not found (an unknown run or a missing, foreign or
/// non-Agent attempt), unavailable (no membership is shown and which proof failed is not disclosed) or the whole proved membership.</summary>
internal sealed record RecordedVerificationMembershipReading(bool Found, RecordedVerificationMembership? Membership)
{
    public static readonly RecordedVerificationMembershipReading NotFound = new(false, null);

    public static readonly RecordedVerificationMembershipReading Unavailable = new(true, null);

    public static RecordedVerificationMembershipReading Proven(RecordedVerificationMembership membership) => new(true, membership);
}
