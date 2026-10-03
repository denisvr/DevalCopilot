namespace DevalCopilot.Api.IntegrationTests.BrowserJourney.ProviderFixture;

/// <summary>
/// The only repository mutation a double ever performs: one exact text replacement in the one fixed candidate file of an owned
/// worktree. The expected current content is required, so an edit applied twice, or to a different file state, is refused.
/// Verification passes only when the file holds the corrected statement and not the defective one.
/// </summary>
public static class CandidateEdit
{
    public const string Stub = "return 0;";
    public const string Defect = "return left - right;";
    public const string Correct = "return left + right;";

    public static string IntroduceDefect(string candidatePath) => Replace(candidatePath, Stub, Defect);

    public static string ApplyCorrection(string candidatePath) => Replace(candidatePath, Defect, Correct);

    public static bool IsCorrect(string content) =>
        content.Contains(Correct, StringComparison.Ordinal) && !content.Contains(Defect, StringComparison.Ordinal);

    private static string Replace(string candidatePath, string expected, string replacement)
    {
        var content = File.ReadAllText(candidatePath);
        if (!content.Contains(expected, StringComparison.Ordinal))
        {
            throw new FixtureRefusal(FixtureRefusal.UnsupportedInvocation, "The candidate file is not in the state this edit expects.");
        }

        File.WriteAllText(candidatePath, content.Replace(expected, replacement, StringComparison.Ordinal));
        return OwnedLocation.CandidateRelativePath;
    }
}
