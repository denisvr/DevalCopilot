namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionSequence;

/// <summary>The exact final references proved from durable identity (never a latest message or a loaded window): the linked report an
/// approved review or an escalation answered, the approving review attempt, or the escalation message.</summary>
public sealed record LocalExecutionSequenceFinalReferences(Guid ReportMessageId, Guid? ReviewAttemptId, Guid? EscalationMessageId);
