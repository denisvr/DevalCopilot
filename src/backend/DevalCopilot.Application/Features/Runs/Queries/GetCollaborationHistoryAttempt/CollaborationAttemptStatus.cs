namespace DevalCopilot.Application.Features.Runs.Queries.GetCollaborationHistoryAttempt;

/// <summary>The recorded Agent-attempt attribution of one collaboration message. <see cref="NoAgentAttempt"/> says only that, under
/// this contract, a recognized message that was not provider-observed attributes no Agent attempt (a simulated message can still have
/// a simulated attempt); it is not absence of every attempt. <see cref="Unavailable"/> covers every stored shape that cannot be shown
/// as a coherent attribution and discloses no target.</summary>
public enum CollaborationAttemptStatus
{
    NoAgentAttempt,
    Unavailable,
    Available,
}
