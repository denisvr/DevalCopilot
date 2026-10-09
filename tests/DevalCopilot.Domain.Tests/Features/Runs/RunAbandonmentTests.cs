using System.Globalization;
using System.Text;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

/// <summary>ADR-0031: the reason contract, the single coherence rule and the Run's own abandonment transition.</summary>
public sealed class RunAbandonmentTests
{
    private static readonly DateTimeOffset Created = new(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);

    private static Run Manual() =>
        Run.RecordClassifiedIntent(Guid.NewGuid(), Guid.NewGuid(), 1, RunExecutionMode.ManualAgent, "Plan the increment", Created);

    [Fact]
    public void The_lifecycle_value_is_appended_without_renumbering_any_existing_value()
    {
        Assert.Equal(0, (int)RunLifecycle.Created);
        Assert.Equal(1, (int)RunLifecycle.Running);
        Assert.Equal(2, (int)RunLifecycle.Completed);
        Assert.Equal(3, (int)RunLifecycle.Failed);
        Assert.Equal(4, (int)RunLifecycle.Interrupted);
        Assert.Equal(5, (int)RunLifecycle.Abandoned);
        Assert.Equal(6, Enum.GetValues<RunLifecycle>().Length);
        Assert.Equal("run.abandoned", RunEventType.RunAbandoned);
    }

