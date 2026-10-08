using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalCommit;

/// <summary>One enabled recipe with the latest Passed execution that currently qualifies, and the digest that binds both.</summary>
public sealed record LocalCommitVerificationMember(
    int Sequence, VerificationCommand Command, VerificationExecution Execution, string Digest);

/// <summary>One human decision row of the checkpoint with the digest of its decision and evidence.</summary>
public sealed record LocalCommitHumanDecision(CheckpointReview Review, string Digest);

/// <summary>
/// Everything one fresh, untracked read proved about a run's local-commit authority. <see cref="AuthoritySha256"/> binds the
/// complete identity, including the whole human decision set and the verification members, and is compared again at every seam.
/// </summary>
public sealed record LocalCommitAuthority(
    Run Run,
    string ProjectCanonicalPath,
    GitWorkspace Workspace,
    RepositoryMutationLease Lease,
    GitCheckpoint Checkpoint,
    IReadOnlyList<GitChangedFile> ChangedFiles,
    Attempt ReviewAttempt,
    CollaborationMessage ApprovalMessage,
    CollaborationMessage ExecutionReport,
    CheckpointReview AgentReview,
    CheckpointReview HumanReview,
    IReadOnlyList<LocalCommitHumanDecision> HumanDecisions,
    IReadOnlyList<LocalCommitVerificationMember> Verification,
    string ExpectedParentCommitSha,
    string AuthoritySha256);
