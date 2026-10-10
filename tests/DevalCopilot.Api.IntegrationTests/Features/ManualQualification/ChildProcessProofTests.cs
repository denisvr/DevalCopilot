using DevalCopilot.Api.IntegrationTests.ManualQualification;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

/// <summary>The child-shutdown proof against scripted process tables: ownership by ancestry and identity whatever a process is
/// called, no attribution of unrelated processes, and no proof from a failed or partial read.</summary>
public sealed class ChildProcessProofTests
{
    private const int Self = 100;
    private static readonly DateTime T0 = new(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);

    private sealed class ScriptedTable(params Func<IReadOnlyList<ProcessEntry>>[] reads) : IProcessTable
    {
        private int _next;

        public int Reads => _next;

        public IReadOnlyList<ProcessEntry> Read() => reads[Math.Min(_next++, reads.Length - 1)]();
    }

    private static ProcessEntry Entry(int id, int parent, string name, int minutes) => new(id, parent, name, T0.AddMinutes(minutes));

    private static Func<IReadOnlyList<ProcessEntry>> Table(params ProcessEntry[] entries) => () => entries;

    private static Func<IReadOnlyList<ProcessEntry>> Fails() => () => throw new ProcessTableException("scripted failure");

    private static readonly ProcessEntry Explorer = Entry(10, 1, "explorer.exe", 0);
    private static readonly ProcessEntry Launcher = Entry(Self, 10, "ManualQualification.exe", 1);

    [Fact]
    public async Task A_descendant_with_any_name_that_survives_leaves_the_shutdown_unproven()
    {
        var table = new ScriptedTable(
            Table(Explorer, Launcher),
            Table(Explorer, Launcher, Entry(200, Self, "helper-with-any-name.exe", 5)),
            Table(Explorer, Launcher, Entry(200, Self, "helper-with-any-name.exe", 5)));
        var watch = ChildProcessWatch.Begin(table, Self);

        var proof = await watch.ProveAsync(TimeSpan.Zero);

        Assert.Equal(ChildProof.Unproven("LeftoverAlive"), proof);
    }

    [Fact]
    public async Task A_grandchild_through_a_shell_is_owned_through_new_processes_only()
    {
        var shell = Entry(200, Self, "cmd.exe", 5);
        var helper = Entry(300, 200, "ping.exe", 6);
        var table = new ScriptedTable(Table(Explorer, Launcher), Table(Explorer, Launcher, shell, helper));
        var watch = ChildProcessWatch.Begin(table, Self);

        var leftovers = watch.Leftovers();

        Assert.Equal([200, 300], leftovers.Select(entry => entry.Id).Order());
        Assert.False((await watch.ProveAsync(TimeSpan.Zero)).Proven);
    }

    [Fact]
    public async Task An_orphan_remembered_while_its_parent_lived_is_still_a_leftover_after_the_parent_ended()
    {
        var shell = Entry(200, Self, "cmd.exe", 5);
        var helper = Entry(300, 200, "ping.exe", 6);
        var table = new ScriptedTable(Table(Explorer, Launcher), Table(Explorer, Launcher, shell, helper), Table(Explorer, Launcher, helper));
        var watch = ChildProcessWatch.Begin(table, Self);
        watch.Sample();

        var leftover = Assert.Single(watch.Leftovers());

        Assert.Equal(300, leftover.Id);
        Assert.False((await watch.ProveAsync(TimeSpan.Zero)).Proven);
    }

    [Fact]
    public void A_new_orphan_never_seen_under_a_live_parent_is_attributed_only_when_it_is_named_like_a_provider()
    {
        var unrelated = Entry(400, 999, "someone-elses-tool.exe", 7);
        var providerShaped = Entry(401, 999, "claude.exe", 7);
        var table = new ScriptedTable(Table(Explorer, Launcher), Table(Explorer, Launcher, unrelated, providerShaped));
        var watch = ChildProcessWatch.Begin(table, Self);

        var leftovers = watch.Leftovers();

        Assert.Equal([401], leftovers.Select(entry => entry.Id));
    }

