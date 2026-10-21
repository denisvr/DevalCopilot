namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionSequenceAvailability;

/// <summary>One recipe of the pinned set as disclosed for confirmation: identifier, number, safe name and timeout. Never the raw
/// executable, arguments or a path.</summary>
public sealed record LocalExecutionSequenceAvailabilityRecipe(int Ordinal, Guid CommandId, int CommandNumber, string Name, int TimeoutSeconds);
