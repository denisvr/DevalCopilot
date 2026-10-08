namespace DevalCopilot.Application.Features.Runs.Ports;

/// <summary><paramref name="ReasonCode"/> is a fixed, path-free code. <paramref name="SourceConsistent"/> reports whether the
/// working files still equal the delivered tree after promotion; an edit after the last observation never changes the commit but
/// means the workspace cannot be labeled clean.</summary>
public sealed record LocalCommitExecutionResult(LocalCommitExecutionOutcome Outcome, string ReasonCode, bool SourceConsistent);
