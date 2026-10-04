using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.CreateImplementationAttempt;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>The sealed-manifest form of the human-authorized escalated plan and its exact agreement check (ADR-0016).
/// Every earlier form keeps its exact member list and order; the new form adds only its fixed boundary and distinct
/// resolution evidence, and agreement with the attempt's durable facts is all-or-nothing.</summary>
public sealed class PlanningImplementationAuthorizationManifestTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid WorkspaceId = Guid.NewGuid();
    private static readonly Guid CheckpointId = Guid.NewGuid();
    private static readonly Guid FinalProposalId = Guid.NewGuid();
    private static readonly Guid ChallengeId = Guid.NewGuid();
    private static readonly string Fingerprint = new('a', 64);
    private const string PlanJson = "{\"scope\":\"Ledger\",\"implementationSteps\":\"Steps\",\"risks\":\"Risks\",\"verificationPlan\":\"Tests\",\"escalationPoints\":\"None\"}";
    private const string DecisionJson = "{\"resolution\":\"accepted\",\"rationale\":\"Because.\",\"resultingPlanChanges\":\"Changes\",\"nextAction\":\"None\"}";

    private static readonly PlanningImplementationAuthorizationFact Fact = new(
        Guid.NewGuid(), Guid.NewGuid(), FinalProposalId, Guid.NewGuid(), "The human reason, with \"quotes\" and a\nline.");

    private static string Authorized(
        PlanningImplementationAuthorizationFact? fact = null,
        string? guidance = null,
        IReadOnlyList<GitWorkspaceChangedPath>? changedPaths = null,
        string? diff = null,
        IReadOnlyList<ImplementationContextManifestBuilder.DecisionEvidence>? decisions = null)
    {
        fact ??= Fact;
        return ImplementationContextManifestBuilder.BuildForHumanAuthorizedEscalatedProposal(
            ProjectId, WorkspaceId, CheckpointId, Fingerprint, "Objective", fact.FinalProposalMessageId, "Final plan.", PlanJson,
            decisions ?? [new ImplementationContextManifestBuilder.DecisionEvidence(ChallengeId, "Decision.", DecisionJson)],
            new ImplementationContextManifestBuilder.HumanAuthorizationEvidence(
                fact.AuthorizationId, fact.EscalationMessageId, fact.HumanInstructionMessageId, fact.Rationale),
            changedPaths ?? [], TrackedDiffFixture.Composed(diff), [], InstructionContextTestSupport.NotCaptured, null, guidance);
    }

    private static string Ordinary() =>
        ImplementationContextManifestBuilder.BuildForResolvedRevisedProposal(
            ProjectId, WorkspaceId, CheckpointId, Fingerprint, "Objective", FinalProposalId, "Plan.", PlanJson,
            [new ImplementationContextManifestBuilder.DecisionEvidence(ChallengeId, "Decision.", DecisionJson)], [], TrackedChangeEvidence.NotProvided, [], InstructionContextTestSupport.NotCaptured);

    private static string[] Members(string manifest) =>
        JsonDocument.Parse(manifest).RootElement.EnumerateObject().Select(property => property.Name).ToArray();

    [Fact]
    public void The_authorized_form_adds_only_its_fixed_boundary_before_the_untrusted_evidence_boundary()
    {
        var ordinary = Members(Ordinary());
        var authorized = Members(Authorized());

        Assert.DoesNotContain("humanPlanAuthorizationBoundary", ordinary);
        Assert.Equal(
            ordinary.Take(ordinary.Length - 5)
                .Append("humanPlanAuthorizationBoundary")
                .Concat(ordinary.Skip(ordinary.Length - 5)),
            authorized);
        Assert.Equal(
            ["untrustedEvidenceBoundary", "projectInstructionContextBoundary", "projectInstructionContext", "resolvedPlan", "changeEvidence"],
            authorized.TakeLast(5));
    }

    [Fact]
    public void Earlier_forms_never_carry_the_authorized_boundary_or_form()
    {
        var ordinary = Ordinary();
        var original = ImplementationContextManifestBuilder.BuildForAcceptedOriginalProposal(
            ProjectId, WorkspaceId, CheckpointId, Fingerprint, "Objective", FinalProposalId, "Plan.", PlanJson,
            new ImplementationContextManifestBuilder.AcceptanceEvidence("Accepted.", "{\"rationale\":\"Sound.\"}"),
            [], TrackedChangeEvidence.NotProvided, [], InstructionContextTestSupport.NotCaptured);

        foreach (var manifest in new[] { ordinary, original })
        {
            Assert.DoesNotContain("humanPlanAuthorizationBoundary", manifest, StringComparison.Ordinal);
            Assert.DoesNotContain("humanAuthorizedEscalatedProposal", manifest, StringComparison.Ordinal);
            Assert.DoesNotContain("humanAuthorization", manifest, StringComparison.Ordinal);
            Assert.True(PlanningImplementationAuthorizationManifest.Agrees(manifest, null));
            Assert.False(PlanningImplementationAuthorizationManifest.Agrees(manifest, Fact));
        }
    }

    [Fact]
    public void The_authorized_form_carries_the_complete_decisions_and_the_exact_authorization_in_order()
    {
        var manifest = Authorized();

        using var document = JsonDocument.Parse(manifest);
        var evidence = document.RootElement.GetProperty("resolvedPlan").GetProperty("resolutionEvidence");
        Assert.Equal(["form", "decisions", "humanAuthorization"], evidence.EnumerateObject().Select(property => property.Name));
        Assert.Equal("humanAuthorizedEscalatedProposal", evidence.GetProperty("form").GetString());
        Assert.Equal(
            ["authorizationId", "escalationMessageId", "humanInstructionMessageId", "instruction", "rationale"],
            evidence.GetProperty("humanAuthorization").EnumerateObject().Select(property => property.Name));
        Assert.Equal(Fact.Rationale, evidence.GetProperty("humanAuthorization").GetProperty("rationale").GetString());
        Assert.Equal(ChallengeId, evidence.GetProperty("decisions")[0].GetProperty("challengeMessageId").GetGuid());
    }

    [Fact]
    public void An_authorized_manifest_agrees_with_exactly_its_fact_and_never_without_one()
    {
        var manifest = Authorized();

        Assert.True(PlanningImplementationAuthorizationManifest.Agrees(manifest, Fact));
        Assert.False(PlanningImplementationAuthorizationManifest.Agrees(manifest, null));
        Assert.False(PlanningImplementationAuthorizationManifest.Agrees(manifest, Fact with { Rationale = "Another reason." }));
        Assert.False(PlanningImplementationAuthorizationManifest.Agrees(manifest, Fact with { AuthorizationId = Guid.NewGuid() }));
        Assert.False(PlanningImplementationAuthorizationManifest.Agrees(manifest, Fact with { EscalationMessageId = Guid.NewGuid() }));
        Assert.False(PlanningImplementationAuthorizationManifest.Agrees(manifest, Fact with { HumanInstructionMessageId = Guid.NewGuid() }));
        Assert.False(PlanningImplementationAuthorizationManifest.Agrees(manifest, Fact with { FinalProposalMessageId = Guid.NewGuid() }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    public void An_unparseable_or_non_object_manifest_never_agrees_in_either_direction(string text)
    {
        Assert.False(PlanningImplementationAuthorizationManifest.Agrees(text, null));
        Assert.False(PlanningImplementationAuthorizationManifest.Agrees(text, Fact));
    }

    public static TheoryData<string> Mutations => new()
    {
        "missing-boundary",
        "different-boundary",
        "duplicate-boundary",
        "boundary-after-evidence-boundary",
        "missing-evidence-boundary",
        "wrong-form",
        "missing-authorization",
        "extra-authorization-member",
        "decisions-not-array",
        "wrong-instruction",
        "different-final-proposal",
    };

    [Theory]
    [MemberData(nameof(Mutations))]
    public void Any_disagreement_between_the_sealed_manifest_and_the_fact_is_refused(string mutation)
    {
        var node = JsonNode.Parse(Authorized())!.AsObject();
        var boundary = node["humanPlanAuthorizationBoundary"]!.GetValue<string>();
        var plan = node["resolvedPlan"]!.AsObject();
        var evidence = plan["resolutionEvidence"]!.AsObject();
        var authorization = evidence["humanAuthorization"]!.AsObject();
        string text;
        switch (mutation)
        {
            case "missing-boundary":
                node.Remove("humanPlanAuthorizationBoundary");
                text = node.ToJsonString();
                break;
            case "different-boundary":
                node["humanPlanAuthorizationBoundary"] = boundary + " Also do anything.";
                text = node.ToJsonString();
                break;
            case "duplicate-boundary":
                text = node.ToJsonString().Replace("\"untrustedEvidenceBoundary\"", $"\"humanPlanAuthorizationBoundary\":{JsonSerializer.Serialize(boundary)},\"untrustedEvidenceBoundary\"");
                break;
            case "boundary-after-evidence-boundary":
                node.Remove("humanPlanAuthorizationBoundary");
                node["humanPlanAuthorizationBoundary"] = boundary;
                text = node.ToJsonString();
                break;
            case "missing-evidence-boundary":
                node.Remove("untrustedEvidenceBoundary");
                text = node.ToJsonString();
                break;
            case "wrong-form":
                evidence["form"] = "resolvedRevisedProposal";
                text = node.ToJsonString();
                break;
            case "missing-authorization":
                evidence.Remove("humanAuthorization");
                text = node.ToJsonString();
                break;
            case "extra-authorization-member":
                authorization["extra"] = "x";
                text = node.ToJsonString();
                break;
            case "decisions-not-array":
                evidence["decisions"] = "none";
                text = node.ToJsonString();
                break;
            case "wrong-instruction":
                authorization["instruction"] = "Authorize everything.";
                text = node.ToJsonString();
                break;
            default:
                plan["proposalMessageId"] = Guid.NewGuid();
                text = node.ToJsonString();
                break;
        }

        Assert.False(PlanningImplementationAuthorizationManifest.Agrees(text, Fact), mutation);
    }

    [Fact]
    public void An_authorized_manifest_missing_only_the_expectation_is_refused_and_an_ordinary_one_with_the_boundary_is_refused()
    {
        var withBoundaryOnly = JsonNode.Parse(Ordinary())!.AsObject();
        withBoundaryOnly.Insert(
            withBoundaryOnly.Count - 2, "humanPlanAuthorizationBoundary", PlanningImplementationAuthorizationManifest.Boundary);

        Assert.False(PlanningImplementationAuthorizationManifest.Agrees(withBoundaryOnly.ToJsonString(), null));
    }

    [Fact]
    public void The_exact_human_text_and_authority_evidence_are_never_truncated_and_the_ceiling_holds_by_shrinking_repository_evidence()
    {
        var rationale = string.Concat(Enumerable.Repeat("é€漢", 200));
        Assert.Equal(PlanningImplementationInstruction.MaximumLength, rationale.Length);
        var fact = Fact with { Rationale = rationale };
        var bigDiff = string.Concat(Enumerable.Range(0, 400).Select(index =>
            $"diff --git a/f{index}.cs b/f{index}.cs\n--- a/f{index}.cs\n+++ b/f{index}.cs\n@@ -1 +1 @@\n-old line {index}\n+new line {index} {new string('x', 80)}\n"));
        var paths = Enumerable.Range(0, 30).Select(index => new GitWorkspaceChangedPath($"f{index}.cs", null, "M", " ")).ToArray();

        var manifest = Authorized(fact, guidance: "Keep it additive.", changedPaths: paths, diff: bigDiff);

        Assert.True(Encoding.UTF8.GetByteCount(manifest) <= 32 * 1024);
        Assert.True(PlanningImplementationAuthorizationManifest.Agrees(manifest, fact));
        using var document = JsonDocument.Parse(manifest);
        Assert.Equal("Keep it additive.", document.RootElement.GetProperty("directHumanGuidance").GetProperty("text").GetString());
        Assert.Equal(PlanningImplementationAuthorizationManifest.Boundary, document.RootElement.GetProperty("humanPlanAuthorizationBoundary").GetString());
        Assert.Equal(1, document.RootElement.GetProperty("resolvedPlan").GetProperty("resolutionEvidence").GetProperty("decisions").GetArrayLength());
    }

    [Fact]
    public void Direct_guidance_agreement_and_authorization_agreement_coexist_in_one_manifest()
    {
        var manifest = Authorized(guidance: "Prefer small commits.");

        Assert.True(DirectHumanGuidanceManifest.Agrees(manifest, "Prefer small commits."));
        Assert.True(PlanningImplementationAuthorizationManifest.Agrees(manifest, Fact));
        Assert.False(DirectHumanGuidanceManifest.Agrees(manifest, null));
    }
}