    // ---- the reason contract ----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Superseded.", "Superseded.")]
    [InlineData("  Superseded by a smaller change  ", "Superseded by a smaller change")]
    [InlineData("First line\r\nSecond line", "First line\nSecond line")]
    [InlineData("\r\n\r\nPadded\r\n", "Padded")]
    [InlineData("Keeps\n\nblank lines", "Keeps\n\nblank lines")]
    [InlineData("Unicode é漢😀", "Unicode é漢😀")]
    public void An_acceptable_reason_is_trimmed_with_crlf_normalized_to_lf(string reason, string expected)
    {
        Assert.True(RunAbandonmentPolicy.TryNormalizeReason(reason, out var normalized));
        Assert.Equal(expected, normalized);
        Assert.True(RunAbandonmentPolicy.IsCanonicalReason(normalized));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n \n")]
    [InlineData("lone\rcarriage return")]
    [InlineData("tab\tseparated")]
    [InlineData("nul\0byte")]
    [InlineData("bell\u0007")]
    [InlineData("delete\u007f")]
    public void An_unacceptable_reason_is_refused_without_a_normalized_form(string? reason)
    {
        Assert.False(RunAbandonmentPolicy.TryNormalizeReason(reason, out var normalized));
        Assert.Equal(string.Empty, normalized);
        Assert.False(RunAbandonmentPolicy.IsCanonicalReason(reason));
    }

    // Built in code: a test-data pipeline would replace a lone surrogate before the policy ever saw it.
    [Fact]
    public void An_unpaired_surrogate_is_refused()
    {
        var lonelyHigh = "high" + (char)0xD83D + " alone";
        var lonelyLow = "alone" + (char)0xDE00 + " low";
        var reversedPair = "reversed" + (char)0xDE00 + (char)0xD83D;

        Assert.False(RunAbandonmentPolicy.TryNormalizeReason(lonelyHigh, out _));
        Assert.False(RunAbandonmentPolicy.TryNormalizeReason(lonelyLow, out _));
        Assert.False(RunAbandonmentPolicy.TryNormalizeReason(reversedPair, out _));
        Assert.True(RunAbandonmentPolicy.TryNormalizeReason("paired " + (char)0xD83D + (char)0xDE00, out _));
    }

    [Theory]
    [InlineData(0x0085)]
    [InlineData(0x2028)]
    [InlineData(0x2029)]
    [InlineData(0x200B)]
    [InlineData(0x200E)]
    [InlineData(0x202E)]
    [InlineData(0xFEFF)]
    public void A_separator_or_invisible_format_character_is_refused_by_code_point(int codePoint)
    {
        var reason = "before" + char.ConvertFromUtf32(codePoint) + "after";

        Assert.False(RunAbandonmentPolicy.TryNormalizeReason(reason, out _));
    }

    // Supplementary-plane Format characters are valid surrogate pairs in UTF-16; the category is a property of the scalar value, so
    // a pair must be classified as one scalar and never skipped.
    [Theory]
    [InlineData(0xE0001)]
    [InlineData(0xE0020)]
    [InlineData(0xE007F)]
    [InlineData(0x1D173)]
    [InlineData(0x110BD)]
    public void A_supplementary_format_character_is_refused_by_scalar_value_wherever_it_appears(int codePoint)
    {
        Assert.Equal(UnicodeCategory.Format, CharUnicodeInfo.GetUnicodeCategory(codePoint));
        var scalar = char.ConvertFromUtf32(codePoint);

        Assert.False(RunAbandonmentPolicy.TryNormalizeReason("before" + scalar + "after", out var middle));
        Assert.Equal(string.Empty, middle);
        Assert.False(RunAbandonmentPolicy.TryNormalizeReason(scalar + "leading", out _));
        Assert.False(RunAbandonmentPolicy.TryNormalizeReason("trailing" + scalar, out _));
        Assert.False(RunAbandonmentPolicy.TryNormalizeReason("😀" + scalar + "😀", out _));
        Assert.False(RunAbandonmentPolicy.IsCanonicalReason("before" + scalar + "after"));
    }

    [Theory]
    [InlineData(0x1F600)]
    [InlineData(0x10000)]
    [InlineData(0x1D11E)]
    [InlineData(0x20000)]
    [InlineData(0xE0100)]
    [InlineData(0x10FFFD)]
    public void Ordinary_supplementary_text_is_accepted_and_kept_exactly(int codePoint)
    {
        Assert.NotEqual(UnicodeCategory.Format, CharUnicodeInfo.GetUnicodeCategory(codePoint));
        Assert.NotEqual(UnicodeCategory.Control, CharUnicodeInfo.GetUnicodeCategory(codePoint));
        var reason = "before " + char.ConvertFromUtf32(codePoint) + " after";

        Assert.True(RunAbandonmentPolicy.TryNormalizeReason(reason, out var normalized));
        Assert.Equal(reason, normalized);
        Assert.True(RunAbandonmentPolicy.IsCanonicalReason(reason));
    }

    [Fact]
    public void A_supplementary_scalar_counts_as_four_utf8_bytes_in_the_bound()
    {
        // 512 four-byte scalars are exactly 2048 bytes; one more scalar is over the bound.
        var exact = string.Concat(Enumerable.Repeat(char.ConvertFromUtf32(0x1F600), 512));
        Assert.Equal(RunAbandonmentPolicy.MaximumReasonUtf8Bytes, Encoding.UTF8.GetByteCount(exact));
        Assert.True(RunAbandonmentPolicy.TryNormalizeReason(exact, out _));
        Assert.False(RunAbandonmentPolicy.TryNormalizeReason(exact + char.ConvertFromUtf32(0x1F600), out _));
    }

    [Fact]
    public void The_bound_is_two_kibibytes_of_utf8_not_characters()
    {
        var exact = new string('a', RunAbandonmentPolicy.MaximumReasonUtf8Bytes);
        Assert.True(RunAbandonmentPolicy.TryNormalizeReason(exact, out _));
        Assert.False(RunAbandonmentPolicy.TryNormalizeReason(exact + "a", out _));

        // 683 three-byte characters are 2049 bytes though only 683 characters.
        var multibyte = new string('漢', 683);
        Assert.Equal(2049, Encoding.UTF8.GetByteCount(multibyte));
        Assert.False(RunAbandonmentPolicy.TryNormalizeReason(multibyte, out _));
        Assert.True(RunAbandonmentPolicy.TryNormalizeReason(new string('漢', 682), out _));
    }

    [Theory]
    [InlineData(" padded")]
    [InlineData("crlf\r\nline")]
    public void A_reason_that_is_not_already_normalized_is_not_canonical(string reason) =>
        Assert.False(RunAbandonmentPolicy.IsCanonicalReason(reason));

    // ---- the transition -----------------------------------------------------------------------------------------------------

    [Fact]
    public void A_created_run_is_abandoned_without_counting_its_created_time_and_keeps_its_stage()
    {
        var run = Manual();
        var at = Created.AddHours(5);

        run.Abandon("No longer wanted", at);

        Assert.Equal(RunLifecycle.Abandoned, run.Lifecycle);
        Assert.Equal(RunStage.Intake, run.Stage);
        Assert.Equal(0, run.AccumulatedAutonomousSeconds);
        Assert.Equal(at, run.LastAdvancedAtUtc);
        Assert.Equal(at, run.AbandonedAtUtc);
        Assert.Equal("No longer wanted", run.AbandonmentReason);
        Assert.Equal(ParticipantIdentity.None(), run.ActiveParticipant);
    }

    [Fact]
    public void A_running_run_freezes_its_accumulated_time_up_to_the_abandonment_and_keeps_its_stage()
    {
        var run = Manual();
        run.Claim(Created.AddSeconds(10));
        run.AdvanceStage(RunStage.Plan, ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex), Created.AddSeconds(30));
        var at = Created.AddSeconds(100);

        run.Abandon("Stopped after planning", at);

        Assert.Equal(RunLifecycle.Abandoned, run.Lifecycle);
        Assert.Equal(RunStage.Plan, run.Stage);
        Assert.Equal(90, run.AccumulatedAutonomousSeconds);
        Assert.Equal(at, run.AbandonedAtUtc);
        Assert.Equal(ParticipantIdentity.None(), run.ActiveParticipant);
        Assert.Null(run.ActiveAgentRole);
        Assert.Null(run.ActiveAgentProvider);
    }

