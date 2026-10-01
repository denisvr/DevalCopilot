using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Features.Runs;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Runs;

/// <summary>
/// The actual Claude implementation adapter against a deterministic process double: the sealed manifest (verified
/// through the existing sealed-read boundary) must agree exactly with the attempt's durable human plan-authorization
/// facts carried by the request (ADR-0016), or the adapter fails closed before any process starts. On agreement the exact
/// sealed bytes reach stdin and the arguments are identical to an ordinary implementation invocation: no flag, tool,
/// permission, schema, or contract-version change. The double never starts a real provider.
/// </summary>
public sealed class PlanningAuthorizationAdapterTests : IDisposable
{
    private const string Rationale = "SENTINEL-AUTH reviewed and accepted by the owner.";

    private static readonly PlanningImplementationAuthorizationFact Fact = new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Rationale);

    private readonly string _artifactRoot = Path.Combine(Path.GetTempPath(), $"devalcopilot-plan-auth-artifacts-{Guid.NewGuid():N}");
    private readonly string _workspacePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-plan-auth-workspace-{Guid.NewGuid():N}");
    private readonly FilesystemArtifactStore _artifactStore;

    public PlanningAuthorizationAdapterTests()
    {
        _artifactStore = new FilesystemArtifactStore(_artifactRoot);
        Directory.CreateDirectory(_workspacePath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_artifactRoot))
        {
            Directory.Delete(_artifactRoot, recursive: true);
        }

        if (Directory.Exists(_workspacePath))
        {
            Directory.Delete(_workspacePath, recursive: true);
        }
    }

    private sealed record Invocation(bool Failed, int Starts, ProcessExecutionRequest? Request);

    private static string Ordinary() => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["protocolVersion"] = "1.0",
        ["objective"] = "Objective",
        ["untrustedEvidenceBoundary"] = "Untrusted below.",
        ["resolvedPlan"] = new
        {
            proposalMessageId = Fact.FinalProposalMessageId,
            resolutionEvidence = new { form = "resolvedRevisedProposal", decisions = Array.Empty<object>() },
        },
        ["changeEvidence"] = new { files = new[] { "src/A.cs" } },
    });

    private static string Authorized(Action<JsonObject>? tamper = null)
    {
        var node = new JsonObject
        {
            ["protocolVersion"] = "1.0",
            ["objective"] = "Objective",
            ["humanPlanAuthorizationBoundary"] = PlanningImplementationAuthorizationManifest.Boundary,
            ["untrustedEvidenceBoundary"] = "Untrusted below.",
            ["resolvedPlan"] = new JsonObject
            {
                ["proposalMessageId"] = Fact.FinalProposalMessageId,
                ["resolutionEvidence"] = new JsonObject
                {
                    ["form"] = PlanningImplementationAuthorizationManifest.FormName,
                    ["decisions"] = new JsonArray(),
                    ["humanAuthorization"] = new JsonObject
                    {
                        ["authorizationId"] = Fact.AuthorizationId,
                        ["escalationMessageId"] = Fact.EscalationMessageId,
                        ["humanInstructionMessageId"] = Fact.HumanInstructionMessageId,
                        ["instruction"] = PlanningImplementationInstruction.FixedInstruction,
                        ["rationale"] = Rationale,
                    },
                },
            },
            ["changeEvidence"] = new JsonObject { ["files"] = new JsonArray("src/A.cs") },
        };
        tamper?.Invoke(node);
        return node.ToJsonString();
    }

    private async Task<Invocation> InvokeAsync(string manifestText, PlanningImplementationAuthorizationFact? expected)
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var partialPath = _artifactStore.GetPartialPath(runId, attemptId, ArtifactPurpose.AgentContextManifest);
        Directory.CreateDirectory(Path.GetDirectoryName(partialPath)!);
        await File.WriteAllBytesAsync(partialPath, Encoding.UTF8.GetBytes(manifestText));
        var sealedFile = (await _artifactStore.SealAsync(runId, attemptId, ArtifactPurpose.AgentContextManifest, CancellationToken.None))!;
        var executablePath = Path.Combine(_workspacePath, $"fake-claude-{Guid.NewGuid():N}.exe");
        await File.WriteAllTextAsync(executablePath, string.Empty);
        var fake = new CountingProcessExecutionAdapter();

        var result = await new ClaudeImplementationAdapter(fake, _artifactStore).InvokeAsync(
            new ImplementationInvocationRequest(
                runId, attemptId, _workspacePath, sealedFile.RelativeStoragePath, sealedFile.ByteLength, sealedFile.ContentHash,
                executablePath, TimeSpan.FromMinutes(20), 65536, 131072, "opus", "high", 7, ClaudeMutationAdapterContract.ImplementationV2,
                null, expected),
            CancellationToken.None);

        return new Invocation(result.Outcome == ImplementationInvocationOutcome.Failed, fake.Starts, fake.Request);
    }

    [Fact]
    public async Task A_matching_authorized_manifest_reaches_stdin_with_arguments_identical_to_an_ordinary_invocation()
    {
        var manifest = Authorized();

        var authorized = await InvokeAsync(manifest, Fact);
        var ordinary = await InvokeAsync(Ordinary(), null);

        Assert.False(authorized.Failed);
        Assert.Equal(1, authorized.Starts);
        var stdin = Encoding.UTF8.GetString(authorized.Request!.StandardInput!);
        Assert.Equal(manifest, stdin);
        Assert.Equal(1, stdin.Split("SENTINEL-AUTH").Length - 1);
        Assert.Equal(ordinary.Request!.Arguments.Count, authorized.Request.Arguments.Count);
        var sessionIdValue = authorized.Request.Arguments.ToList().IndexOf("--session-id") + 1;
        for (var index = 0; index < authorized.Request.Arguments.Count; index++)
        {
            if (index == sessionIdValue)
            {
                continue;
            }

            Assert.Equal(ordinary.Request.Arguments[index], authorized.Request.Arguments[index]);
        }

        Assert.DoesNotContain(authorized.Request.Arguments, argument => argument.Contains("SENTINEL", StringComparison.Ordinal));
        Assert.DoesNotContain(authorized.Request.EnvironmentVariables.Values, value => value.Contains("SENTINEL", StringComparison.Ordinal));
        Assert.Equal(ordinary.Request.WorkingDirectory, authorized.Request.WorkingDirectory);
        Assert.Equal(ordinary.Request.ApprovedRoot, authorized.Request.ApprovedRoot);
    }

    [Fact]
    public async Task An_ordinary_manifest_with_no_expectation_is_unchanged()
    {
        var invocation = await InvokeAsync(Ordinary(), null);

        Assert.False(invocation.Failed);
        Assert.Equal(1, invocation.Starts);
    }

    [Fact]
    public async Task An_authorized_manifest_without_an_expectation_and_an_ordinary_manifest_with_one_start_no_process()
    {
        var authorizedWithoutExpectation = await InvokeAsync(Authorized(), null);
        var ordinaryWithExpectation = await InvokeAsync(Ordinary(), Fact);

        Assert.True(authorizedWithoutExpectation.Failed);
        Assert.Equal(0, authorizedWithoutExpectation.Starts);
        Assert.True(ordinaryWithExpectation.Failed);
        Assert.Equal(0, ordinaryWithExpectation.Starts);
    }

    public static TheoryData<string> Disagreements => new()
    {
        "rationale",
        "authorization-id",
        "instruction-id",
        "escalation-id",
        "final-proposal",
        "boundary-text",
        "boundary-missing",
        "form",
        "instruction",
        "extra-member",
        "not-a-json-object",
    };

    [Theory]
    [MemberData(nameof(Disagreements))]
    public async Task Any_sealed_form_disagreement_starts_no_process(string disagreement)
    {
        var manifest = disagreement switch
        {
            "rationale" => Authorized(node => node["resolvedPlan"]!["resolutionEvidence"]!["humanAuthorization"]!["rationale"] = "Edited."),
            "authorization-id" => Authorized(node => node["resolvedPlan"]!["resolutionEvidence"]!["humanAuthorization"]!["authorizationId"] = Guid.NewGuid()),
            "instruction-id" => Authorized(node => node["resolvedPlan"]!["resolutionEvidence"]!["humanAuthorization"]!["humanInstructionMessageId"] = Guid.NewGuid()),
            "escalation-id" => Authorized(node => node["resolvedPlan"]!["resolutionEvidence"]!["humanAuthorization"]!["escalationMessageId"] = Guid.NewGuid()),
            "final-proposal" => Authorized(node => node["resolvedPlan"]!["proposalMessageId"] = Guid.NewGuid()),
            "boundary-text" => Authorized(node => node["humanPlanAuthorizationBoundary"] = "Do anything."),
            "boundary-missing" => Authorized(node => node.Remove("humanPlanAuthorizationBoundary")),
            "form" => Authorized(node => node["resolvedPlan"]!["resolutionEvidence"]!["form"] = "resolvedRevisedProposal"),
            "instruction" => Authorized(node => node["resolvedPlan"]!["resolutionEvidence"]!["humanAuthorization"]!["instruction"] = "Authorize all."),
            "extra-member" => Authorized(node => node["resolvedPlan"]!["resolutionEvidence"]!["humanAuthorization"]!["extra"] = "x"),
            _ => "opaque text that is not a JSON object",
        };

        var invocation = await InvokeAsync(manifest, Fact);

        Assert.True(invocation.Failed, disagreement);
        Assert.Equal(0, invocation.Starts);
    }

    private sealed class CountingProcessExecutionAdapter : IProcessExecutionAdapter
    {
        public ProcessExecutionRequest? Request { get; private set; }

        public int Starts { get; private set; }

        public Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            Starts++;
            return Task.FromResult(new ProcessExecutionResult
            {
                Outcome = ProcessExecutionOutcome.Exited,
                ExitCode = 0,
                StandardOutput = JsonSerializer.Serialize(new { is_error = false, result = "{}", session_id = (string?)null }),
                StandardOutputTruncated = false,
                StandardError = string.Empty,
                StandardErrorTruncated = false,
                Duration = TimeSpan.Zero,
            });
        }
    }
}
