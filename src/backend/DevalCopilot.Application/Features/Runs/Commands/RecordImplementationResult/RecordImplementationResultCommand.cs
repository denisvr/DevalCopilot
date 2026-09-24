using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Ports;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordImplementationResult;

/// <summary>
/// Atomically records a Claude implementation attempt's terminal outcome, its sealed artifact
/// metadata, and — only when the attempt is classified as
/// <see cref="Domain.Features.Runs.AgentOutcome.Implemented"/> — a new immutable
/// <see cref="Domain.Features.Projects.GitCheckpoint"/> plus its
/// <see cref="Domain.Features.Projects.GitChangedFile"/> rows and exactly one ExecutionReport
/// <see cref="Domain.Features.Runs.CollaborationMessage"/> (and its <see cref="Domain.Features.Runs.RunEvent"/>).
/// The caller (the implementation supervisor) has already invoked the provider and independently
/// re-read fresh Git evidence — this handler receives that evidence as plain data and never
/// performs any filesystem or process I/O itself, so it remains a plain, automatically-transacted
/// command even though it may mark the workspace
/// <see cref="Domain.Features.Projects.WorkspaceStatus.NeedsAttention"/>.
///
/// Unlike <c>RecordChallengeResolutionResultCommand</c>, the caller never supplies a pre-decided
/// <see cref="Domain.Features.Runs.AgentOutcome"/>: <see cref="Domain.Features.Runs.Attempt.CompleteImplementation"/>
/// deliberately has no fingerprint-override logic (source change is expected in this role), so
/// this handler is the one place that classifies the outcome, from the raw ingredients below,
/// exactly per the project's implementation-truthfulness rules.
/// </summary>
/// <param name="ProcessSucceeded">Whether the Claude process itself exited successfully.
/// Independent of whether the worktree was mutated — a crash or timeout may still have edited
/// files first.</param>
/// <param name="CompletionHeadCommitSha">Null only when the post-invocation Git evidence capture
/// itself failed — never null merely because the process failed.</param>
/// <param name="ObservedChangedPaths">The independently observed changed-path evidence from that
/// same post-invocation capture. Empty (never null) when evidence capture failed.</param>
/// <param name="Report">The already-schema-validated final response, or null when the process
/// failed or the response failed <see cref="ImplementationResponseParser"/> validation.</param>
public sealed record RecordImplementationResultCommand(
    Guid RunId,
    Guid AttemptId,
    bool ProcessSucceeded,
    string? CompletionHeadCommitSha,
    string? CompletionFingerprintSha256,
    IReadOnlyList<GitWorkspaceChangedPath> ObservedChangedPaths,
    IReadOnlyList<SealedImplementationArtifact> SealedArtifacts,
    ValidatedImplementationReport? Report,
    string? ProviderSessionId,
    string? ObservedModel = null,
    string? ObservedEffort = null,
    AgentProcessEvidence? ProcessEvidence = null,
    AgentTokenUsage? TokenUsage = null) : ICommand<Result<RecordImplementationResultCommandResult>>;
