namespace DevalCopilot.Application.Features.Runs.Ports;

public sealed record LocalCommitPreparationResult(LocalCommitPreparationOutcome Outcome, LocalCommitPreparedFacts? Facts);
