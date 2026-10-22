using System.Globalization;
using DevalCopilot.Api.Features.Runs.GetLocalExecutionSequence;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionSequenceAvailability;

namespace DevalCopilot.Api.Features.Runs.GetLocalExecutionSequenceAvailability;

/// <summary>The one safe mapping of a recorded arming availability, shared by the Immediate and the queued operations (ADR-0060,
/// ADR-0062). An answer whose tuple is internally inconsistent is refused (null) instead of being shown.</summary>
internal static class GetLocalExecutionSequenceAvailabilityResponses
{
    public static GetLocalExecutionSequenceAvailabilityResponse? Map(GetLocalExecutionSequenceAvailabilityQueryResult value)
    {
        if (value.Available != (value.RefusalCode is null)
            || value.Available != (value.ConfirmationSha256 is not null)
            || value.Available != (value.Recipes is not null)
            || (value.Recipes is { } recipes && (recipes.Count is < 1 or > 32))
            || !Enum.IsDefined(value.AdmissionMode))
        {
            return null;
        }

        return new GetLocalExecutionSequenceAvailabilityResponse(
            value.RunId,
            value.PlanProposalMessageId,
            value.Available,
            value.RefusalCode,
            LocalExecutionSequenceText.RefusalText(value.RefusalCode),
            value.PlanAuthority?.ToString(),
            value.WorkspaceId,
            value.CheckpointId,
            value.FingerprintSha256,
            value.ConfirmationSha256,
            value.CorrectionAllowance,
            value.MaximumSteps,
            value.Recipes?
                .Select(recipe => new LocalExecutionSequenceAvailabilityRecipeResponse(
                    recipe.Ordinal, recipe.CommandId, recipe.CommandNumber, recipe.Name, recipe.TimeoutSeconds))
                .ToArray(),
            value.Disclosure is { } disclosure
                ? new LocalExecutionSequenceAvailabilityDisclosureResponse(
                    disclosure.CodexModel,
                    disclosure.CodexEffort,
                    disclosure.ClaudeModel,
                    disclosure.ClaudeEffort,
                    disclosure.ClaudeMaxTurns,
                    disclosure.CodexAccountUsageStopPercent,
                    disclosure.CodexTokenStopThreshold?.ToString(CultureInfo.InvariantCulture),
                    disclosure.ClaudeTokenStopThreshold?.ToString(CultureInfo.InvariantCulture))
                : null,
            value.AdmissionMode.ToString());
    }
}
