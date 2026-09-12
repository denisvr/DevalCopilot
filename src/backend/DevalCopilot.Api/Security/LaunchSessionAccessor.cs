using System.Security.Cryptography;
using DevalCopilot.Application.Security.Ports;

namespace DevalCopilot.Api.Security;

/// <summary>
/// Holds the per-launch bearer secret for the lifetime of this host process. When
/// configuration does not supply one (the normal production path once the Tauri shell
/// exists), a cryptographically random secret is generated once and never surfaced
/// through any endpoint, log, or console output.
/// </summary>
public sealed class LaunchSessionAccessor : ILaunchSessionAccessor
{
    public LaunchSessionAccessor(IConfiguration configuration)
    {
        var configured = configuration["LaunchSession:Secret"];
        Secret = string.IsNullOrEmpty(configured) ? RandomNumberGenerator.GetHexString(64) : configured;
    }

    public string Secret { get; }
}
