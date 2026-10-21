namespace DevalCopilot.Api.Features.Runs.GetLocalExecutionSequence;

/// <summary>The exact final references, present only when proved from durable identity: an approved cycle names its linked report and
/// approving review attempt, an escalated cycle names its linked report and escalation message. They grant no approval or commit.</summary>
public sealed record LocalExecutionSequenceFinalResponse(Guid ReportMessageId, Guid? ReviewAttemptId, Guid? EscalationMessageId);