    [Fact]
    public async Task Processes_that_already_ran_and_processes_with_a_live_parent_elsewhere_are_never_attributed()
    {
        var elsewhere = Entry(500, 10, "other-session.exe", 8);
        var preexisting = Entry(600, Self, "old-child-of-a-previous-launcher-instance.exe", 0);
        var table = new ScriptedTable(Table(Explorer, Launcher, preexisting), Table(Explorer, Launcher, preexisting, elsewhere));
        var watch = ChildProcessWatch.Begin(table, Self);

        Assert.Empty(watch.Leftovers());
        Assert.Equal(ChildProof.Stopped, await watch.ProveAsync(TimeSpan.Zero));
    }

    [Fact]
    public void A_reused_process_id_with_another_creation_time_is_a_new_process_not_the_old_one()
    {
        var oldHolder = Entry(700, 10, "was-here-before.exe", 0);
        var reusedUnderUs = Entry(700, Self, "new-helper.exe", 9);
        var table = new ScriptedTable(Table(Explorer, Launcher, oldHolder), Table(Explorer, Launcher, reusedUnderUs));
        var watch = ChildProcessWatch.Begin(table, Self);

        Assert.Equal(700, Assert.Single(watch.Leftovers()).Id);
    }

    [Fact]
    public void A_remembered_descendant_whose_id_was_reused_by_another_process_is_not_a_leftover()
    {
        var helper = Entry(800, Self, "helper.exe", 5);
        var reusedElsewhere = Entry(800, 10, "unrelated.exe", 20);
        var table = new ScriptedTable(Table(Explorer, Launcher), Table(Explorer, Launcher, helper), Table(Explorer, Launcher, reusedElsewhere));
        var watch = ChildProcessWatch.Begin(table, Self);
        watch.Sample();

        Assert.Empty(watch.Leftovers());
    }

    [Fact]
    public void A_parent_that_is_younger_than_its_child_is_a_reused_id_and_is_not_followed()
    {
        var helper = Entry(900, 950, "child.exe", 5);
        var youngerParent = Entry(950, Self, "parent-id-reused.exe", 30);
        var table = new ScriptedTable(Table(Explorer, Launcher), Table(Explorer, Launcher, helper, youngerParent));
        var watch = ChildProcessWatch.Begin(table, Self);

        var ids = watch.Leftovers().Select(entry => entry.Id).ToArray();

        Assert.DoesNotContain(900, ids);
        Assert.Contains(950, ids);
    }

    [Fact]
    public void A_chain_through_a_pre_existing_process_is_not_ours()
    {
        var helper = Entry(1000, 10, "child-of-explorer.exe", 5);
        var table = new ScriptedTable(Table(Explorer, Launcher), Table(Explorer, Launcher, helper));
        var watch = ChildProcessWatch.Begin(table, Self);

        Assert.Empty(watch.Leftovers());
    }

    [Fact]
    public void A_new_child_of_a_process_that_already_ran_under_this_launcher_before_the_watch_began_is_not_attributed()
    {
        var preexistingChild = Entry(1200, Self, "started-before-the-host.exe", 2);
        var newGrandchild = Entry(1201, 1200, "started-by-it.exe", 9);
        var table = new ScriptedTable(Table(Explorer, Launcher, preexistingChild), Table(Explorer, Launcher, preexistingChild, newGrandchild));
        var watch = ChildProcessWatch.Begin(table, Self);

        Assert.Empty(watch.Leftovers());
    }

    [Fact]
    public void An_unknown_creation_time_is_an_identity_that_still_counts_as_a_leftover_when_alive()
    {
        var helper = new ProcessEntry(1100, Self, "helper.exe");
        var table = new ScriptedTable(Table(Explorer, Launcher), Table(Explorer, Launcher, helper));
        var watch = ChildProcessWatch.Begin(table, Self);

        Assert.Equal(1100, Assert.Single(watch.Leftovers()).Id);
    }

