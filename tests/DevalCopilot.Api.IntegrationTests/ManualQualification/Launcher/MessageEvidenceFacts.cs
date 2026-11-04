namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// The closed facts one message-evidence comparison is made against: the attempt, the provider, role, outcome and process facts the
/// evidence must repeat, the envelope kind the role expects, and the complete ordered recorded input identities (as one ordered key).
/// A remembered agreement is valid only while every read of the stage yields exactly these facts.
/// </summary>
public sealed record MessageEvidenceFacts(
    Guid AttemptId,
    string Provider,
    string Role,
    string? Outcome,
    string? ProcessOutcome,
    int? ExitCode,
    string ExpectedEnvelope,
    string OrderedInputs)
{
    public static MessageEvidenceFacts Create(
        Guid attemptId,
        string provider,
        string role,
        string? outcome,
        string? processOutcome,
        int? exitCode,
        string expectedEnvelope,
        IReadOnlyList<Guid> inputs) =>
        new(attemptId, provider, role, outcome, processOutcome, exitCode, expectedEnvelope, string.Join(",", inputs));
}
