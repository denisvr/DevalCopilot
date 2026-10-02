namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The closed Domain facts of the explicit verification-diagnosis stage (ADR-0018): the fixed adapter contract the
/// current Codex assignment uses and the exact persisted tuple a diagnosis attempt carries. A diagnosis is a
/// <see cref="AgentRole.CodeReviewer"/> read-only attempt, but it is a different response contract from an ordinary
/// implementation review and never shares its outcomes, input identity, or approval authority. There is no manual format
/// repair of a diagnosis.
/// </summary>
public static class VerificationDiagnosisPolicy
{
    public const string AdapterContractVersion = "codex-verification-diagnosis-v1";

    public const int MinimumFindings = 1;

    public const int MaximumFindings = 10;

    /// <summary>Whether the attempt carries the diagnosis path's exact current provider, role, response contract,
    /// expected message type, protocol version, read-only permission profile, and adapter contract — the coherent tuple
    /// of a diagnosis attempt, whatever its state. It reads persisted values only, so an incoherent row never
    /// qualifies.</summary>
    public static bool HasExactTuple(Attempt attempt) =>
        attempt.Kind == AttemptKind.Agent
        && attempt.AgentProvider == AgentProvider.Codex
        && attempt.AgentRole == AgentRole.CodeReviewer
        && attempt.AgentResponseContract == AgentResponseContract.VerificationDiagnosis
        && attempt.AgentExpectedMessageType == CollaborationMessageType.ReviewFinding
        && attempt.AgentProtocolVersion == CollaborationMessage.ProtocolVersionOne
        && attempt.AgentPermissionProfile == AgentPermissionProfile.ReadOnly
        && attempt.AgentAdapterContractVersion == AdapterContractVersion;
}