    [Fact]
    public async Task A_failed_final_read_is_never_an_empty_proof()
    {
        var table = new ScriptedTable(Table(Explorer, Launcher), Fails());
        var watch = ChildProcessWatch.Begin(table, Self);

        Assert.Equal(ChildProof.Unproven("EnumerationFailed"), await watch.ProveAsync(TimeSpan.Zero));
    }

    [Fact]
    public async Task A_failed_sample_leaves_a_gap_that_a_later_clean_read_cannot_close()
    {
        var table = new ScriptedTable(Table(Explorer, Launcher), Fails(), Table(Explorer, Launcher));
        var watch = ChildProcessWatch.Begin(table, Self);
        watch.Sample();

        Assert.Equal(ChildProof.Unproven("EnumerationFailed"), await watch.ProveAsync(TimeSpan.Zero));
    }

    [Fact]
    public async Task A_failed_baseline_read_leaves_the_proof_unproven()
    {
        var table = new ScriptedTable(Fails(), Table(Explorer, Launcher));
        var watch = ChildProcessWatch.Begin(table, Self);

        Assert.Equal(ChildProof.Unproven("EnumerationFailed"), await watch.ProveAsync(TimeSpan.Zero));
    }

    [Fact]
    public async Task A_clean_table_without_any_new_descendant_is_proven()
    {
        var table = new ScriptedTable(Table(Explorer, Launcher), Table(Explorer, Launcher), Table(Explorer, Launcher));
        var watch = ChildProcessWatch.Begin(table, Self);
        watch.Sample();

        Assert.Equal(ChildProof.Stopped, await watch.ProveAsync(TimeSpan.Zero));
    }

    [Fact]
    public async Task Background_sampling_remembers_a_short_lived_parent_chain_and_stops_after_the_proof()
    {
        var shell = Entry(200, Self, "cmd.exe", 5);
        var helper = Entry(300, 200, "ping.exe", 6);
        var table = new ScriptedTable(Table(Explorer, Launcher), Table(Explorer, Launcher, shell, helper), Table(Explorer, Launcher, helper));
        var watch = ChildProcessWatch.Begin(table, Self);
        watch.StartSampling(TimeSpan.FromMilliseconds(5));
        await Task.Delay(200);

        var proof = await watch.ProveAsync(TimeSpan.Zero);
        var readsAtProof = table.Reads;
        await Task.Delay(100);

        Assert.Equal(ChildProof.Unproven("LeftoverAlive"), proof);
        Assert.Equal(readsAtProof, table.Reads);
    }

    private static ProcessEntry NoTime(int id, int parent, string name) => new(id, parent, name);

    [Fact]
    public async Task A_new_orphan_of_any_name_first_seen_after_its_parent_is_gone_leaves_the_origin_unresolved()
    {
        var orphan = Entry(1300, 999, "unremarkable-name.exe", 9);
        var table = new ScriptedTable(Table(Explorer, Launcher), Table(Explorer, Launcher, orphan));
        var watch = ChildProcessWatch.Begin(table, Self);

        Assert.Empty(watch.Leftovers());
        Assert.Equal(ChildProof.Unproven("OriginUnresolved"), await watch.ProveAsync(TimeSpan.Zero));
    }

    [Fact]
    public async Task A_remembered_descendant_whose_creation_time_is_now_unreadable_is_not_proven_gone()
    {
        var shell = Entry(200, Self, "cmd.exe", 5);
        var helper = Entry(300, 200, "helper.exe", 6);
        var table = new ScriptedTable(Table(Explorer, Launcher), Table(Explorer, Launcher, shell, helper), Table(Explorer, Launcher, NoTime(300, 200, "helper.exe")));
        var watch = ChildProcessWatch.Begin(table, Self);
        watch.Sample();

        Assert.Equal(ChildProof.Unproven("IdentityUnavailable"), await watch.ProveAsync(TimeSpan.Zero));
    }

