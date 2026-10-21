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

    /// <summary>The trailing comment the first correction of the local execution cycle journey leaves behind and the second removes.
    /// The statement still adds its operands, so verification passes with it present.</summary>
    public const string ReviewNit = " // REVIEW-NIT";

    /// <summary>
    /// The two distinct corrections of the local execution cycle journey, chosen by the file's own state so the double keeps no counter:
    /// the defect becomes the correct statement plus a review nit (ordinal one); the nit is then removed (ordinal two). Any other
    /// state is refused, so neither correction can be applied twice or out of order.
    /// </summary>
    public static (string RelativePath, int Ordinal) ApplyLocalCycleCorrection(string candidatePath)
    {
        var content = File.ReadAllText(candidatePath);
        if (content.Contains(Defect, StringComparison.Ordinal))
        {
            return (Replace(candidatePath, Defect, Correct + ReviewNit), 1);
        }

        if (content.Contains(Correct + ReviewNit, StringComparison.Ordinal))
        {
            return (Replace(candidatePath, Correct + ReviewNit, Correct), 2);
        }

        throw new FixtureRefusal(FixtureRefusal.UnsupportedInvocation, "The candidate file is not in a state either local cycle correction expects.");
    }

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
