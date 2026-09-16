namespace DevalCopilot.Application.Features.Projects.Queries.GetVerificationExecutionOutput;

public sealed record VerificationExecutionOutputQueryResult(string Text, long NextOffset, long TotalLength, bool IsFinal);
