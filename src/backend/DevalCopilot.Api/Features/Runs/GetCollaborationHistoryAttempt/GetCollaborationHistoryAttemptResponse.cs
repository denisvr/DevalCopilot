namespace DevalCopilot.Api.Features.Runs.GetCollaborationHistoryAttempt;

/// <summary>The recorded Agent-attempt attribution of one collaboration message. <c>SourceSequence</c> is the source's exact sequence as a
/// canonical decimal string. <c>Status</c> is <c>NoAgentAttempt</c> (a recognized message that was not provider-observed attributes no
/// Agent attempt under this contract; it is not absence of every attempt), <c>Unavailable</c> (the stored reference cannot be shown as a
/// coherent attribution; no target) or <c>Available</c>. <c>Attempt</c> is non-null exactly when the status is <c>Available</c>.</summary>
public sealed record GetCollaborationHistoryAttemptResponse(
    Guid RunId, Guid MessageId, string SourceSequence, string Status, CollaborationAttemptHeaderResponse? Attempt);
