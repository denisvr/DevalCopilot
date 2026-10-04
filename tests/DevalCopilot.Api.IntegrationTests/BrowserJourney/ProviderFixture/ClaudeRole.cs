using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DevalCopilot.Api.IntegrationTests.BrowserJourney.ProviderFixture;

/// <summary>The Claude double: the fixed version probe and the fixed print contracts for critical review, implementation, and correction.</summary>
public static class ClaudeRole
{
    public static int Run(IReadOnlyList<string> args, OwnedLocation location, string currentDirectory, Func<string> readStandardInput)
    {
        if (args.Count == 1 && args[0] == "--version")
        {
            InvocationLog.Append(location, new Dictionary<string, object?> { ["role"] = "claude", ["kind"] = "probe" });
            Console.Out.Write("2.1.0 (Claude Code browser journey fixture)\n");
            return 0;
        }

        var exec = ClosedArguments.ParseClaude(args);
        var worktree = location.RequireWorktree(currentDirectory);
        var manifest = ManifestInfo.Parse(readStandardInput());
        ResponseSchemas.RequireAgreement("claude", manifest.Contract, exec.SchemaJson);
        var fields = new Dictionary<string, object?> { ["role"] = "claude", ["kind"] = "print", ["contract"] = manifest.Contract };
        InstructionEvidence.Record(fields, manifest);

        string response;
        switch (manifest.Contract, exec.Profile)
        {
            case ("CriticalReview", ClosedArguments.ClaudeProfile.ReadOnly):
                // The proposal under review decides the round: the root receives the first challenge, the first revision the second.
                // The final revision is never reviewed again, and no other proposal is served.
                var (reviewedId, reviewedMarker) = manifest.RequireRoundProposal("reviewedProposal");
                fields["planMessageId"] = reviewedId;
                fields["planMarker"] = reviewedMarker;
                response = reviewedMarker == ManifestInfo.RootPlanMarker ? ResponseFactory.Challenge() : ResponseFactory.SecondChallenge();
                break;
            case ("ImplementationReport", ClosedArguments.ClaudeProfile.Mutating):
                // The plan, and for the human-authorized final plan its whole authorization contract, are validated before any edit,
                // answer or successful log entry. Only identity, count and hash facts are logged.
                var (planId, marker, authorization) = manifest.RequireImplementablePlan();
                fields["planMessageId"] = planId;
                fields["planMarker"] = marker;
                if (authorization is not null)
                {
                    fields["authorizationId"] = authorization.AuthorizationId;
                    fields["escalationMessageId"] = authorization.EscalationMessageId;
                    fields["instructionMessageId"] = authorization.InstructionMessageId;
                    fields["rationaleSha256"] = authorization.RationaleSha256;
                    fields["decisionCount"] = authorization.DecisionChallengeIds.Count;
                    fields["decisionChallengeIds"] = string.Join(",", authorization.DecisionChallengeIds);
                }

                fields["changedPath"] = CandidateEdit.IntroduceDefect(location.RequireCandidateFile(worktree));
                response = ResponseFactory.ImplementationReport(marker);
                break;
            case ("ReviewCorrection", ClosedArguments.ClaudeProfile.Mutating):
                var findingIds = manifest.MessageIds("orderedFindings");
                fields["findingCount"] = findingIds.Count;
                fields["reportMessageId"] = manifest.ReportMessageId();
                // The sealed direct guidance (ADR-0019), if any, is checked before the edit; only its hash and whether the host's fixed
                // boundary framed it are logged, never the text.
                if (manifest.DirectGuidance() is { } guidance)
                {
                    fields["guidanceSha256"] = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(guidance.Text)));
                    fields["guidanceBoundary"] = guidance.BoundaryIsFixed ? "fixed" : "altered";
                }

                fields["changedPath"] = CandidateEdit.ApplyCorrection(location.RequireCandidateFile(worktree));
                response = ResponseFactory.Correction(findingIds);
                break;
            default:
                throw new FixtureRefusal(
                    FixtureRefusal.UnsupportedInvocation, "The manifest contract and the argument profile are not served together by the Claude double.");
        }

        InvocationLog.Append(location, fields);
        var envelope = new JsonObject
        {
            ["type"] = "result",
            ["subtype"] = "success",
            ["is_error"] = false,
            ["result"] = response,
            ["session_id"] = "fixture-session-" + Guid.NewGuid().ToString("N"),
        };
        Console.Out.Write(envelope.ToJsonString(new JsonSerializerOptions()) + "\n");
        return 0;
    }
}
