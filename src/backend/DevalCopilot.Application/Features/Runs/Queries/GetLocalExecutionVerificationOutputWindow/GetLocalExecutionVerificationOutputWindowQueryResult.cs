using DevalCopilot.Application.Features.Runs.Policies.AgentAttemptVerification;
using DevalCopilot.Domain.Features.Projects;

namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionVerificationOutputWindow;

/// <summary>
/// <see cref="LocalExecutionVerificationOutputWindowStatus.Ok"/> carries the proved execution, the Recorded descriptor, the verified text,
/// the byte cursor after it and a total length equal to the descriptor's admitted byte length. Every other status carries no descriptor,
/// empty text, the requested offset unchanged and a total length of 0. <see cref="ExecutionId"/> is null unless the selected actual
/// association was proved; an unproved association never substitutes the reserved identity.
/// </summary>
public sealed record GetLocalExecutionVerificationOutputWindowQueryResult(
    Guid RunId,
    Guid SequenceId,
    Guid StepId,
    Guid? ExecutionId,
    VerificationOutputPurpose Stream,
    LocalExecutionVerificationOutputWindowStatus Status,
    RecordedVerificationOutputDescriptor? Artifact,
    string Text,
    long NextOffset,
    long TotalLength)
{
    public static GetLocalExecutionVerificationOutputWindowQueryResult Refused(
        GetLocalExecutionVerificationOutputWindowQuery query,
        Guid sequenceId,
        Guid? executionId,
        LocalExecutionVerificationOutputWindowStatus status) =>
        new(query.RunId, sequenceId, query.StepId, executionId, query.Stream, status, null, string.Empty, query.FromOffset, 0);
}
