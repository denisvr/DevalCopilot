namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;

/// <summary>The exact validated report and result checkpoint of one linked Implemented or CorrectionApplied attempt. Only these may
/// advance the current source of a local execution sequence.</summary>
internal sealed record LocalExecutionAgentResult(Guid ReportMessageId, Guid CheckpointId, string FingerprintSha256);
