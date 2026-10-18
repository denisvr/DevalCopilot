using DevalCopilot.Application.Features.Runs.Policies.AgentAttemptVerification;
using DevalCopilot.Domain.Features.Projects;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptVerificationOutputWindow;

/// <summary>
/// <see cref="AgentAttemptVerificationOutputWindowStatus.Ok"/> carries the Recorded descriptor, the verified text, the byte cursor after
/// it and a total length equal to the descriptor's admitted byte length. Every other status carries no descriptor, empty text, the
/// requested offset unchanged and a total length of 0.
/// </summary>
public sealed record GetAgentAttemptVerificationOutputWindowQueryResult(
    Guid RunId,
    Guid AttemptId,
    Guid ExecutionId,
    VerificationOutputPurpose Stream,
    AgentAttemptVerificationOutputWindowStatus Status,
    RecordedVerificationOutputDescriptor? Artifact,
    string Text,
    long NextOffset,
    long TotalLength)
{
    public static GetAgentAttemptVerificationOutputWindowQueryResult Refused(
        GetAgentAttemptVerificationOutputWindowQuery query, AgentAttemptVerificationOutputWindowStatus status) =>
        new(query.RunId, query.AttemptId, query.ExecutionId, query.Stream, status, null, string.Empty, query.FromOffset, 0);
}