    [Theory]
    [InlineData(RunLifecycle.Completed)]
    [InlineData(RunLifecycle.Failed)]
    [InlineData(RunLifecycle.Interrupted)]
    [InlineData(RunLifecycle.Abandoned)]
    public void No_terminal_run_can_be_abandoned_and_nothing_changes(RunLifecycle terminal)
    {
        var run = Manual();
        run.Claim(Created);
        switch (terminal)
        {
            case RunLifecycle.Completed: run.Complete(Created.AddSeconds(1)); break;
            case RunLifecycle.Failed: run.Fail(Created.AddSeconds(1)); break;
            case RunLifecycle.Interrupted: run.MarkInterrupted(Created.AddSeconds(1)); break;
            default: run.Abandon("First reason", Created.AddSeconds(1)); break;
        }

        var reasonBefore = run.AbandonmentReason;
        var atBefore = run.AbandonedAtUtc;
        var seconds = run.AccumulatedAutonomousSeconds;

        Assert.Throws<InvalidOperationException>(() => run.Abandon("Second reason", Created.AddSeconds(50)));

        Assert.Equal(terminal, run.Lifecycle);
        Assert.Equal(reasonBefore, run.AbandonmentReason);
        Assert.Equal(atBefore, run.AbandonedAtUtc);
        Assert.Equal(seconds, run.AccumulatedAutonomousSeconds);
    }

    [Theory]
    [InlineData(RunExecutionMode.Simulated)]
    public void A_non_manual_run_cannot_be_abandoned(RunExecutionMode mode)
    {
        var run = Run.RecordClassifiedIntent(Guid.NewGuid(), Guid.NewGuid(), 1, mode, "Objective", Created);

        Assert.Throws<InvalidOperationException>(() => run.Abandon("Reason", Created.AddSeconds(1)));
        Assert.Equal(RunLifecycle.Created, run.Lifecycle);
    }

    [Fact]
    public void A_legacy_run_cannot_be_abandoned()
    {
        var run = Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Legacy objective", Created);

        Assert.Throws<InvalidOperationException>(() => run.Abandon("Reason", Created.AddSeconds(1)));
        Assert.Equal(RunLifecycle.Created, run.Lifecycle);
        Assert.Null(run.AbandonmentReason);
    }

    [Theory]
    [InlineData(" padded ")]
    [InlineData("crlf\r\nline")]
    [InlineData("   ")]
    public void Only_an_already_normalized_reason_is_stored(string reason)
    {
        var run = Manual();

        Assert.Throws<ArgumentException>(() => run.Abandon(reason, Created.AddSeconds(1)));
        Assert.Equal(RunLifecycle.Created, run.Lifecycle);
        Assert.Null(run.AbandonmentReason);
        Assert.Null(run.AbandonedAtUtc);
    }

    [Fact]
    public void An_abandoned_run_accepts_no_further_policy_change()
    {
        var run = Manual();
        run.Abandon("Done", Created.AddSeconds(1));

        Assert.Throws<InvalidOperationException>(() => run.SetRequestedCodexAssignment("gpt-6-sol", null));
        Assert.Throws<InvalidOperationException>(() => run.SetRequestedClaudeModelRequest("opus", null));
        Assert.Throws<InvalidOperationException>(() => run.SetRequestedClaudeMaxTurns(3));
        Assert.Throws<InvalidOperationException>(() => run.SetCodexAccountUsageStopPercent(50));
        Assert.Throws<InvalidOperationException>(() => run.SetCodexAccountUsageWarningPercent(50));
        Assert.Throws<InvalidOperationException>(() => run.SetTokenStopThreshold(AgentProvider.Codex, 10));
        Assert.Throws<InvalidOperationException>(() => run.SetTokenWarningThreshold(AgentProvider.Codex, 10));
        Assert.Throws<InvalidOperationException>(() => run.Claim(Created.AddSeconds(2)));
        Assert.Throws<InvalidOperationException>(() => run.Complete(Created.AddSeconds(2)));
        Assert.Throws<InvalidOperationException>(() => run.Fail(Created.AddSeconds(2)));
        Assert.Throws<InvalidOperationException>(() => run.MarkInterrupted(Created.AddSeconds(2)));
        Assert.Throws<InvalidOperationException>(() => run.AdvanceStage(RunStage.Plan, ParticipantIdentity.ForOrchestrator(), Created.AddSeconds(2)));
    }

