using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;

/// <summary>What a script sees about the step it is deciding the result of: the step's fixed kind, pass, recipe position and the
/// operation identity its claim committed.</summary>
public sealed record LocalExecutionStepContext(
    LocalExecutionStepKind Kind, int PassNumber, int? RecipeOrdinal, int Ordinal, Guid OperationId, int FindingCount);
