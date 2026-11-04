using DevalCopilot.Api.IntegrationTests.ManualQualification;
using Xunit;
using Xunit.Sdk;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

/// <summary>
/// The memory the reader keeps of its message-evidence requests: bound to the attempt and to the closed facts of the comparison,
/// ordered inputs included. An unchanged retry reuses every result and issues nothing; a retry that observes other facts can never
/// qualify on the earlier agreement and asks for nothing more; the stage-wide ceiling, the stop after a failure and the propagation of
/// a cancellation are kept.
/// </summary>
public sealed class MessageEvidenceMemoryTests
{
    private static readonly Guid Attempt = Guid.NewGuid();
    private static readonly Guid Proposal = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    private static MessageEvidenceFacts Facts(Action<FactsBuilder>? change = null)
    {
        var builder = new FactsBuilder();
        change?.Invoke(builder);
        return MessageEvidenceFacts.Create(
            builder.AttemptId, "ClaudeCode", "CriticalReviewer", builder.Outcome, builder.ProcessOutcome, builder.ExitCode, builder.Envelope, builder.Inputs);
    }

    private sealed class FactsBuilder
    {
        public Guid AttemptId { get; set; } = Attempt;

        public string? Outcome { get; set; } = "Challenged";

        public string? ProcessOutcome { get; set; } = "Exited";

        public int? ExitCode { get; set; } = 0;

        public string Envelope { get; set; } = "Recorded";

        public IReadOnlyList<Guid> Inputs { get; set; } = [Proposal];
    }

    private sealed class Spy
    {
        public int Calls { get; private set; }

        public Func<Task<EvidenceRouteComparisonStatus>> Agrees => () =>
        {
            Calls++;
            return Task.FromResult(EvidenceRouteComparisonStatus.Agrees);
        };
    }

    private static async Task<EvidenceRouteComparisonStatus[]> ReadAsync(
        MessageEvidenceMemory memory, Spy spy, MessageEvidenceFacts facts, Guid[] messages, int ceiling = 5)
    {
        var results = new List<EvidenceRouteComparisonStatus>();
        foreach (var message in messages)
        {
            results.Add(await memory.ResolveAsync(message, facts, ceiling, spy.Agrees));
        }

        return [.. results];
    }

    [Fact]
    public async Task An_unchanged_retry_reuses_every_agreement_with_the_same_request_count()
    {
        var memory = new MessageEvidenceMemory();
        var spy = new Spy();
        var messages = new[] { Guid.NewGuid(), Guid.NewGuid() };

        var first = await ReadAsync(memory, spy, Facts(), messages);
        var retry = await ReadAsync(memory, spy, Facts(), messages);

        Assert.Equal([EvidenceRouteComparisonStatus.Agrees, EvidenceRouteComparisonStatus.Agrees], first);
        Assert.Equal(first, retry);
        Assert.Equal(2, spy.Calls);
        Assert.Equal(2, memory.Issued);
    }

    public static TheoryData<string, MessageEvidenceFacts> ChangedFacts => new()
    {
        { "a different attempt", Facts(facts => facts.AttemptId = Other) },
        { "a different outcome", Facts(facts => facts.Outcome = "Accepted") },
        { "a different process outcome", Facts(facts => facts.ProcessOutcome = "TimedOut") },
        { "a different exit code", Facts(facts => facts.ExitCode = 1) },
        { "no exit code", Facts(facts => facts.ExitCode = null) },
        { "a different envelope", Facts(facts => facts.Envelope = "Empty") },
        { "other ordered inputs", Facts(facts => facts.Inputs = [Other]) },
        { "an added input", Facts(facts => facts.Inputs = [Proposal, Other]) },
        { "a removed input", Facts(facts => facts.Inputs = []) },
    };

    [Theory]
    [MemberData(nameof(ChangedFacts))]
    public async Task A_retry_with_changed_facts_cannot_qualify_on_the_stale_agreement_and_asks_for_nothing(
        string change, MessageEvidenceFacts changed)
    {
        Assert.False(string.IsNullOrEmpty(change));
        var memory = new MessageEvidenceMemory();
        var spy = new Spy();
        var messages = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var original = Facts();
        await ReadAsync(memory, spy, original, messages);

        var retry = await ReadAsync(memory, spy, changed, messages);

        Assert.All(retry, status => Assert.Equal(EvidenceRouteComparisonStatus.Disagrees, status));
        Assert.Equal(2, spy.Calls);
        // Even a reply not seen before is never asked about under facts that differ from the compared ones.
        Assert.Equal(
            EvidenceRouteComparisonStatus.NotAttempted,
            await memory.ResolveAsync(Guid.NewGuid(), changed, 5, spy.Agrees));
        Assert.Equal(2, spy.Calls);
        // The drift is irreversible: even the original facts no longer see their agreement, and nothing new can be asked.
        Assert.Equal(EvidenceRouteComparisonStatus.Disagrees, await memory.ResolveAsync(messages[0], original, 5, spy.Agrees));
        Assert.Equal(EvidenceRouteComparisonStatus.NotAttempted, await memory.ResolveAsync(Guid.NewGuid(), original, 5, spy.Agrees));
        Assert.Equal(2, spy.Calls);
        Assert.True(memory.Drifted);
    }

