using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordReviewCorrectionResult;

public sealed record RecordReviewCorrectionResultCommand(
    Guid RunId,
    Guid AttemptId,
    bool ProcessSucceeded,
    string? CompletionHeadCommitSha,
    string? CompletionFingerprintSha256,
    IReadOnlyList<GitWorkspaceChangedPath> ObservedChangedPaths,
    IReadOnlyList<SealedReviewCorrectionArtifact> SealedArtifacts,
    ValidatedReviewCorrection? Correction,
    string? ProviderSessionId,
    AgentProcessEvidence? ProcessEvidence = null,
    AgentTokenUsage? TokenUsage = null,
    AgentModelContextLimits? ModelContextLimits = null) : ICommand<Result<RecordReviewCorrectionResultCommandResult>>
{
    /// <summary>ADR-0065: the opaque in-memory ticket the original host received from the committed dispatch marker of a linked local
    /// execution operation. It is public to the host supervisors only to be carried unchanged; it cannot be minted outside the
    /// Application, is never serialized and is absent for every unlinked operation.</summary>
    public LocalExecutionDispatchTicket? DispatchTicket { get; init; }
}
