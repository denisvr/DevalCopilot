namespace DevalCopilot.Api.Features.Runs.GetLocalExecutionSequenceAvailability;

/// <summary>One recipe of the pinned set as disclosed for confirmation: identifier, number, safe name and timeout. The name is untrusted
/// text and is rendered inert; the raw executable, arguments and any path are never exposed.</summary>
public sealed record LocalExecutionSequenceAvailabilityRecipeResponse(int Ordinal, Guid CommandId, int CommandNumber, string Name, int TimeoutSeconds);
