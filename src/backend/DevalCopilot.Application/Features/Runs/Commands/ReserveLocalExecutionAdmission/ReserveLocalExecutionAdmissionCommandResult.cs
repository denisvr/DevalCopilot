using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;

namespace DevalCopilot.Application.Features.Runs.Commands.ReserveLocalExecutionAdmission;

/// <summary>The one reserved step of this call, or none. The ticket is the only authority to try that step's claim, once.</summary>
public sealed record ReserveLocalExecutionAdmissionCommandResult(Guid? RunId, LocalExecutionStepTicket? ReservedStep);
