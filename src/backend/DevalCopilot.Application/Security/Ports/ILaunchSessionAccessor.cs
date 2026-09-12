namespace DevalCopilot.Application.Security.Ports;

/// <summary>
/// Exposes the per-launch bearer secret the host was bootstrapped with. In production
/// the Tauri shell supplies it over stdin; a test harness supplies it through in-memory
/// configuration. Neither path ever logs, echoes, or otherwise persists the value.
/// </summary>
public interface ILaunchSessionAccessor
{
    string Secret { get; }
}
