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

        string response;
        switch (manifest.Contract, exec.Profile)
        {
            case ("CriticalReview", ClosedArguments.ClaudeProfile.ReadOnly):
                response = ResponseFactory.Challenge();
                break;
            case ("ImplementationReport", ClosedArguments.ClaudeProfile.Mutating):
                var (planId, marker) = manifest.RequireRevisedPlan();
                fields["planMessageId"] = planId;
                fields["planMarker"] = marker;
                fields["changedPath"] = CandidateEdit.IntroduceDefect(location.RequireCandidateFile(worktree));
                response = ResponseFactory.ImplementationReport();
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
