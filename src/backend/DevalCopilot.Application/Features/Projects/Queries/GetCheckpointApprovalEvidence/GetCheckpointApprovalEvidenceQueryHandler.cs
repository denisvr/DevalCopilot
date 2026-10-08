using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Projects.Policies;
using DevalCopilot.Application.Features.Projects.Ports;

namespace DevalCopilot.Application.Features.Projects.Queries.GetCheckpointApprovalEvidence;

public sealed class GetCheckpointApprovalEvidenceQueryHandler(
    IDevalCopilotDbContext dbContext,
    IGitWorkspaceEvidenceReader evidenceReader)
    : IQueryHandler<GetCheckpointApprovalEvidenceQuery, Result<CheckpointApprovalEvidenceQueryResult>>
{
    public const string NoEnabledRecipesCode = "approval_evidence.no_enabled_recipes";
    public const string TooManyRecipesCode = "approval_evidence.too_many_recipes";
    public const string IncompleteCode = "approval_evidence.verification_incomplete";

    /// <summary>Untracked reads decide the source, the bounded Git observation then confirms its fingerprint outside any
    /// transaction, and the source and the complete verification set are read again afterwards, so the returned bundle is the set that
    /// was current after the observation. It is a point-in-time read and no authority: the review command re-reads and decides
    /// independently.</summary>
    public async Task<Result<CheckpointApprovalEvidenceQueryResult>> HandleAsync(
        GetCheckpointApprovalEvidenceQuery query, CancellationToken cancellationToken)
    {
        var early = await CheckpointReviewSource.ReadAsync(dbContext, query.ProjectId, query.CheckpointId, cancellationToken);
        if (early.Source is not { } observed)
        {
            return Failure(early.Error!);
        }

        var evidence = await evidenceReader.CaptureAsync(observed.WorkspacePath, cancellationToken);
        if (evidence.Outcome != GitWorkspaceEvidenceOutcome.Success || evidence.FingerprintSha256 != observed.FingerprintSha256)
        {
            return Failure(CheckpointReviewSource.CheckpointNotCurrent());
        }

        var fresh = await CheckpointReviewSource.ReadAsync(dbContext, query.ProjectId, query.CheckpointId, cancellationToken);
        if (fresh.Error is { } freshError)
        {
            return Failure(freshError);
        }

        if (fresh.Source != observed)
        {
            return Failure(CheckpointReviewSource.CheckpointNotCurrent());
        }

        var set = await CompleteVerificationSet.ReadAsync(dbContext, observed, cancellationToken);
        if (set.Refusal is { } refusal)
        {
            return Failure(refusal switch
            {
                CompleteVerificationSet.Refusal.NoEnabledRecipes => Error.Conflict(
                    NoEnabledRecipesCode, "At least one enabled verification recipe is required to approve the checkpoint."),
                CompleteVerificationSet.Refusal.TooManyRecipes => Error.Conflict(
                    TooManyRecipesCode, "More than 32 verification recipes are enabled, so no complete approval bundle can be offered."),
                _ => Error.Conflict(
                    IncompleteCode,
                    "Every enabled verification recipe needs a latest passed execution for the current checkpoint before it can be approved."),
            });
        }

        return Result<CheckpointApprovalEvidenceQueryResult>.Success(new CheckpointApprovalEvidenceQueryResult(
            observed.ProjectId,
            observed.WorkspaceId,
            observed.CheckpointId,
            observed.CheckpointNumber,
            observed.FingerprintSha256,
            set.Members.Select(member => new CheckpointApprovalEvidenceMemberQueryResult(
                member.Command.Id,
                member.Command.CommandNumber,
                member.Command.Name,
                member.Execution.Id,
                member.Execution.ExecutionNumber)).ToArray()));
    }

    private static Result<CheckpointApprovalEvidenceQueryResult> Failure(Error error) =>
        Result<CheckpointApprovalEvidenceQueryResult>.Failure(error);
}