    [Fact]
    public async Task A_remembered_descendant_first_seen_without_a_creation_time_is_not_proven_gone_when_it_gains_one()
    {
        var table = new ScriptedTable(
            Table(Explorer, Launcher), Table(Explorer, Launcher, NoTime(300, Self, "helper.exe")), Table(Explorer, Launcher, Entry(300, 200, "helper.exe", 6)));
        var watch = ChildProcessWatch.Begin(table, Self);
        watch.Sample();

        Assert.Equal(ChildProof.Unproven("IdentityUnavailable"), await watch.ProveAsync(TimeSpan.Zero));
    }

    [Fact]
    public async Task A_remembered_descendant_with_the_same_unavailable_identity_is_still_a_leftover()
    {
        var table = new ScriptedTable(
            Table(Explorer, Launcher), Table(Explorer, Launcher, NoTime(300, Self, "helper.exe")), Table(Explorer, Launcher, NoTime(300, Self, "helper.exe")));
        var watch = ChildProcessWatch.Begin(table, Self);
        watch.Sample();

        Assert.Equal(ChildProof.Unproven("LeftoverAlive"), await watch.ProveAsync(TimeSpan.Zero));
    }

    [Fact]
    public async Task A_remembered_descendant_whose_id_now_has_a_positively_different_identity_is_gone()
    {
        var helper = Entry(800, Self, "helper.exe", 5);
        var reusedElsewhere = Entry(800, 10, "unrelated.exe", 20);
        var table = new ScriptedTable(Table(Explorer, Launcher), Table(Explorer, Launcher, helper), Table(Explorer, Launcher, reusedElsewhere));
        var watch = ChildProcessWatch.Begin(table, Self);
        watch.Sample();

        Assert.Equal(ChildProof.Stopped, await watch.ProveAsync(TimeSpan.Zero));
    }

    [Fact]
    public async Task A_new_process_under_a_parent_whose_creation_time_is_unreadable_is_not_proven_unrelated()
    {
        var parent = NoTime(1350, 10, "parent.exe");
        var child = Entry(1400, 1350, "child.exe", 9);
        var table = new ScriptedTable(Table(Explorer, Launcher), Table(Explorer, Launcher, parent, child));
        var watch = ChildProcessWatch.Begin(table, Self);

        Assert.Equal(ChildProof.Unproven("OriginUnresolved"), await watch.ProveAsync(TimeSpan.Zero));
    }

    [Fact]
    public async Task A_new_process_whose_parent_id_now_has_an_unreadable_identity_is_not_proven_unrelated()
    {
        var child = Entry(1500, 10, "child.exe", 9);
        var table = new ScriptedTable(Table(Explorer, Launcher), Table(NoTime(10, 1, "explorer.exe"), Launcher, child));
        var watch = ChildProcessWatch.Begin(table, Self);

        Assert.Equal(ChildProof.Unproven("OriginUnresolved"), await watch.ProveAsync(TimeSpan.Zero));
    }

    [Fact]
    public async Task A_new_process_whose_parent_id_was_reused_by_a_younger_process_is_not_proven_unrelated()
    {
        var child = Entry(900, 950, "child.exe", 5);
        var youngerParent = Entry(950, 10, "parent-id-reused.exe", 30);
        var table = new ScriptedTable(Table(Explorer, Launcher), Table(Explorer, Launcher, child, youngerParent));
        var watch = ChildProcessWatch.Begin(table, Self);

        Assert.Equal(ChildProof.Unproven("OriginUnresolved"), await watch.ProveAsync(TimeSpan.Zero));
    }

    [Fact]
    public async Task A_new_descendant_of_a_parent_with_an_unreadable_creation_time_under_the_launcher_is_a_possible_leftover()
    {
        var parent = NoTime(1350, Self, "parent.exe");
        var child = Entry(1400, 1350, "child.exe", 9);
        var table = new ScriptedTable(Table(Explorer, Launcher), Table(Explorer, Launcher, parent, child));
        var watch = ChildProcessWatch.Begin(table, Self);

        Assert.Equal(ChildProof.Unproven("LeftoverAlive"), await watch.ProveAsync(TimeSpan.Zero));
    }

