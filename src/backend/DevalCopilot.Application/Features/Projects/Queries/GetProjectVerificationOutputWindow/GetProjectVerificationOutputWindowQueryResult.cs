using DevalCopilot.Application.Features.Runs.Policies.AgentAttemptVerification;
using DevalCopilot.Domain.Features.Projects;

namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectVerificationOutputWindow;

/// <summary>
/// <see cref="ProjectVerificationOutputWindowStatus.Ok"/> carries the Recorded descriptor, the verified text, the byte cursor after it and a
/// total length equal to the descriptor's admitted byte length. Every other status carries no descriptor, empty text, the requested
/// offset unchanged and a total length of 0.
/// </summary>
public sealed record GetProjectVerificationOutputWindowQueryResult(
    Guid ProjectId,
    Guid ExecutionId,
    VerificationOutputPurpose Stream,
    ProjectVerificationOutputWindowStatus Status,
    RecordedVerificationOutputDescriptor? Artifact,
    string Text,
    long NextOffset,
    long TotalLength)
{
    public static GetProjectVerificationOutputWindowQueryResult Refused(
        GetProjectVerificationOutputWindowQuery query, ProjectVerificationOutputWindowStatus status) =>
        new(query.ProjectId, query.ExecutionId, query.Stream, status, null, string.Empty, query.FromOffset, 0);
}
