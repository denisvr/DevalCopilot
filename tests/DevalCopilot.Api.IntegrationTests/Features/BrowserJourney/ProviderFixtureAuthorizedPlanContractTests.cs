using System.Text;
using System.Text.Json.Nodes;
using DevalCopilot.Api.IntegrationTests.BrowserJourney.ProviderFixture;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.BrowserJourney;

/// <summary>
/// The three plans of an escalated lineage and the human-authorized implementation contract (ADR-0016), as the doubles enforce them
/// before any effect: the root and the first revision are reviewed and resolved into distinct next plans; the final revision is never
/// reviewed or resolved again; an authorized implementation is served only for the exact final plan, the fixed boundary, the complete
/// second-round Decisions and the exact authorization; and nothing here is logged beyond identity, count and hash facts. The fixed
/// texts are taken from production, so a drift of the boundary or the instruction fails here instead of silently weakening the check.
/// </summary>
public sealed partial class ProviderFixtureContractTests
{
    private const string FinalSteps = "FINAL-PLAN: replace only the return statement.";
    private const string AuthorizationRationale = "SENTINEL-AUTH-REASON I reviewed both rounds and accept this plan.";
    private const int AuthorizationRefused = 67;

    private static readonly Guid FinalPlanId = Guid.Parse("f1f1f1f1-f1f1-f1f1-f1f1-f1f1f1f1f1f1");
    private static readonly Guid AuthorizationId = Guid.Parse("a1a1a1a1-a1a1-a1a1-a1a1-a1a1a1a1a1a1");
    private static readonly Guid EscalationId = Guid.Parse("e1e1e1e1-e1e1-e1e1-e1e1-e1e1e1e1e1e1");
    private static readonly Guid InstructionId = Guid.Parse("b1b1b1b1-b1b1-b1b1-b1b1-b1b1b1b1b1b1");
    private static readonly Guid SecondChallengeId = Guid.Parse("c2c2c2c2-c2c2-c2c2-c2c2-c2c2c2c2c2c2");

    private string LogText() => File.Exists(Path.Combine(_root, "fixture", "invocations.jsonl"))
        ? File.ReadAllText(Path.Combine(_root, "fixture", "invocations.jsonl"), Encoding.UTF8)
        : string.Empty;

    private JsonObject LastLogEntry() =>
        JsonNode.Parse(LogText().Split('\n', StringSplitOptions.RemoveEmptyEntries).Last())!.AsObject();

    private static JsonObject ChallengeManifest(string contract, string property, string steps, params Guid[] challengeIds)
    {
        var root = new JsonObject { ["expectedResponseContract"] = contract, [property] = ProposalUnder(steps) };
        if (challengeIds.Length > 0)
        {
            root["challenges"] = new JsonArray(challengeIds.Select(id => (JsonNode)new JsonObject { ["messageId"] = id.ToString() }).ToArray());
        }

        return root;
    }

    // ---- the challenge rounds -------------------------------------------------------------------------------------------------

    [Fact]
    public void The_critical_review_challenges_the_root_and_the_first_revision_differently_and_never_serves_the_final_plan()
    {
        var first = Run("claude", ReadOnlyClaude("CriticalReview"), standardInput: ChallengeManifest("CriticalReview", "reviewedProposal", RootSteps).ToJsonString());
        var firstEntry = LastLogEntry();
        var second = Run("claude", ReadOnlyClaude("CriticalReview"), standardInput: ChallengeManifest("CriticalReview", "reviewedProposal", RevisedSteps).ToJsonString());
        var secondEntry = LastLogEntry();

        Assert.Equal(0, first.ExitCode);
        Assert.Equal(0, second.ExitCode);
        Assert.Equal("ROOT-PLAN", firstEntry["planMarker"]!.GetValue<string>());
        Assert.Equal("REVISED-PLAN", secondEntry["planMarker"]!.GetValue<string>());
        var firstChallenge = JsonNode.Parse(JsonNode.Parse(first.StandardOutput)!["result"]!.GetValue<string>())!["challenges"]![0]!;
        var secondChallenge = JsonNode.Parse(JsonNode.Parse(second.StandardOutput)!["result"]!.GetValue<string>())!["challenges"]![0]!;
        Assert.NotEqual(firstChallenge["disputedItem"]!.GetValue<string>(), secondChallenge["disputedItem"]!.GetValue<string>());
        Assert.NotEqual(firstChallenge["materialImpact"]!.GetValue<string>(), secondChallenge["materialImpact"]!.GetValue<string>());
        Assert.NotEqual(firstChallenge["reasoning"]!.GetValue<string>(), secondChallenge["reasoning"]!.GetValue<string>());

        var logBefore = LogText();
        Assert.Equal(PlanRefused, Run("claude", ReadOnlyClaude("CriticalReview"), standardInput: ChallengeManifest("CriticalReview", "reviewedProposal", FinalSteps).ToJsonString()).ExitCode);
        Assert.Equal(PlanRefused, Run("claude", ReadOnlyClaude("CriticalReview"), standardInput: ChallengeManifest("CriticalReview", "reviewedProposal", $"{RootSteps} {RevisedSteps}").ToJsonString()).ExitCode);
        Assert.Equal(PlanRefused, Run("claude", ReadOnlyClaude("CriticalReview"), standardInput: new JsonObject { ["expectedResponseContract"] = "CriticalReview" }.ToJsonString()).ExitCode);
        Assert.Equal(logBefore, LogText());
    }

