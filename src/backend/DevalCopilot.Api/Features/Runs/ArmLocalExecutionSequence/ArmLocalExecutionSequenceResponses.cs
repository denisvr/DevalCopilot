using System.Globalization;
using DevalCopilot.Api.Features.Runs.GetLocalExecutionSequence;
using DevalCopilot.Application.Features.Runs.Commands.ArmLocalExecutionSequence;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Api.Features.Runs.ArmLocalExecutionSequence;

/// <summary>The one safe mapping of an arming receipt, shared by the Immediate and the queued operations (ADR-0060, ADR-0062). A receipt
/// whose mode and queue disclosure disagree is refused (null) instead of being shown.</summary>
internal static class ArmLocalExecutionSequenceResponses
{
    public static ArmLocalExecutionSequenceResponse? Map(ArmLocalExecutionSequenceCommandResult value)
    {
        if (!Enum.IsDefined(value.AdmissionMode)
            || !LocalExecutionQueueMapping.TryMap(value.Queue, out var queue)
            || (value.Queue is not null && value.AdmissionMode != LocalExecutionAdmissionMode.WaitForAdmission))
        {
            return null;
        }

        return new ArmLocalExecutionSequenceResponse(
            value.SequenceId,
            value.RunId,
            value.PlanProposalMessageId,
            value.WorkspaceId,
            value.CheckpointId,
            value.FingerprintSha256,
            value.ConfirmationSha256,
            value.State.ToString(),
            value.RecipeCount,
            value.CorrectionAllowance,
            value.ArmedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            value.AdvancementDeadlineUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            value.Revision.ToString(CultureInfo.InvariantCulture),
            value.Replayed,
            value.AdmissionMode.ToString(),
            queue);
    }
}