    [Fact]
    public async Task A_return_to_the_original_facts_after_a_drift_neither_reuses_an_agreement_nor_requests_anything()
    {
        var memory = new MessageEvidenceMemory();
        var spy = new Spy();
        var messages = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var original = Facts();
        var changed = Facts(facts => facts.Inputs = [Other]);

        var first = await ReadAsync(memory, spy, original, messages);
        var drifted = await ReadAsync(memory, spy, changed, messages);
        var returned = await ReadAsync(memory, spy, original, messages);
        var again = await ReadAsync(memory, spy, original, [.. messages, Guid.NewGuid()]);

        Assert.All(first, status => Assert.Equal(EvidenceRouteComparisonStatus.Agrees, status));
        Assert.All(drifted, status => Assert.Equal(EvidenceRouteComparisonStatus.Disagrees, status));
        Assert.All(returned, status => Assert.Equal(EvidenceRouteComparisonStatus.Disagrees, status));
        Assert.Equal(EvidenceRouteComparisonStatus.NotAttempted, again[2]);
        Assert.Equal(2, spy.Calls);
        Assert.True(memory.Drifted);
    }

    [Fact]
    public async Task A_drift_observed_by_a_read_that_cannot_request_anything_is_latched_for_the_stage()
    {
        var memory = new MessageEvidenceMemory();
        var spy = new Spy();
        var original = Facts();
        var changed = Facts(facts => facts.Outcome = "Accepted");
        var asked = Guid.NewGuid();
        await memory.ResolveAsync(asked, original, 5, spy.Agrees);

        // A read with an empty listing observes the facts without resolving any reply; a read with a zero ceiling resolves one and
        // can request nothing. Neither may leave the drift unnoticed.
        memory.Observe(changed);
        Assert.True(memory.Drifted);
        var zero = new MessageEvidenceMemory();
        zero.Observe(original);
        Assert.Equal(EvidenceRouteComparisonStatus.NotAttempted, await zero.ResolveAsync(Guid.NewGuid(), changed, 0, spy.Agrees));
        Assert.True(zero.Drifted);

        Assert.Equal(EvidenceRouteComparisonStatus.Disagrees, await memory.ResolveAsync(asked, original, 5, spy.Agrees));
        Assert.Equal(EvidenceRouteComparisonStatus.NotAttempted, await memory.ResolveAsync(Guid.NewGuid(), original, 5, spy.Agrees));
        Assert.Equal(EvidenceRouteComparisonStatus.NotAttempted, await zero.ResolveAsync(Guid.NewGuid(), original, 5, spy.Agrees));
        Assert.Equal(1, spy.Calls);
    }

    [Fact]
    public async Task A_drift_seen_before_any_reply_was_ever_asked_about_still_closes_the_stage()
    {
        var memory = new MessageEvidenceMemory();
        var spy = new Spy();
        var original = Facts();

        memory.Observe(original);
        memory.Observe(Facts(facts => facts.ExitCode = 2));
        var after = await memory.ResolveAsync(Guid.NewGuid(), original, 5, spy.Agrees);

        Assert.Equal(EvidenceRouteComparisonStatus.NotAttempted, after);
        Assert.Equal(0, spy.Calls);
        Assert.True(memory.Drifted);
    }

    [Fact]
    public async Task Observing_the_same_facts_again_never_drifts_and_keeps_the_stable_retry()
    {
        var memory = new MessageEvidenceMemory();
        var spy = new Spy();
        var message = Guid.NewGuid();
        await memory.ResolveAsync(message, Facts(), 5, spy.Agrees);

        memory.Observe(Facts());
        memory.Observe(Facts());

        Assert.False(memory.Drifted);
        Assert.Equal(EvidenceRouteComparisonStatus.Agrees, await memory.ResolveAsync(message, Facts(), 5, spy.Agrees));
        Assert.Equal(1, spy.Calls);
    }

