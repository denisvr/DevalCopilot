using DevalCopilot.Application.Features.Projects.Ports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.AgentSupervisions;

/// <summary>What one real supervisor composition provides to the shared observation scenarios (ADR-0056): how it builds its own provider
/// over the class's own database, adapter double and artifact store, how it seeds one eligible attempt through the production commands,
/// and how it creates the production supervisor. The scenarios themselves never touch a provider, a process or Git.</summary>
internal sealed class AgentSupervisionHostedComposition
{
    public required Func<GateableEvidenceReader, Action<ServiceCollection>, ServiceProvider> Build { get; init; }

    public required Func<ServiceProvider, GateableEvidenceReader, Task<(Guid RunId, Guid AttemptId)>> Seed { get; init; }

    public required Func<ServiceProvider, IHostedService> CreateSupervisor { get; init; }

    public required Func<ServiceProvider, object> AdapterOf { get; init; }

    public required Func<string> DatabasePath { get; init; }

    /// <summary>The numbered-call results of the composition's evidence double (claim-time captures included).</summary>
    public Func<int, GitWorkspaceEvidenceResult> EvidenceForCall { get; init; } =
        _ => GateableEvidenceReader.Matching(new string('a', 64));

    /// <summary>Lets an adapter double that waits for a release run through instead of holding the attempt.</summary>
    public Action<object>? OpenAdapter { get; init; }

    /// <summary>A double that holds the adapter call until it is released, for compositions that have one.</summary>
    public AdapterHold? Hold { get; init; }
}
