using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordVerificationDiagnosisResult;

/// <summary>
/// Atomically records a Codex verification-diagnosis attempt's terminal outcome, its sealed artifact metadata, and — only
/// for <see cref="AgentOutcome.DiagnosisFindingsRecorded"/> or <see cref="AgentOutcome.DiagnosisEscalated"/> — either one
/// <see cref="CollaborationMessageType.ReviewFinding"/> message per finding or exactly one
/// <see cref="CollaborationMessageType.Escalation"/>, each replying to the diagnosed ExecutionReport, plus their events. It
/// never records an approval and never a checkpoint review. Before any semantic result is recorded the handler re-reads the
/// attempt's applicability untracked; changed verification evidence or eligibility turns the response into a truthful
/// <see cref="AgentOutcome.VerificationEvidenceChanged"/> with no finding or escalation. No external I/O happens in this
/// handler, so this is a plain, automatically-transacted command.
/// </summary>
public sealed record RecordVerificationDiagnosisResultCommand(
    Guid RunId,
    Guid AttemptId,
    AgentOutcome Outcome,
    string? CompletionFingerprintSha256,
    IReadOnlyList<SealedVerificationDiagnosisArtifact> SealedArtifacts,
    ValidatedVerificationDiagnosis? Diagnosis,
    string? ProviderSessionId,
    AgentProcessEvidence? ProcessEvidence = null,
    AgentTokenUsage? TokenUsage = null) : ICommand<Result<RecordVerificationDiagnosisResultCommandResult>>
{
    /// <summary>ADR-0065: the opaque in-memory ticket the original host received from the committed dispatch marker of a linked local
    /// execution operation. It is public to the host supervisors only to be carried unchanged; it cannot be minted outside the
    /// Application, is never serialized and is absent for every unlinked operation.</summary>
    public LocalExecutionDispatchTicket? DispatchTicket { get; init; }
}
