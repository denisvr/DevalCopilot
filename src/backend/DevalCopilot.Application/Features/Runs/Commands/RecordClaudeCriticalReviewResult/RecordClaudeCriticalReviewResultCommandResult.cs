using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordClaudeCriticalReviewResult;

public sealed record RecordClaudeCriticalReviewResultCommandResult(AttemptStatus Status, long LatestEventSequence);
