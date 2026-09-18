namespace DevalCopilot.Api.Features.Runs.GetProcessAttemptOutput;

/// <summary><c>Truncated</c> is null exactly when genuinely unknown — an artifact recovered from
/// a host interruption.</summary>
public sealed record GetProcessAttemptOutputResponse(
    string Status, string Text, long NextOffset, long TotalLengthSoFar, bool IsFinal, bool? Truncated);
