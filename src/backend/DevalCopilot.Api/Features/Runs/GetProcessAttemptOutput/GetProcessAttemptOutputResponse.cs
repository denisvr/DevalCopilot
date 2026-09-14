namespace DevalCopilot.Api.Features.Runs.GetProcessAttemptOutput;

public sealed record GetProcessAttemptOutputResponse(
    string Status, string Text, long NextOffset, long TotalLengthSoFar, bool IsFinal, bool Truncated);
