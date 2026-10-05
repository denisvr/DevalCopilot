using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>One claimed attempt as the hosted scenarios need to see it.</summary>
public sealed record HostedAttemptView(
    Attempt Attempt, int CompletionEvents, int Messages)
{
    public AttemptStatus Status => Attempt.Status;
}
