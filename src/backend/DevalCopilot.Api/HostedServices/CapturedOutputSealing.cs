using DevalCopilot.Domain.Features.Processes;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Api.HostedServices;

/// <summary>
/// ADR-0072: a captured stream's partial output file may be sealed only when the host proved its writer closed (any recorded
/// completion except <see cref="OutputCaptureCompletion.Unknown"/>). Without that proof, or without any recorded I/O fact, the
/// partial file is neither sealed nor deleted. A file the adapter wrote itself after the process (the final response) is not a
/// captured stream and is unaffected.
/// </summary>
internal static class CapturedOutputSealing
{
    public static bool MaySealAgentArtifact(ArtifactPurpose purpose, ProcessIoCompletion? ioCompletion) => purpose switch
    {
        ArtifactPurpose.AgentStandardOutput => ProcessIoCompletion.ProvesWriterClosed(ioCompletion?.StandardOutput),
        ArtifactPurpose.AgentStandardError => ProcessIoCompletion.ProvesWriterClosed(ioCompletion?.StandardError),
        _ => true,
    };
}
