namespace DevalCopilot.Api.Features.Runs.GetAgentAttemptVerificationEvidence;

/// <summary>The immutable recorded identity of the inspected attempt. It carries no status, time, outcome, assignment, session or path.</summary>
/// <param name="AttemptNumber">The recorded, positive attempt number of its run.</param>
/// <param name="Role">The CodeReviewer role name.</param>
/// <param name="Provider">The provider name supported for that role.</param>
/// <param name="ResponseContract">ImplementationReview or VerificationDiagnosis.</param>
public sealed record AgentAttemptVerificationHeaderResponse(
    Guid Id, int AttemptNumber, string Role, string Provider, string ResponseContract);
