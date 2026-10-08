using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs.Ports;

namespace DevalCopilot.Application.Features.Runs.Errors;

/// <summary>Stable, non-echoing refusals of the explicit local commit (ADR-0029). No error carries a path, a commit message, a
/// stored value, Git output or an exception.</summary>
public static class LocalCommitErrors
{
    public const string RunNotFoundCode = "runs.not_found";
    public const string NotManualRunningCode = "local_commit.run_not_eligible";
    public const string WorkspaceNotReadyCode = "local_commit.workspace_not_ready";
    public const string LeaseNotActiveCode = "local_commit.lease_not_active";
    public const string CheckpointNotCurrentCode = "local_commit.checkpoint_not_current";
    public const string ImplementationNotCurrentCode = "local_commit.implementation_not_current";
    public const string AgentApprovalMissingCode = "local_commit.agent_approval_missing";
    public const string AgentApprovalNotLatestCode = "local_commit.agent_approval_not_latest";
    public const string HumanApprovalMissingCode = "local_commit.human_approval_missing";
    public const string HumanDecisionNotApprovedCode = "local_commit.human_decision_not_approved";
    public const string VerificationNotCurrentCode = "local_commit.verification_not_current";
    public const string MembershipMismatchCode = "local_commit.membership_mismatch";
    public const string ActiveWorkCode = "local_commit.active_work";
    public const string ParentMismatchCode = "local_commit.parent_mismatch";
    public const string OperationConflictCode = "local_commit.operation_conflict";
    public const string AuthorityChangedCode = "local_commit.authority_changed";
    public const string OperationNotFoundCode = "local_commit.operation_not_found";
    public const string RefusedCodePrefix = "local_commit.refused.";

    public static Error RunNotFound() => Error.NotFound(RunNotFoundCode, "The requested run was not found.");

    public static Error OperationNotFound() =>
        Error.NotFound(OperationNotFoundCode, "This run has no local-commit operation.");

    public static Error NotManualRunning() => Error.Conflict(
        NotManualRunningCode, "Only a running manual Agent run can deliver a local commit.");

    public static Error WorkspaceNotReady() => Error.Conflict(
        WorkspaceNotReadyCode, "A ready, owned isolated workspace is required for a local commit.");

    public static Error LeaseNotActive() => Error.Conflict(
        LeaseNotActiveCode, "An active workspace lease is required for a local commit.");

    public static Error CheckpointNotCurrent() => Error.Conflict(
        CheckpointNotCurrentCode, "The selected checkpoint is not the workspace's current checkpoint.");

    public static Error ImplementationNotCurrent() => Error.Conflict(
        ImplementationNotCurrentCode, "The implemented report lineage is missing, malformed or superseded.");

    public static Error AgentApprovalMissing() => Error.Conflict(
        AgentApprovalMissingCode, "The selected CodeReviewer attempt has no valid approval for this checkpoint.");

    public static Error AgentApprovalNotLatest() => Error.Conflict(
        AgentApprovalNotLatestCode, "A newer implementation or review exists after the selected approval.");

    public static Error HumanApprovalMissing() => Error.Conflict(
        HumanApprovalMissingCode, "The selected human checkpoint approval was not found for this checkpoint.");

    public static Error HumanDecisionNotApproved() => Error.Conflict(
        HumanDecisionNotApprovedCode,
        "Every human decision for this checkpoint must be Approved and coherent; resolve mixed decisions with a new checkpoint.");

    public static Error VerificationNotCurrent() => Error.Conflict(
        VerificationNotCurrentCode,
        "Every enabled verification recipe needs a current Passed execution with an unchanged command and completion fingerprint.");

    public static Error MembershipMismatch() => Error.Conflict(
        MembershipMismatchCode,
        "The verification membership of the executions, the Agent review and the human approval must be exactly equal.");

    public static Error ActiveWork() => Error.Conflict(
        ActiveWorkCode, "An Agent attempt or verification execution is still active in this workspace.");

    public static Error ParentMismatch() => Error.Conflict(
        ParentMismatchCode, "The workspace head is not the expected recorded tip.");

    public static Error OperationConflict() => Error.Conflict(
        OperationConflictCode, "This run already has a different local-commit operation or request.");

    public static Error AuthorityChanged() => Error.Conflict(
        AuthorityChangedCode, "The approval authority changed while the local commit was being prepared; request it again.");

    public static Error Refused(LocalCommitPreparationOutcome outcome) => Error.Conflict(
        RefusedCodePrefix + ToSnake(outcome),
        outcome switch
        {
            LocalCommitPreparationOutcome.ConversionRefused =>
                "The approved bytes would be converted by a Git attribute or setting; normalize them in a new checkpoint.",
            LocalCommitPreparationOutcome.UnsafeSource =>
                "A changed file is not a physically proven single-name regular file inside the owned workspace.",
            LocalCommitPreparationOutcome.HostUnsupported => "This host cannot prove the source bytes for a local commit.",
            LocalCommitPreparationOutcome.TooManyPaths or LocalCommitPreparationOutcome.SourceTooLarge
                or LocalCommitPreparationOutcome.TotalTooLarge => "The change exceeds the local-commit bounds.",
            LocalCommitPreparationOutcome.CheckpointNotCurrent or LocalCommitPreparationOutcome.SourceChanged =>
                "The workspace source no longer matches the approved checkpoint.",
            LocalCommitPreparationOutcome.IdentityUnavailable =>
                "A valid local Git author identity is not configured for this repository.",
            _ => "The approved change cannot be committed by the host as configured.",
        });

    private static string ToSnake(LocalCommitPreparationOutcome outcome) =>
        string.Concat(outcome.ToString().Select((character, index) =>
            index > 0 && char.IsUpper(character) ? "_" + char.ToLowerInvariant(character) : char.ToLowerInvariant(character).ToString()));
}
