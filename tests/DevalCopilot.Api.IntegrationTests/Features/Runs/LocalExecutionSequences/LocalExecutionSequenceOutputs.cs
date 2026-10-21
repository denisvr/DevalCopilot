using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionSequences;

/// <summary>Writes the three partial output files a provider adapter leaves behind for the supervisor to seal.</summary>
internal static class LocalExecutionSequenceOutputs
{
    public static async Task WriteAsync(
        IArtifactStore artifactStore, Guid runId, Guid attemptId, string finalResponse, CancellationToken cancellationToken)
    {
        foreach (var (purpose, content) in new[]
                 {
                     (ArtifactPurpose.AgentFinalResponse, finalResponse),
                     (ArtifactPurpose.AgentStandardOutput, string.Empty),
                     (ArtifactPurpose.AgentStandardError, string.Empty),
                 })
        {
            var path = artifactStore.GetPartialPath(runId, attemptId, purpose);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, content, cancellationToken);
        }
    }
}
