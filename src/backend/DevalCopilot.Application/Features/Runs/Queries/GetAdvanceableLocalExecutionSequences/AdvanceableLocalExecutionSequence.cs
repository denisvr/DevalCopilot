using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAdvanceableLocalExecutionSequences;

public sealed record AdvanceableLocalExecutionSequence(
    Guid SequenceId, Guid RunId, Guid ProjectId, LocalExecutionSequenceState State, long CoordinationNumber = 0);