    // ---- the coherence rule -------------------------------------------------------------------------------------------------

    private static RunAbandonmentFacts Coherent(
        RunLifecycle lifecycle = RunLifecycle.Abandoned,
        RunExecutionMode mode = RunExecutionMode.ManualAgent,
        string? reason = "A reason",
        DateTimeOffset? at = null,
        DateTimeOffset? lastAdvanced = null,
        bool noParticipant = true,
        IReadOnlyList<RunAbandonmentEventFacts>? events = null)
    {
        var time = at ?? Created.AddHours(1);
        return new RunAbandonmentFacts(
            lifecycle, mode, reason, at is null && lifecycle == RunLifecycle.Abandoned ? time : at,
            lastAdvanced ?? time, noParticipant, events ?? [new RunAbandonmentEventFacts(true, "A reason", time)]);
    }

    [Fact]
    public void Exactly_the_expected_facts_are_coherent() => Assert.True(RunAbandonmentPolicy.IsCoherent(Coherent()));

    [Fact]
    public void Every_single_departure_from_the_expected_facts_is_incoherent()
    {
        var time = Created.AddHours(1);
        var human = new RunAbandonmentEventFacts(true, "A reason", time);
        var cases = new Dictionary<string, RunAbandonmentFacts>
        {
            ["not abandoned"] = Coherent(lifecycle: RunLifecycle.Completed),
            ["legacy"] = Coherent(mode: RunExecutionMode.Legacy),
            ["simulated"] = Coherent(mode: RunExecutionMode.Simulated),
            ["unrecognized mode"] = Coherent(mode: RunExecutionModeStorage.Unrecognized),
            ["no reason"] = Coherent(reason: null),
            ["blank reason"] = Coherent(reason: "  "),
            ["padded reason"] = Coherent(reason: " A reason "),
            ["no time"] = Coherent() with { AbandonedAtUtc = null },
            ["time differs from the last advance"] = Coherent(lastAdvanced: time.AddSeconds(1)),
            ["active participant"] = Coherent(noParticipant: false),
            ["no event"] = Coherent(events: []),
            ["two events"] = Coherent(events: [human, human]),
            ["agent or attempt event"] = Coherent(events: [human with { IsHumanAndRunScoped = false }]),
            ["other reason in the event"] = Coherent(events: [human with { Reason = "Another" }]),
            ["no reason in the event"] = Coherent(events: [human with { Reason = null }]),
            ["event at another time"] = Coherent(events: [human with { OccurredAtUtc = time.AddSeconds(1) }]),
        };

        foreach (var (name, facts) in cases)
        {
            Assert.False(RunAbandonmentPolicy.IsCoherent(facts), name);
        }
    }

    [Fact]
    public void Only_a_coherent_abandonment_permits_a_new_intent_and_no_other_lifecycle_changes()
    {
        Assert.True(RunLifecycleAdmission.PermitsNewIntent(RunLifecycle.Completed));
        Assert.True(RunLifecycleAdmission.PermitsNewIntent(RunLifecycle.Failed));
        Assert.True(RunLifecycleAdmission.PermitsNewIntent(RunLifecycle.Interrupted));
        Assert.False(RunLifecycleAdmission.PermitsNewIntent(RunLifecycle.Created));
        Assert.False(RunLifecycleAdmission.PermitsNewIntent(RunLifecycle.Running));
        Assert.False(RunLifecycleAdmission.PermitsNewIntent((RunLifecycle)99));

        Assert.False(RunLifecycleAdmission.PermitsNewIntent(RunLifecycle.Abandoned));
        Assert.False(RunLifecycleAdmission.PermitsNewIntent(RunLifecycle.Abandoned, coherentAbandonment: false));
        Assert.True(RunLifecycleAdmission.PermitsNewIntent(RunLifecycle.Abandoned, coherentAbandonment: true));

        // The coherence flag is ignored for every lifecycle but Abandoned: it can neither admit nor block another outcome.
        Assert.False(RunLifecycleAdmission.PermitsNewIntent(RunLifecycle.Running, coherentAbandonment: true));
        Assert.False(RunLifecycleAdmission.PermitsNewIntent((RunLifecycle)99, coherentAbandonment: true));
        Assert.True(RunLifecycleAdmission.PermitsNewIntent(RunLifecycle.Failed, coherentAbandonment: false));
    }
}
