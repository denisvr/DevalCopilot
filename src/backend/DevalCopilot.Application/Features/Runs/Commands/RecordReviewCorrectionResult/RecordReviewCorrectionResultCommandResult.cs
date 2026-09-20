using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordReviewCorrectionResult;

public sealed record RecordReviewCorrectionResultCommandResult(AttemptStatus Status, AgentOutcome Outcome, long LatestEventSequence);
