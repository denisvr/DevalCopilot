using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordAgentAttemptResult;

public sealed record RecordAgentAttemptResultCommandResult(AttemptStatus Status, long LatestEventSequence);
