namespace DevalCopilot.Application.Features.Projects.Ports;

/// <summary>
/// ADR-0072: builds the one fixed, versioned verification environment for a dispatched verification execution: explicit system
/// keys derived from the system boundary, a PATH built only from validated selected tool directories and the system directory, and
/// per-execution owned profile, temporary, application-data, cache and configuration locations. It never copies the host's ambient
/// environment, credentials or profile configuration, and has no fallback: a missing or unvalidated prerequisite is
/// <see cref="VerificationEnvironmentPreparation.Unavailable"/> so the execution completes truthfully instead of looping.
/// </summary>
public interface IVerificationEnvironmentProvider
{
    VerificationEnvironmentPreparation Prepare(Guid verificationExecutionId, string executablePath, IReadOnlyList<string> arguments);

    /// <summary>Removes an execution's owned scratch only when the caller has proved its process exited and every stream
    /// completed; a removal failure is ignored and leaves the scratch in place.</summary>
    void ReleaseScratch(Guid verificationExecutionId);
}