    [Fact]
    public async Task An_orphan_that_was_resolved_as_unrelated_while_its_parent_lived_is_not_an_unresolved_orphan_later()
    {
        var parent = Entry(450, 10, "unrelated-parent.exe", 7);
        var child = Entry(460, 450, "unrelated-child.exe", 8);
        var table = new ScriptedTable(
            Table(Explorer, Launcher), Table(Explorer, Launcher, parent, child), Table(Explorer, Launcher, child));
        var watch = ChildProcessWatch.Begin(table, Self);
        watch.Sample();

        Assert.Equal(ChildProof.Stopped, await watch.ProveAsync(TimeSpan.Zero));
    }

    [Fact]
    public async Task Unchanged_pre_existing_processes_with_unavailable_timestamps_are_not_a_failure()
    {
        var system = NoTime(4, 0, "System");
        var registry = NoTime(88, 4, "Registry");
        var flaky = Entry(700, 10, "flaky-query.exe", 0);
        var flakyOrphan = Entry(710, 999, "old-orphan.exe", 0);
        var table = new ScriptedTable(
            Table(Explorer, Launcher, system, registry, flaky, flakyOrphan),
            Table(Explorer, Launcher, system, registry, NoTime(700, 10, "flaky-query.exe"), NoTime(710, 999, "old-orphan.exe")));
        var watch = ChildProcessWatch.Begin(table, Self);

        Assert.Equal(ChildProof.Stopped, await watch.ProveAsync(TimeSpan.Zero));
    }

    [Fact]
    public async Task A_new_child_of_a_protected_process_with_an_unavailable_timestamp_is_not_proven_unrelated()
    {
        var system = NoTime(4, 0, "System");
        var table = new ScriptedTable(
            Table(Explorer, Launcher, system), Table(Explorer, Launcher, system, NoTime(1600, 4, "child-of-system.exe")));
        var watch = ChildProcessWatch.Begin(table, Self);

        Assert.Equal(ChildProof.Unproven("OriginUnresolved"), await watch.ProveAsync(TimeSpan.Zero));
    }

    [Fact]
    public async Task A_new_child_with_a_known_time_under_an_unreadable_pre_existing_parent_is_not_proven_unrelated()
    {
        var parent = NoTime(700, 10, "preexisting-parent.exe");
        var helper = Entry(701, 700, "helper.exe", 9);
        var table = new ScriptedTable(Table(Explorer, Launcher, parent), Table(Explorer, Launcher, parent, helper));
        var watch = ChildProcessWatch.Begin(table, Self);

        Assert.Equal(ChildProof.Unproven("OriginUnresolved"), await watch.ProveAsync(TimeSpan.Zero));
    }

    [Fact]
    public async Task A_new_child_without_a_creation_time_is_not_ordered_after_its_known_pre_existing_parent()
    {
        var helper = NoTime(701, 10, "helper.exe");
        var table = new ScriptedTable(Table(Explorer, Launcher), Table(Explorer, Launcher, helper));
        var watch = ChildProcessWatch.Begin(table, Self);

        Assert.Equal(ChildProof.Unproven("OriginUnresolved"), await watch.ProveAsync(TimeSpan.Zero));
    }

    [Fact]
    public async Task A_baseline_id_that_reappears_unreadable_with_another_name_and_a_gone_parent_is_unresolved()
    {
        var before = Entry(700, 10, "preexisting.exe", 0);
        var after = NoTime(700, 999, "new-helper.exe");
        var table = new ScriptedTable(Table(Explorer, Launcher, before), Table(Explorer, Launcher, after));
        var watch = ChildProcessWatch.Begin(table, Self);

        Assert.Equal(ChildProof.Unproven("OriginUnresolved"), await watch.ProveAsync(TimeSpan.Zero));
    }

