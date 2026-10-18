using DevalCopilot.Api.Features.Runs.GetAgentAttemptVerificationEvidence;

namespace DevalCopilot.Api.Features.Runs.GetAgentAttemptVerificationOutputWindow;

/// <summary>
/// One bounded window of one sealed verification output stream (ADR-0052). <c>Stream</c> is the canonical <c>stdout</c> or
/// <c>stderr</c>. <c>Status</c> is <c>Ok</c> (the Recorded <c>Artifact</c> descriptor, verified text, the UTF-8 byte cursor after it and
/// a <c>TotalLength</c> equal to the descriptor byte length), or <c>Unavailable</c>, <c>NotRecorded</c>, <c>Missing</c> or
/// <c>IntegrityMismatch</c> (no artifact, empty text, the requested offset unchanged as <c>NextOffset</c> and a <c>TotalLength</c> of 0;
/// no observed file length or diagnostic). The text is bytes as the host decoded them, never HTML.
/// </summary>
public sealed record GetAgentAttemptVerificationOutputWindowResponse(
    Guid RunId,
    Guid AttemptId,
    Guid ExecutionId,
    string Stream,
    string Status,
    AgentAttemptVerificationStreamResponse? Artifact,
    string Text,
    long NextOffset,
    long TotalLength);
