using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;

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
    AgentProcessEvidence? ProcessEvidence = null) : ICommand<Result<RecordReviewCorrectionResultCommandResult>>;
