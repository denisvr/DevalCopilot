using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordImplementationResult;

public sealed record RecordImplementationResultCommandResult(AttemptStatus Status, AgentOutcome Outcome, long LatestEventSequence);