    [Fact]
    public async Task A_baseline_id_that_changed_only_its_parent_or_only_its_name_is_unresolved_when_unreadable()
    {
        var before = NoTime(700, 10, "preexisting.exe");
        var renamed = NoTime(700, 10, "other-name.exe");
        var reparented = NoTime(700, 999, "preexisting.exe");

        foreach (var after in new[] { renamed, reparented })
        {
            var table = new ScriptedTable(Table(Explorer, Launcher, before), Table(Explorer, Launcher, after));
            var watch = ChildProcessWatch.Begin(table, Self);

            Assert.False((await watch.ProveAsync(TimeSpan.Zero)).Proven);
        }
    }

    [Fact]
    public async Task A_new_process_under_an_ambiguously_reused_baseline_id_is_not_declared_owned()
    {
        var before = Entry(1900, Self, "started-before-the-host.exe", 2);
        var reused = NoTime(1900, Self, "another-program.exe");
        var grandchild = Entry(1901, 1900, "grandchild.exe", 9);
        var table = new ScriptedTable(Table(Explorer, Launcher, before), Table(Explorer, Launcher, reused, grandchild));
        var watch = ChildProcessWatch.Begin(table, Self);

        Assert.Equal([1900], watch.Leftovers().Select(entry => entry.Id));
        Assert.False((await watch.ProveAsync(TimeSpan.Zero)).Proven);
    }

    [Fact]
    public void A_baseline_id_that_reappears_unreadable_as_a_provider_named_orphan_is_a_leftover()
    {
        var before = Entry(700, 10, "preexisting.exe", 0);
        var after = NoTime(700, 999, "node.exe");
        var table = new ScriptedTable(Table(Explorer, Launcher, before), Table(Explorer, Launcher, after));
        var watch = ChildProcessWatch.Begin(table, Self);

        Assert.Equal([700], watch.Leftovers().Select(entry => entry.Id));
    }

    [Fact]
    public async Task An_uncertain_classification_is_not_cached_as_unrelated_for_a_later_orphan()
    {
        var shortLived = Entry(650, 10, "short-lived-preexisting.exe", 2);
        var unordered = NoTime(701, 650, "helper.exe");
        var table = new ScriptedTable(
            Table(Explorer, Launcher, shortLived),
            Table(Explorer, Launcher, shortLived, unordered),
            Table(Explorer, Launcher, unordered));
        var watch = ChildProcessWatch.Begin(table, Self);
        watch.Sample();

        Assert.Equal(ChildProof.Unproven("OriginUnresolved"), await watch.ProveAsync(TimeSpan.Zero));
    }

    [Fact]
    public async Task A_positively_ordered_chain_to_an_unchanged_known_pre_existing_process_stays_unrelated_and_cached()
    {
        var shortLived = Entry(650, 10, "short-lived-preexisting.exe", 2);
        var ordered = Entry(701, 650, "helper.exe", 9);
        var table = new ScriptedTable(
            Table(Explorer, Launcher, shortLived),
            Table(Explorer, Launcher, shortLived, ordered),
            Table(Explorer, Launcher, ordered));
        var watch = ChildProcessWatch.Begin(table, Self);
        watch.Sample();

        Assert.Equal(ChildProof.Stopped, await watch.ProveAsync(TimeSpan.Zero));
    }

    [Fact]
    public async Task A_new_process_under_a_pre_existing_id_whose_identity_is_now_unreadable_is_not_declared_owned()
    {
        var earlierChild = Entry(1900, Self, "started-before-the-host.exe", 2);
        var grandchild = Entry(1901, 1900, "grandchild.exe", 9);
        var table = new ScriptedTable(
            Table(Explorer, Launcher, earlierChild), Table(Explorer, Launcher, NoTime(1900, Self, "started-before-the-host.exe"), grandchild));
        var watch = ChildProcessWatch.Begin(table, Self);

        Assert.Equal([1900], watch.Leftovers().Select(entry => entry.Id));
        Assert.False((await watch.ProveAsync(TimeSpan.Zero)).Proven);
    }