    [Fact]
    public async Task Changed_facts_met_first_on_a_message_never_asked_before_still_close_the_stage()
    {
        var memory = new MessageEvidenceMemory();
        var spy = new Spy();
        var asked = Guid.NewGuid();
        await memory.ResolveAsync(asked, Facts(), 5, spy.Agrees);
        var changed = Facts(facts => facts.Inputs = [Other]);

        var fresh = await memory.ResolveAsync(Guid.NewGuid(), changed, 5, spy.Agrees);
        var remembered = await memory.ResolveAsync(asked, changed, 5, spy.Agrees);

        Assert.Equal(EvidenceRouteComparisonStatus.NotAttempted, fresh);
        Assert.Equal(EvidenceRouteComparisonStatus.Disagrees, remembered);
        Assert.Equal(1, spy.Calls);
    }

    [Fact]
    public async Task A_remembered_failure_or_disagreement_keeps_its_result_under_changed_facts()
    {
        var memory = new MessageEvidenceMemory();
        var failing = Guid.NewGuid();
        var disagreeing = Guid.NewGuid();
        await memory.ResolveAsync(disagreeing, Facts(), 5, () => Task.FromResult(EvidenceRouteComparisonStatus.Disagrees));
        await memory.ResolveAsync(failing, Facts(), 5, () => throw new InvalidOperationException());
        var changed = Facts(facts => facts.Inputs = [Other]);

        Assert.Equal(EvidenceRouteComparisonStatus.Disagrees, await memory.ResolveAsync(disagreeing, changed, 5, () => throw new XunitException("asked")));
        Assert.Equal(EvidenceRouteComparisonStatus.Unavailable, await memory.ResolveAsync(failing, changed, 5, () => throw new XunitException("asked")));
        // Neither result is changed by a return to the original facts after the drift.
        Assert.Equal(EvidenceRouteComparisonStatus.Disagrees, await memory.ResolveAsync(disagreeing, Facts(), 5, () => throw new XunitException("asked")));
        Assert.Equal(EvidenceRouteComparisonStatus.Unavailable, await memory.ResolveAsync(failing, Facts(), 5, () => throw new XunitException("asked")));
    }

    [Fact]
    public async Task The_stage_wide_ceiling_and_the_stop_after_a_failure_are_kept_across_repeated_reads()
    {
        var memory = new MessageEvidenceMemory();
        var spy = new Spy();
        var messages = Enumerable.Range(0, 7).Select(_ => Guid.NewGuid()).ToArray();

        var first = await ReadAsync(memory, spy, Facts(), messages);
        var retry = await ReadAsync(memory, spy, Facts(), messages);

        Assert.Equal(5, spy.Calls);
        Assert.Equal(first, retry);
        Assert.All(first.Take(5), status => Assert.Equal(EvidenceRouteComparisonStatus.Agrees, status));
        Assert.All(first.Skip(5), status => Assert.Equal(EvidenceRouteComparisonStatus.NotAttempted, status));

        var failing = new MessageEvidenceMemory();
        var calls = 0;
        Func<Task<EvidenceRouteComparisonStatus>> request = () =>
        {
            calls++;
            return calls == 2 ? throw new InvalidOperationException() : Task.FromResult(EvidenceRouteComparisonStatus.Agrees);
        };
        var three = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        for (var read = 0; read < 3; read++)
        {
            var results = new List<EvidenceRouteComparisonStatus>();
            foreach (var message in three)
            {
                results.Add(await failing.ResolveAsync(message, Facts(), 5, request));
            }

            Assert.Equal(
                [EvidenceRouteComparisonStatus.Agrees, EvidenceRouteComparisonStatus.Unavailable, EvidenceRouteComparisonStatus.NotAttempted],
                results);
        }

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task A_cancelled_request_is_remembered_as_unavailable_and_still_propagates()
    {
        var memory = new MessageEvidenceMemory();
        var message = Guid.NewGuid();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => memory.ResolveAsync(message, Facts(), 5, () => throw new OperationCanceledException()));

        Assert.Equal(EvidenceRouteComparisonStatus.Unavailable, await memory.ResolveAsync(message, Facts(), 5, () => throw new XunitException("asked")));
        Assert.Equal(EvidenceRouteComparisonStatus.NotAttempted, await memory.ResolveAsync(Guid.NewGuid(), Facts(), 5, () => throw new XunitException("asked")));
        Assert.Equal(1, memory.Issued);
    }

    [Fact]
    public void The_facts_compare_the_complete_ordered_inputs_not_a_set()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        Assert.Equal(Facts(facts => facts.Inputs = [a, b]), Facts(facts => facts.Inputs = [a, b]));
        Assert.NotEqual(Facts(facts => facts.Inputs = [a, b]), Facts(facts => facts.Inputs = [b, a]));
    }
}
