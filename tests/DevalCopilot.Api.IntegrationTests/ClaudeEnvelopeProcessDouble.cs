using System.Text.Json;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptEvidence;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests;

/// <summary>
/// A deterministic provider process for the REAL Claude adapters: it records every request it is asked to execute and answers
/// with one fixed <c>--output-format json</c> envelope as a clean exit. Used by the hosted tests that run the actual
/// supervisors, adapters, mediator and recording against a file-backed SQLite database. No provider is ever started.
/// </summary>
internal sealed class ClaudeEnvelopeProcessDouble(string standardOutput) : IProcessExecutionAdapter
{
    internal const string ValidUsage =
        "\"usage\":{\"input_tokens\":1200,\"output_tokens\":345,\"cache_creation_input_tokens\":67,\"cache_read_input_tokens\":890}";

    /// <summary>Two models listed in the reverse of their ordinal order, so the recorded order is provably the project's.</summary>
    internal const string TwoModelUsage =
        "\"modelUsage\":{\"claude-hosted-b\":{\"inputTokens\":1,\"costUSD\":0.5,\"contextWindow\":1000000,\"maxOutputTokens\":64000},"
        + "\"claude-hosted-a\":{\"contextWindow\":200000,\"maxOutputTokens\":32000}}";

    /// <summary>The exact canonical text the project persists for <see cref="TwoModelUsage"/>, written independently of the
    /// production serializer.</summary>
    internal const string TwoModelSnapshot =
        "{\"version\":1,\"source\":\"claude-cli-model-usage-v1\",\"models\":[{\"modelId\":\"claude-hosted-a\",\"contextWindowTokens\":200000,"
        + "\"maxOutputTokens\":32000},{\"modelId\":\"claude-hosted-b\",\"contextWindowTokens\":1000000,\"maxOutputTokens\":64000}]}";

    internal const string MalformedModelUsage =
        "\"modelUsage\":{\"claude-hosted-a\":{\"contextWindow\":200000,\"maxOutputTokens\":32000,\"maxOutputTokens\":32000}}";

    public List<ProcessExecutionRequest> Requests { get; } = [];

    internal static string Envelope(string result, params string[] members) => Envelope(false, result, members);

    internal static string Envelope(bool isError, string result, params string[] members) =>
        "{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":" + (isError ? "true" : "false")
        + ",\"result\":" + JsonSerializer.Serialize(result) + ",\"session_id\":\"hosted-session\""
        + string.Concat(members.Select(member => "," + member)) + "}";

    public Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(new ProcessExecutionResult
        {
            Outcome = ProcessExecutionOutcome.Exited,
            ExitCode = 0,
            StandardOutput = standardOutput,
            StandardOutputTruncated = false,
            StandardError = string.Empty,
            StandardErrorTruncated = false,
            Duration = TimeSpan.FromMilliseconds(5),
        });
    }

    /// <summary>A real file at a fully qualified path, which the real adapters require of a launch component.</summary>
    internal static string CreateLaunchFile(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"fake-claude-{Guid.NewGuid():N}.exe");
        File.WriteAllText(path, string.Empty);
        return path;
    }

    /// <summary>What the recorded attempt says, read back through the production evidence query rather than the row.</summary>
    internal static async Task<GetAgentAttemptEvidenceQueryResult> ReadEvidenceAsync(
        Devalente.Shared.Cqrs.IApplicationMediator mediator, Guid runId, Guid attemptId)
    {
        var result = await mediator.SendAsync(new GetAgentAttemptEvidenceQuery(runId, attemptId), CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    internal static void AssertTwoModels(AgentModelContextLimitsEvidence? evidence)
    {
        Assert.NotNull(evidence);
        Assert.Equal(
            [new AgentModelContextLimit("claude-hosted-a", 200000, 32000), new AgentModelContextLimit("claude-hosted-b", 1000000, 64000)],
            evidence.Models.AsEnumerable());
    }
}
