using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordImplementationReviewResult;

public sealed record RecordImplementationReviewResultCommandResult(AttemptStatus Status, long LatestEventSequence);
