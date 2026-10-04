using System.Text;

namespace DevalCopilot.Api.IntegrationTests.BrowserJourney.ProviderFixture;

/// <summary>The Codex double: the fixed version probe and the fixed read-only exec contract, answering the four Codex stages.</summary>
public static class CodexRole
{
    public static int Run(IReadOnlyList<string> args, OwnedLocation location, string currentDirectory, Func<string> readStandardInput)
    {
        if (args.Count == 1 && args[0] == ClosedArguments.CodexProbe)
        {
            InvocationLog.Append(location, new Dictionary<string, object?> { ["role"] = "codex", ["kind"] = "probe" });
            Console.Out.Write("codex-cli 1.2.3 (browser journey fixture)\n");
            return 0;
        }

        var exec = ClosedArguments.ParseCodex(args);
        var worktree = location.RequireWorktree(currentDirectory);
        if (!string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(exec.WorkingDirectory)), worktree, StringComparison.OrdinalIgnoreCase))
        {
            throw new FixtureRefusal(FixtureRefusal.OwnershipRefused, "The declared working directory is not the process working directory.");
        }

        var manifest = ManifestInfo.Parse(readStandardInput());
        ResponseSchemas.RequireAgreement("codex", manifest.Contract, ResponseSchemas.ReadBoundedFile(exec.SchemaPath));
        var resultPath = location.RequireArtifactSink(exec.ResultPath);
        var fields = new Dictionary<string, object?> { ["role"] = "codex", ["kind"] = "exec", ["contract"] = manifest.Contract };
        InstructionEvidence.Record(fields, manifest);

        string response;
        switch (manifest.Contract)
        {
            case "Proposal":
                response = ResponseFactory.RootProposal();
                break;
            case "ChallengeResolution":
                // The proposal being resolved decides the round: the root yields the first revision, the first revision yields the
                // final one. The final revision is never resolved again, and no other proposal is served.
                var (resolvedId, resolvedMarker) = manifest.RequireRoundProposal("originalProposal");
                var challengeIds = manifest.MessageIds("challenges");
                fields["planMessageId"] = resolvedId;
                fields["planMarker"] = resolvedMarker;
                fields["challengeCount"] = challengeIds.Count;
                response = resolvedMarker == ManifestInfo.RootPlanMarker
                    ? ResponseFactory.Resolution(challengeIds)
                    : ResponseFactory.FinalResolution(challengeIds);
                break;
            case "VerificationDiagnosis":
                RecordPlan(manifest, fields);
                if (!manifest.Raw.Contains(ManifestInfo.VerificationFailureMarker, StringComparison.Ordinal))
                {
                    throw new FixtureRefusal(FixtureRefusal.PlanIdentityRefused, "The diagnosis input carries no failed verification output.");
                }

                response = ResponseFactory.DiagnosisFindings();
                fields["findingCount"] = 1;
                break;
            case "ImplementationReview":
                var reviewedMarker = RecordPlan(manifest, fields);
                if (!manifest.Raw.Contains("Passed", StringComparison.Ordinal))
                {
                    throw new FixtureRefusal(FixtureRefusal.PlanIdentityRefused, "The review input carries no passed verification.");
                }

                response = ResponseFactory.ReviewApproved(reviewedMarker);
                break;
            default:
                throw new FixtureRefusal(FixtureRefusal.UnsupportedInvocation, "The manifest contract is not served by the Codex double.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(resultPath)!);
        File.WriteAllText(resultPath, response, new UTF8Encoding(false));
        InvocationLog.Append(location, fields);
        Console.Out.Write("{\"type\":\"thread.started\",\"thread_id\":\"fixture-thread-" + Guid.NewGuid().ToString("N") + "\"}\n");
        return 0;
    }

    private static string RecordPlan(ManifestInfo manifest, Dictionary<string, object?> fields)
    {
        var (planId, marker) = manifest.RequireRevisionPlan();
        fields["planMessageId"] = planId;
        fields["planMarker"] = marker;
        fields["reportMessageId"] = manifest.ReportMessageId();
        return marker;
    }
}