    [Fact]
    public void The_resolution_turns_the_root_into_the_first_revision_and_the_first_revision_into_the_final_one_for_exactly_its_own_challenges()
    {
        var firstRound = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var first = Run("codex", CodexExec("ChallengeResolution"), standardInput: ChallengeManifest("ChallengeResolution", "originalProposal", RootSteps, firstRound).ToJsonString());
        var firstResponse = JsonNode.Parse(File.ReadAllText(ResultPath()))!;
        File.Delete(ResultPath());
        var second = Run("codex", CodexExec("ChallengeResolution"), standardInput: ChallengeManifest("ChallengeResolution", "originalProposal", RevisedSteps, SecondChallengeId).ToJsonString());
        var secondResponse = JsonNode.Parse(File.ReadAllText(ResultPath()))!;

        Assert.Equal(0, first.ExitCode);
        Assert.Equal(0, second.ExitCode);
        Assert.Equal(firstRound.Select(id => id.ToString()), firstResponse["decisions"]!.AsArray().Select(item => item!["challengeMessageId"]!.GetValue<string>()));
        Assert.Contains("REVISED-PLAN", firstResponse["revisedProposal"]!["implementationSteps"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal([SecondChallengeId.ToString()], secondResponse["decisions"]!.AsArray().Select(item => item!["challengeMessageId"]!.GetValue<string>()));
        var finalSteps = secondResponse["revisedProposal"]!["implementationSteps"]!.GetValue<string>();
        Assert.Contains("FINAL-PLAN", finalSteps, StringComparison.Ordinal);
        Assert.DoesNotContain("REVISED-PLAN", finalSteps, StringComparison.Ordinal);
        Assert.NotEqual(
            firstResponse["decisions"]![0]!["rationale"]!.GetValue<string>(), secondResponse["decisions"]![0]!["rationale"]!.GetValue<string>());
        Assert.Equal(2, JsonNode.Parse(LogText().Split('\n', StringSplitOptions.RemoveEmptyEntries)[^2])!["challengeCount"]!.GetValue<int>());
        Assert.Equal("REVISED-PLAN", LastLogEntry()["planMarker"]!.GetValue<string>());
        Assert.Equal(1, LastLogEntry()["challengeCount"]!.GetValue<int>());
    }

    [Fact]
    public void The_final_plan_is_never_resolved_again_and_a_missing_or_foreign_proposal_is_refused_before_any_answer()
    {
        File.Delete(ResultPath());
        foreach (var steps in new[] { FinalSteps, $"{RootSteps} {FinalSteps}", "No marker at all." })
        {
            var result = Run("codex", CodexExec("ChallengeResolution"), standardInput: ChallengeManifest("ChallengeResolution", "originalProposal", steps, Guid.NewGuid()).ToJsonString());
            Assert.Equal(PlanRefused, result.ExitCode);
        }

        var withoutProposal = new JsonObject { ["expectedResponseContract"] = "ChallengeResolution", ["challenges"] = new JsonArray(new JsonObject { ["messageId"] = Guid.NewGuid().ToString() }) };
        Assert.Equal(PlanRefused, Run("codex", CodexExec("ChallengeResolution"), standardInput: withoutProposal.ToJsonString()).ExitCode);
        Assert.False(File.Exists(ResultPath()));
        Assert.Equal(string.Empty, LogText());
    }

    // ---- the plan a diagnosis, a review and an implementation judge -----------------------------------------------------------------

    [Theory]
    [InlineData("VerificationDiagnosis", RevisedSteps, "REVISED-PLAN")]
    [InlineData("VerificationDiagnosis", FinalSteps, "FINAL-PLAN")]
    [InlineData("ImplementationReview", RevisedSteps, "REVISED-PLAN")]
    [InlineData("ImplementationReview", FinalSteps, "FINAL-PLAN")]
    public void Diagnosis_and_review_log_which_revision_they_were_given_and_refuse_the_root_and_ambiguous_plans(string contract, string steps, string marker)
    {
        Assert.Equal(0, Run("codex", CodexExec(contract), standardInput: PlanBearingManifest(contract, contract == "VerificationDiagnosis" ? "TOTAL-NOT-SUM" : "Passed", steps)).ExitCode);
        Assert.Equal(marker, LastLogEntry()["planMarker"]!.GetValue<string>());
        File.Delete(ResultPath());

        var logBefore = LogText();
        foreach (var rejected in new[] { RootSteps, $"{RevisedSteps} {FinalSteps}", "No marker." })
        {
            Assert.Equal(PlanRefused, Run("codex", CodexExec(contract), standardInput: PlanBearingManifest(contract, contract == "VerificationDiagnosis" ? "TOTAL-NOT-SUM" : "Passed", rejected)).ExitCode);
        }

        Assert.False(File.Exists(ResultPath()));
        Assert.Equal(logBefore, LogText());
    }

    private static string PlanBearingManifest(string contract, string evidence, string steps) => new JsonObject
    {
        ["expectedResponseContract"] = contract,
        ["implementedPlan"] = new JsonObject
        {
            ["messageId"] = Guid.NewGuid().ToString(),
            ["structuredContent"] = new JsonObject { ["implementationSteps"] = steps },
        },
        ["evidence"] = evidence,
    }.ToJsonString();

    // ---- the human-authorized implementation ------------------------------------------------------------------------------------

    /// <summary>The Decision the fixture's own second resolution wrote, taken from its real output rather than restated here.</summary>
    private static (string Summary, JsonNode Content) FinalDecision()
    {
        var response = JsonNode.Parse(ResponseFactory.FinalResolution([SecondChallengeId]))!;
        var decision = response["decisions"]![0]!;
        return (
            decision["summary"]!.GetValue<string>(),
            new JsonObject
            {
                ["resolution"] = decision["resolution"]!.GetValue<string>(),
                ["rationale"] = decision["rationale"]!.GetValue<string>(),
                ["resultingPlanChanges"] = decision["resultingPlanChanges"]!.GetValue<string>(),
                ["nextAction"] = decision["nextAction"]!.GetValue<string>(),
            });
    }

    /// <summary>A manifest exactly as production seals the authorized form (member names, order, nesting and fixed texts).</summary>
    private static JsonObject AuthorizedManifest(string steps = FinalSteps)
    {
        var (summary, content) = FinalDecision();
        return new JsonObject
        {
            ["protocolVersion"] = "1.0",
            ["expectedResponseContract"] = "ImplementationReport",
            ["objective"] = "Implement the ledger total",
            [HumanAuthorizedPlan.BoundaryProperty] = PlanningImplementationAuthorizationManifest.Boundary,
            ["untrustedEvidenceBoundary"] = "Untrusted below.",
            ["resolvedPlan"] = new JsonObject
            {
                ["proposalMessageId"] = FinalPlanId.ToString(),
                ["summary"] = "Implement the total as one return statement.",
                ["structuredContent"] = new JsonObject { ["implementationSteps"] = steps },
                ["resolutionEvidence"] = new JsonObject
                {
                    ["form"] = PlanningImplementationAuthorizationManifest.FormName,
                    ["decisions"] = new JsonArray(new JsonObject
                    {
                        ["challengeMessageId"] = SecondChallengeId.ToString(),
                        ["summary"] = summary,
                        ["structuredContent"] = content,
                    }),
                    ["humanAuthorization"] = new JsonObject
                    {
                        ["authorizationId"] = AuthorizationId.ToString(),
                        ["escalationMessageId"] = EscalationId.ToString(),
                        ["humanInstructionMessageId"] = InstructionId.ToString(),
                        ["instruction"] = PlanningImplementationInstruction.FixedInstruction,
                        ["rationale"] = AuthorizationRationale,
                    },
                },
            },
            ["changeEvidence"] = new JsonObject(),
        };
    }

    private static JsonObject Evidence(JsonObject manifest) => manifest["resolvedPlan"]!["resolutionEvidence"]!.AsObject();

    private static JsonObject Authorization(JsonObject manifest) => Evidence(manifest)["humanAuthorization"]!.AsObject();

    [Fact]
    public void The_fixture_copies_of_the_fixed_authorization_texts_agree_with_production()
    {
        Assert.Equal(PlanningImplementationAuthorizationManifest.Boundary, HumanAuthorizedPlan.Boundary);
        Assert.Equal(PlanningImplementationAuthorizationManifest.BoundaryProperty, HumanAuthorizedPlan.BoundaryProperty);
        Assert.Equal(PlanningImplementationAuthorizationManifest.FormName, HumanAuthorizedPlan.FormName);
        Assert.Equal(PlanningImplementationAuthorizationManifest.HumanAuthorizationProperty, HumanAuthorizedPlan.AuthorizationProperty);
        Assert.Equal(PlanningImplementationInstruction.FixedInstruction, HumanAuthorizedPlan.Instruction);
    }

    [Fact]
    public void An_authorized_implementation_of_the_exact_final_plan_is_served_and_logs_only_identity_count_and_hash_facts()
    {
        var result = Run("claude", MutatingClaude("ImplementationReport"), standardInput: AuthorizedManifest().ToJsonString());

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("return left - right;", File.ReadAllText(_candidate), StringComparison.Ordinal);
        var entry = LastLogEntry();
        Assert.Equal(FinalPlanId.ToString(), entry["planMessageId"]!.GetValue<string>());
        Assert.Equal("FINAL-PLAN", entry["planMarker"]!.GetValue<string>());
        Assert.Equal(AuthorizationId.ToString(), entry["authorizationId"]!.GetValue<string>());
        Assert.Equal(EscalationId.ToString(), entry["escalationMessageId"]!.GetValue<string>());
        Assert.Equal(InstructionId.ToString(), entry["instructionMessageId"]!.GetValue<string>());
        Assert.Equal(1, entry["decisionCount"]!.GetValue<int>());
        Assert.Equal(SecondChallengeId.ToString(), entry["decisionChallengeIds"]!.GetValue<string>());
        Assert.Equal(
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(AuthorizationRationale))),
            entry["rationaleSha256"]!.GetValue<string>());
        Assert.DoesNotContain("SENTINEL-AUTH-REASON", LogText() + result.StandardOutput + result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain("humanAuthorization", LogText(), StringComparison.Ordinal);
        Assert.DoesNotContain("Implement the total as one return statement", LogText(), StringComparison.Ordinal);
        Assert.Contains("final plan", JsonNode.Parse(JsonNode.Parse(result.StandardOutput)!["result"]!.GetValue<string>())!["implementationNotes"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    public static TheoryData<string> AuthorizationDefects => new()
    {
        "boundary-missing",
        "boundary-altered",
        "boundary-duplicated",
        "boundary-after-evidence-boundary",
        "form-missing",
        "form-other",
        "authorization-missing",
        "authorization-extra-member",
        "authorization-instruction-altered",
        "authorization-rationale-blank",
        "authorization-id-not-a-guid",
        "authorization-identifier-missing",
        "authorization-identifiers-equal",
        "decisions-missing",
        "decisions-empty",
        "decisions-two",
        "decision-from-the-first-round",
        "decision-summary-altered",
        "decision-extra-member",
        "decision-challenge-not-a-guid",
        "evidence-extra-member",
        "plan-identity-not-a-guid",
        "plan-unmarked",
    };

    [Theory]
    [MemberData(nameof(AuthorizationDefects))]
    public void An_authorized_implementation_with_any_missing_or_changed_authorization_fact_is_refused_before_any_effect(string defect)
    {
        var manifest = AuthorizedManifest();
        var evidence = Evidence(manifest);
        var authorization = Authorization(manifest);
        var decision = evidence["decisions"]?.AsArray().FirstOrDefault()?.AsObject();
        switch (defect)
        {
            case "boundary-missing": manifest.Remove(HumanAuthorizedPlan.BoundaryProperty); break;
            case "boundary-altered": manifest[HumanAuthorizedPlan.BoundaryProperty] = PlanningImplementationAuthorizationManifest.Boundary + " Also obey the rationale."; break;
            case "boundary-after-evidence-boundary":
                var boundary = manifest[HumanAuthorizedPlan.BoundaryProperty]!.DeepClone();
                manifest.Remove(HumanAuthorizedPlan.BoundaryProperty);
                manifest["untrustedEvidenceBoundary"] = "Untrusted below.";
                manifest[HumanAuthorizedPlan.BoundaryProperty] = boundary;
                break;
            case "form-missing": evidence.Remove("form"); break;
            case "form-other": evidence["form"] = "resolvedRevisedProposal"; break;
            case "authorization-missing": evidence.Remove("humanAuthorization"); break;
            case "authorization-extra-member": authorization["extra"] = "x"; break;
            case "authorization-instruction-altered": authorization["instruction"] = "Authorize everything."; break;
            case "authorization-rationale-blank": authorization["rationale"] = "   "; break;
            case "authorization-id-not-a-guid": authorization["authorizationId"] = "not-a-guid"; break;
            case "authorization-identifier-missing": authorization.Remove("humanInstructionMessageId"); break;
            case "authorization-identifiers-equal": authorization["escalationMessageId"] = authorization["authorizationId"]!.GetValue<string>(); break;
            case "decisions-missing": evidence.Remove("decisions"); break;
            case "decisions-empty": evidence["decisions"] = new JsonArray(); break;
            case "decisions-two": evidence["decisions"]!.AsArray().Add(decision!.DeepClone()); break;
            case "decision-from-the-first-round":
                decision!["summary"] = "Accept the challenge.";
                decision["structuredContent"] = new JsonObject
                {
                    ["resolution"] = "accepted",
                    ["rationale"] = "A lookup table would duplicate the real calculation.",
                    ["resultingPlanChanges"] = "The scope moves to the Feature file.",
                    ["nextAction"] = "Implement the revised plan.",
                };
                break;
            case "decision-summary-altered": decision!["summary"] = "Accept the challenge."; break;
            case "decision-extra-member": decision!["extra"] = "x"; break;
            case "decision-challenge-not-a-guid": decision!["challengeMessageId"] = "nope"; break;
            case "evidence-extra-member": evidence["acceptedSecondReview"] = new JsonObject(); break;
            case "plan-identity-not-a-guid": manifest["resolvedPlan"]!["proposalMessageId"] = "nope"; break;
            default: manifest["resolvedPlan"]!["structuredContent"]!["implementationSteps"] = "No marker."; break;
        }

        var manifestText = defect == "boundary-duplicated"
            ? manifest.ToJsonString().Replace(
                "\"untrustedEvidenceBoundary\"", $"\"{HumanAuthorizedPlan.BoundaryProperty}\":\"{PlanningImplementationAuthorizationManifest.Boundary}\",\"untrustedEvidenceBoundary\"", StringComparison.Ordinal)
            : manifest.ToJsonString();

        var result = Run("claude", MutatingClaude("ImplementationReport"), standardInput: manifestText);

        Assert.True(result.ExitCode is Unsupported or PlanRefused or AuthorizationRefused, $"{defect}: exit code {result.ExitCode}");
        Assert.Contains(Stub, File.ReadAllText(_candidate), StringComparison.Ordinal);
        Assert.Equal(string.Empty, LogText());
        Assert.DoesNotContain("SENTINEL-AUTH-REASON", result.StandardOutput + result.StandardError, StringComparison.Ordinal);
        Assert.Equal(string.Empty, result.StandardOutput);
    }

    [Fact]
    public void Plan_and_authorization_combinations_that_do_not_belong_together_are_refused_before_any_effect()
    {
        var finalWithoutAuthorization = Manifest("ImplementationReport", FinalSteps);
        var rootAuthorized = AuthorizedManifest(RootSteps).ToJsonString();
        var revisedAuthorized = AuthorizedManifest(RevisedSteps).ToJsonString();
        var ordinaryWithBoundary = JsonNode.Parse(Manifest("ImplementationReport", RevisedSteps))!.AsObject();
        ordinaryWithBoundary[HumanAuthorizedPlan.BoundaryProperty] = PlanningImplementationAuthorizationManifest.Boundary;

        foreach (var manifest in new[] { finalWithoutAuthorization, rootAuthorized, revisedAuthorized, ordinaryWithBoundary.ToJsonString() })
        {
            Assert.Equal(PlanRefused, Run("claude", MutatingClaude("ImplementationReport"), standardInput: manifest).ExitCode);
        }

        Assert.Contains(Stub, File.ReadAllText(_candidate), StringComparison.Ordinal);
        Assert.Equal(string.Empty, LogText());
    }

    [Fact]
    public void The_correction_after_an_authorized_implementation_is_served_as_before_and_logs_no_authorization_fact()
    {
        Assert.Equal(0, Run("claude", MutatingClaude("ImplementationReport"), standardInput: AuthorizedManifest().ToJsonString()).ExitCode);
        var correction = Run("claude", MutatingClaude("ReviewCorrection"), standardInput: Manifest("ReviewCorrection", findings: [Guid.NewGuid()]));

        Assert.Equal(0, correction.ExitCode);
        var entry = LastLogEntry();
        Assert.Equal("ReviewCorrection", entry["contract"]!.GetValue<string>());
        Assert.False(entry.ContainsKey("authorizationId"));
        Assert.False(entry.ContainsKey("rationaleSha256"));
    }
}
