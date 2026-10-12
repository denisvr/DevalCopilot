namespace DevalCopilot.Api.Features.Runs.GetCollaborationHistoryAttempt;

/// <summary>The minimal recorded header of the Agent attempt a provider-observed message references. It states recorded attribution only:
/// never current execution, an outcome, authority, provider capability or sealed evidence, and no model, effort, profile, session,
/// path, argument, checkpoint, artifact, token or account text.</summary>
/// <param name="AttemptNumber">The recorded, positive attempt number of its run.</param>
/// <param name="Role">A supported Agent role name.</param>
/// <param name="Provider">A supported provider name.</param>
/// <param name="ResponseContract">The response contract name that belongs to the role.</param>
/// <param name="Status">The recorded attempt status (Running, Completed, Failed or Interrupted).</param>
/// <param name="ClaimedAtUtc">The recorded claim time.</param>
/// <param name="DispatchedAtUtc">The recorded dispatch time; null when none was recorded.</param>
/// <param name="CompletedAtUtc">The recorded completion time; null when none was recorded.</param>
public sealed record CollaborationAttemptHeaderResponse(
    Guid Id,
    int AttemptNumber,
    string Role,
    string Provider,
    string ResponseContract,
    string Status,
    DateTimeOffset ClaimedAtUtc,
    DateTimeOffset? DispatchedAtUtc,
    DateTimeOffset? CompletedAtUtc);
