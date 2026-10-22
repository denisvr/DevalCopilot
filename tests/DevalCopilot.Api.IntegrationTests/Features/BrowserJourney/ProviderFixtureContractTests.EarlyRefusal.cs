using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.BrowserJourney;

// LCQ-V2 (review 2): the real fixture checks ownership BEFORE it reads standard input, so a legitimate refusal may close the pipe while the test
// is still writing. The helper must keep that refusal observable (exit code, output and no-effect assertions) and must still fail a stdin
// delivery that breaks on an invocation that was not refused.
public sealed partial class ProviderFixtureContractTests
{
    [Fact]
    public void An_early_ownership_refusal_stays_observable_even_when_the_input_exceeds_the_pipe_buffer()
    {
        var elsewhere = Directory.CreateDirectory(Path.Combine(_outside, "early-refusal")).FullName;
        File.WriteAllText(Path.Combine(elsewhere, ".git"), "gitdir: elsewhere");

        // Ownership is checked before stdin is read: a one-mebibyte write forces the legitimate refusal to precede completion of the write.
        var result = Run("claude", MutatingClaude("ImplementationReport"), elsewhere, new string('x', 1_048_576));

        Assert.Equal(OwnershipRefused, result.ExitCode);
        Assert.False(File.Exists(Path.Combine(_root, "fixture", "invocations.jsonl")), "A refused invocation logs nothing.");
    }

    [Fact]
    public void A_broken_stdin_delivery_to_an_invocation_that_was_not_refused_is_still_a_failure()
    {
        // A passing verification never reads stdin and exits zero: an undelivered megabyte there is a harness failure, not a refusal.
        File.WriteAllText(_candidate, "public static class Feature { public static int Total(int left, int right) { return left + right; } }");

        Assert.ThrowsAny<IOException>(() => Run("verify", ["check"], standardInput: new string('x', 1_048_576)));
    }
}
