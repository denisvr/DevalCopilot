using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Infrastructure.Features.Projects;

namespace DevalCopilot.Api.IntegrationTests;

/// <summary>
/// ADR-0072: a deterministic verification environment for hosted tests whose process adapter is itself a double: every preparation is
/// available with the real profile version and an empty map, and nothing is created on disk. <see cref="Unavailable"/> makes every
/// preparation unavailable instead.
/// </summary>
internal sealed class FixedVerificationEnvironmentProvider : IVerificationEnvironmentProvider
{
    public bool Unavailable { get; set; }

    public List<Guid> Released { get; } = [];

    public VerificationEnvironmentPreparation Prepare(Guid verificationExecutionId, string executablePath, IReadOnlyList<string> arguments) =>
        Unavailable
            ? VerificationEnvironmentPreparation.Unavailable(WindowsVerificationEnvironmentProvider.ProfileVersion)
            : new VerificationEnvironmentPreparation(true, WindowsVerificationEnvironmentProvider.ProfileVersion, new Dictionary<string, string>());

    public void ReleaseScratch(Guid verificationExecutionId)
    {
        lock (Released)
        {
            Released.Add(verificationExecutionId);
        }
    }
}
