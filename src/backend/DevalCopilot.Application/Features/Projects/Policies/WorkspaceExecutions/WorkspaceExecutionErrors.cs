using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Projects.Policies.WorkspaceExecutions;

/// <summary>Stable, non-echoing refusals of the workspace execution exclusion (ADR-0057). None carries a path, an identifier of other
/// work, a stored value or an exception, and none is a workspace loss, a provider failure or a terminal Agent outcome.</summary>
public static class WorkspaceExecutionErrors
{
    public const string AgentAttemptActiveCode = "workspace_execution.agent_attempt_active";
    public const string VerificationActiveCode = "workspace_execution.verification_active";
    public const string OwnersConflictedCode = "workspace_execution.owners_conflicted";
    public const string PreparationReservedCode = "workspace_execution.preparation_reserved";
    public const string UnavailableCode = "workspace_execution.unavailable";
    public const string BodyNotAllowedCode = "workspace_execution.body_not_allowed";
    public const string QueryNotAllowedCode = "workspace_execution.query_not_allowed";

    public static Error AgentAttemptActive() => Error.Conflict(
        AgentAttemptActiveCode, "An Agent attempt is already active in this project's workspace; wait for it to finish.");

    public static Error VerificationActive() => Error.Conflict(
        VerificationActiveCode, "A verification execution is already active in this project's workspace; wait for it to finish.");

    public static Error OwnersConflicted() => Error.Conflict(
        OwnersConflictedCode, "More than one execution is recorded as active in this project's workspace; it needs inspection.");

    public static Error PreparationReserved() => Error.Conflict(
        PreparationReservedCode, "A queued local execution cycle has reserved this project's workspace for its next step preparation; wait for it to start or end.");

    public static Error Unavailable() => Error.Conflict(
        UnavailableCode, "Who may execute in this project's workspace cannot be judged safely; inspect the workspace.");

    public static Error BodyNotAllowed() => new(
        BodyNotAllowedCode, "This request accepts no request body.", ErrorType.Validation);

    public static Error QueryNotAllowed() => new(
        QueryNotAllowedCode, "This request accepts no query string.", ErrorType.Validation);
}
