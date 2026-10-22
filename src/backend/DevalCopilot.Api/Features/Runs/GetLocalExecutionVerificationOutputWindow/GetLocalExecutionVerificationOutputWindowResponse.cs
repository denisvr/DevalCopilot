using DevalCopilot.Api.Features.Runs.GetAgentAttemptVerificationEvidence;

namespace DevalCopilot.Api.Features.Runs.GetLocalExecutionVerificationOutputWindow;

/// <summary>
/// One bounded window of one sealed verification output stream of a recorded step (ADR-0061). <c>Stream</c> is the canonical <c>stdout</c>
/// or <c>stderr</c>. <c>ExecutionId</c> is non-null only when the selected actual association was proved, and is always set for <c>Ok</c>.
/// <c>Status</c> is <c>Ok</c> (the Recorded <c>Artifact</c> descriptor, verified text, the UTF-8 byte cursor after it and a
/// <c>TotalLength</c> equal to the descriptor byte length), or <c>Unavailable</c>, <c>NotRecorded</c>, <c>Missing</c> or
/// <c>IntegrityMismatch</c> (no artifact, empty text, the requested offset unchanged as <c>NextOffset</c> and a <c>TotalLength</c> of 0; no
/// observed file length or diagnostic). The text is bytes as the host decoded them, never HTML.
/// </summary>
public sealed record GetLocalExecutionVerificationOutputWindowResponse(
    Guid RunId,
    Guid SequenceId,
    Guid StepId,
    Guid? ExecutionId,
    string Stream,
    string Status,
    AgentAttemptVerificationStreamResponse? Artifact,
    string Text,
    long NextOffset,
    long TotalLength);
