namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionSequences;

/// <summary>One verification recipe invocation the double has seen: its recipe number (from the pinned argument), the 1-based call count of
/// that recipe and the overall order.</summary>
internal sealed record LocalExecutionSequenceVerificationCall(int RecipeNumber, int RecipeCall, int Order, DateTimeOffset At);