    [Fact]
    public async Task A_new_process_older_than_its_pre_existing_parent_is_not_proven_unrelated()
    {
        var child = Entry(1700, 10, "child.exe", -3);
        var table = new ScriptedTable(Table(Explorer, Launcher), Table(Explorer, Launcher, child));
        var watch = ChildProcessWatch.Begin(table, Self);

        Assert.Equal(ChildProof.Unproven("OriginUnresolved"), await watch.ProveAsync(TimeSpan.Zero));
    }

    [Fact]
    public async Task A_new_process_older_than_the_launcher_whose_parent_id_is_the_launcher_is_not_attributed_to_it()
    {
        var child = Entry(1800, Self, "child.exe", 0);
        var table = new ScriptedTable(Table(Explorer, Launcher), Table(Explorer, Launcher, child));
        var watch = ChildProcessWatch.Begin(table, Self);

        Assert.Empty(watch.Leftovers());
        Assert.Equal(ChildProof.Unproven("OriginUnresolved"), await watch.ProveAsync(TimeSpan.Zero));
    }

    [Fact]
    public async Task An_unresolved_orphan_that_exits_within_the_grace_leaves_no_doubt()
    {
        var orphan = Entry(1300, 999, "short-lived.exe", 9);
        var table = new ScriptedTable(Table(Explorer, Launcher), Table(Explorer, Launcher, orphan), Table(Explorer, Launcher));
        var watch = ChildProcessWatch.Begin(table, Self);

        Assert.Equal(ChildProof.Stopped, await watch.ProveAsync(TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task An_unresolved_orphan_that_stays_through_the_grace_is_reported_unresolved()
    {
        var orphan = Entry(1300, 999, "stays.exe", 9);
        var table = new ScriptedTable(Table(Explorer, Launcher), Table(Explorer, Launcher, orphan));
        var watch = ChildProcessWatch.Begin(table, Self);

        Assert.Equal(ChildProof.Unproven("OriginUnresolved"), await watch.ProveAsync(TimeSpan.FromMilliseconds(300)));
    }

    [Fact]
    public async Task A_provider_named_orphan_is_a_leftover_and_wins_over_an_unresolved_one()
    {
        var unresolved = Entry(400, 999, "someone-elses-tool.exe", 7);
        var providerShaped = Entry(401, 999, "claude.exe", 7);
        var table = new ScriptedTable(Table(Explorer, Launcher), Table(Explorer, Launcher, unresolved, providerShaped));
        var watch = ChildProcessWatch.Begin(table, Self);

        Assert.Equal(ChildProof.Unproven("LeftoverAlive"), await watch.ProveAsync(TimeSpan.Zero));
    }

    [Fact]
    public void The_native_enumeration_ends_only_on_the_documented_end_of_table_error()
    {
        var rows = new Queue<ProcessEntry>([Entry(1, 0, "a.exe", 0), Entry(2, 1, "b.exe", 0), Entry(3, 1, "c.exe", 0)]);
        ProcessEntry current = rows.Peek();

        var table = ProcessTable.Collect(
            () => { current = rows.Dequeue(); return true; },
            () => { if (rows.Count == 0) { return false; } current = rows.Dequeue(); return true; },
            () => ProcessTable.NoMoreFiles,
            () => current);

        Assert.Equal([1, 2, 3], table.Select(entry => entry.Id));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(0)]
    [InlineData(ProcessTable.NoMoreFiles + 1)]
    public void A_native_enumeration_that_stops_with_any_other_error_is_a_failure_not_a_short_table(int error)
    {
        var step = 0;
        ProcessEntry current = Entry(1, 0, "a.exe", 0);

        Assert.Throws<ProcessTableException>(() => ProcessTable.Collect(
            () => true,
            () => ++step < 3,
            () => error,
            () => current));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(ProcessTable.NoMoreFiles)]
    public void A_native_enumeration_whose_first_step_fails_or_is_empty_is_a_failure(int error)
    {
        Assert.Throws<ProcessTableException>(() => ProcessTable.Collect(
            () => false,
            () => false,
            () => error,
            () => Entry(1, 0, "a.exe", 0)));
    }
}
