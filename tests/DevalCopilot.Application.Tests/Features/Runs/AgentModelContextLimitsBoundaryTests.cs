using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// The provider-neutral limits value owns an immutable snapshot of what an adapter reported, and the recording policy refuses
/// a malformed value (null collection, null entry, excessive collection) with the existing fixed error before traversing or
/// copying it, producing no Domain evidence.
/// </summary>
public sealed class AgentModelContextLimitsBoundaryTests
{
    private static readonly AgentModelContextLimitEntry A = new("claude-a", 200000, 32000);
    private static readonly AgentModelContextLimitEntry B = new("claude-b", 1000000, 64000);

    // Counts how many entries anything reads, so a refusal can be proven to happen before a traversal or a copy.
    internal sealed class CountingEntries(int count) : IReadOnlyList<AgentModelContextLimitEntry>
    {
        public int Reads { get; private set; }

        public int Count => count;

        public AgentModelContextLimitEntry this[int index]
        {
            get
            {
                Reads++;
                return new AgentModelContextLimitEntry($"m{index}", 1, 1);
            }
        }

        public IEnumerator<AgentModelContextLimitEntry> GetEnumerator()
        {
            for (var index = 0; index < count; index++)
            {
                Reads++;
                yield return new AgentModelContextLimitEntry($"m{index}", 1, 1);
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static void TryWriteThroughTheCollectionInterfaces(IReadOnlyList<AgentModelContextLimitEntry?> view, AgentModelContextLimitEntry replacement)
    {
        var writes = new List<Action>();
        if (view is IList<AgentModelContextLimitEntry?> list)
        {
            writes.AddRange([() => list[0] = replacement, () => list.Add(replacement), () => list.Clear(), () => list.RemoveAt(0)]);
        }

        if (view is System.Collections.IList legacy)
        {
            writes.AddRange([() => legacy[0] = replacement, () => legacy.Clear()]);
        }

        foreach (var write in writes)
        {
            try
            {
                write();
            }
            catch (Exception exception) when (exception is NotSupportedException or ArgumentException or InvalidCastException)
            {
            }
        }
    }

    [Fact]
    public void The_value_owns_its_entries_so_changing_the_callers_list_afterwards_changes_nothing()
    {
        var source = new List<AgentModelContextLimitEntry> { B, A };
        var limits = new AgentModelContextLimits(TestModelContextLimits.Source, source);

        source[0] = new AgentModelContextLimitEntry("tampered", 777, 7);
        source.Add(new AgentModelContextLimitEntry("extra", 1, 1));
        source.Clear();

        Assert.Equal([B, A], limits.Models.AsEnumerable());
        Assert.Null(AgentModelContextLimitsRecording.Validate(limits, AgentProvider.ClaudeCode, AgentOutcome.ProviderInvocationFailed, true, out var evidence));
        Assert.Equal(TestModelContextLimits.Snapshot, evidence!.Serialize());
    }

    [Fact]
    public void The_value_never_shares_an_array_the_caller_still_holds()
    {
        var source = new[] { B, A };
        var limits = new AgentModelContextLimits(TestModelContextLimits.Source, source);

        source[0] = new AgentModelContextLimitEntry("tampered", 777, 7);
        source[1] = new AgentModelContextLimitEntry("bad id", -1, 0);

        Assert.Equal([B, A], limits.Models.AsEnumerable());
    }

    [Fact]
    public void The_value_exposes_no_collection_a_caller_can_write_through()
    {
        var limits = new AgentModelContextLimits(TestModelContextLimits.Source, [B, A]);

        TryWriteThroughTheCollectionInterfaces(limits.Models, new AgentModelContextLimitEntry("tampered", 777, 7));

        Assert.Equal([B, A], limits.Models.AsEnumerable());
        Assert.Null(AgentModelContextLimitsRecording.Validate(limits, AgentProvider.ClaudeCode, AgentOutcome.ProviderInvocationFailed, true, out var evidence));
        Assert.Equal(TestModelContextLimits.Snapshot, evidence!.Serialize());
    }

    public static TheoryData<AgentModelContextLimits> MalformedValues() => new()
    {
        new AgentModelContextLimits(TestModelContextLimits.Source, null!),
        new AgentModelContextLimits(TestModelContextLimits.Source, [A, null!]),
        new AgentModelContextLimits(TestModelContextLimits.Source, [null!]),
        new AgentModelContextLimits(TestModelContextLimits.Source, new CountingEntries(17)),
        new AgentModelContextLimits(TestModelContextLimits.Source, new CountingEntries(1_000_000)),
    };

    [Theory]
    [MemberData(nameof(MalformedValues))]
    public void Recording_refuses_a_malformed_value_with_the_fixed_error_and_produces_no_evidence(AgentModelContextLimits malformed)
    {
        var error = AgentModelContextLimitsRecording.Validate(malformed, AgentProvider.ClaudeCode, AgentOutcome.ProviderInvocationFailed, true, out var evidence);

        Assert.Equal(AgentModelContextLimitsRecording.InvalidEvidenceCode, error?.Code);
        Assert.Null(evidence);
    }

    [Fact]
    public void An_excessive_collection_is_neither_traversed_nor_copied_without_bound()
    {
        var excessive = new CountingEntries(1_000_000);

        var limits = new AgentModelContextLimits(TestModelContextLimits.Source, excessive);
        var error = AgentModelContextLimitsRecording.Validate(limits, AgentProvider.ClaudeCode, AgentOutcome.ProviderInvocationFailed, true, out _);

        Assert.Equal(AgentModelContextLimitsRecording.InvalidEvidenceCode, error?.Code);
        Assert.Equal(0, excessive.Reads);
    }

    [Fact]
    public void An_uncounted_sequence_is_read_only_to_the_first_excess_entry_and_refused_whole()
    {
        var reads = 0;
        IEnumerable<AgentModelContextLimitEntry> Uncounted()
        {
            while (true)
            {
                reads++;
                yield return new AgentModelContextLimitEntry($"m{reads}", 1, 1);
            }
        }

        var limits = new AgentModelContextLimits(TestModelContextLimits.Source, Uncounted());
        var error = AgentModelContextLimitsRecording.Validate(
            limits, AgentProvider.ClaudeCode, AgentOutcome.ProviderInvocationFailed, true, out var evidence);

        Assert.Equal(AgentModelContextLimitsEvidence.MaxModels + 1, reads);
        Assert.Equal(AgentModelContextLimitsRecording.InvalidEvidenceCode, error?.Code);
        Assert.Null(evidence);
    }

    [Fact]
    public void A_malformed_value_is_refused_whatever_the_outcome_dispatch_state_or_provider()
    {
        var malformed = new AgentModelContextLimits(TestModelContextLimits.Source, [A, null!]);

        foreach (var outcome in new[] { AgentOutcome.Accepted, AgentOutcome.InvalidStructuredOutput, AgentOutcome.SourceChanged })
        {
            Assert.Equal(
                AgentModelContextLimitsRecording.InvalidEvidenceCode,
                AgentModelContextLimitsRecording.Validate(malformed, AgentProvider.ClaudeCode, outcome, true, out _)?.Code);
        }

        Assert.Equal(
            AgentModelContextLimitsRecording.InvalidEvidenceCode,
            AgentModelContextLimitsRecording.Validate(malformed, AgentProvider.Codex, AgentOutcome.Proposed, true, out _)?.Code);
    }
}
