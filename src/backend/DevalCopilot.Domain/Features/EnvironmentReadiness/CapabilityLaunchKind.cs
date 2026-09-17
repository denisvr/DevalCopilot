namespace DevalCopilot.Domain.Features.EnvironmentReadiness;

/// <summary>
/// How a capability's last successfully observed executable is launched. Closed to exactly the
/// two shapes the process-execution boundary supports without a shell: a native executable, or
/// a JavaScript entrypoint run by a direct, fully qualified Node executable. Never an arbitrary
/// command string or an arbitrary prefix argument list.
/// </summary>
public enum CapabilityLaunchKind
{
    DirectExecutable = 0,
    NodeScript = 1,
}
