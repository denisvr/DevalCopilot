namespace DevalCopilot.Api.IntegrationTests.BrowserJourney.ProviderFixture;

/// <summary>
/// The verification executable: one fixed argument, reads the real candidate file in the owned worktree and exits zero only
/// when that content holds the corrected statement. There is no call counter and no stored state, so passing depends on the
/// correction edit alone.
/// </summary>
public static class VerifyRole
{
    public const string Argument = "check";

    public static int Run(IReadOnlyList<string> args, OwnedLocation location, string currentDirectory)
    {
        if (args.Count != 1 || args[0] != Argument)
        {
            throw new FixtureRefusal(FixtureRefusal.UnsupportedInvocation, "The verification executable accepts only the check argument.");
        }

        var worktree = location.RequireWorktree(currentDirectory);
        var content = File.ReadAllText(location.RequireCandidateFile(worktree));
        WaitWhileHeld(location);
        var passed = CandidateEdit.IsCorrect(content);
        InvocationLog.Append(
            location,
            new Dictionary<string, object?> { ["role"] = "verify", ["kind"] = "check", ["outcome"] = passed ? "passed" : "failed" });
        if (passed)
        {
            Console.Out.Write("PASS: Total adds its operands.\n");
            return 0;
        }

        Console.Out.Write("verification started\n");
        Console.Error.Write("FAIL " + ManifestInfo.VerificationFailureMarker + ": Total does not add left and right.\n");
        return 1;
    }

    /// <summary>The owned gate of the contention journey (see <see cref="OwnedLocation.VerifyHoldPath"/>): a bounded wait that logs nothing
    /// and ends the moment the journey removes the file. A hold that is never released is a refusal, never a pass.</summary>
    private static void WaitWhileHeld(OwnedLocation location)
    {
        var deadline = DateTime.UtcNow.AddSeconds(150);
        while (File.Exists(location.VerifyHoldPath))
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new FixtureRefusal(FixtureRefusal.UnsupportedInvocation, "The verification hold was never released.");
            }

            Thread.Sleep(50);
        }
    }
}
