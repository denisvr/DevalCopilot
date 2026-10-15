using System.Data.Common;
using DevalCopilot.Application.Features.Runs.Policies.GitHubBranchPublicationApproval;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// ADR-0048 correction R2: the approval rows and the run's approval events come from one consistent database snapshot. A competing
/// writer commits one whole, valid approval and event at the controlled boundary of the reader's own database command; the reading is
/// then whole (never an event without its row), and one database command observes the approval evidence per reader invocation.
/// </summary>
public sealed partial class GitHubBranchPublicationApprovalReaderTests
{
    private enum EvidenceCommand
    {
        RowsOnly,
        EventsOnly,
        RowsAndEvents,
    }

    /// <summary>Records every database command that observes approval evidence (an approval-table or approval-event read) and runs a
    /// callback once, synchronously, before the first one executes. The recorded kinds are fixed phase markers, never SQL text.</summary>
    private sealed class EvidenceBoundaryInterceptor : DbCommandInterceptor
    {
        private int _armed = 1;

        public Func<Task>? AtBoundary { get; init; }

        public int Fired { get; private set; }

        public List<EvidenceCommand> Observed { get; } = [];

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            var rows = command.CommandText.Contains(Approvals, StringComparison.Ordinal);
            var events = command.CommandText.Contains(RunEventType.GitHubBranchPublicationApproved, StringComparison.Ordinal)
                || command.Parameters.Cast<DbParameter>().Any(parameter =>
                    Equals(parameter.Value, RunEventType.GitHubBranchPublicationApproved));
            if (!rows && !events)
            {
                return result;
            }

            Observed.Add(rows && events ? EvidenceCommand.RowsAndEvents : rows ? EvidenceCommand.RowsOnly : EvidenceCommand.EventsOnly);
            if (events && Interlocked.Exchange(ref _armed, 0) == 1 && AtBoundary is { } boundary)
            {
                Fired++;
                await boundary();
            }

            return result;
        }
    }

    private async Task<GitHubBranchPublicationApprovalReading> ReadWithAsync(
        Seeded seeded, EvidenceBoundaryInterceptor interceptor)
    {
        await using var db = _fixture.CreateContext(interceptor);
        return await GitHubBranchPublicationApprovalReader.ReadAsync(db, seeded.Rows.Run.Id, seeded.Scope, CancellationToken.None);
    }

    [Fact]
    public async Task A_competing_commit_at_the_boundary_of_the_reader_command_is_read_whole_never_an_event_without_its_row()
    {
        var seeded = await SeedAsync();
        var approval = ApprovalFor(seeded);
        var interceptor = new EvidenceBoundaryInterceptor
        {
            AtBoundary = async () =>
            {
                await using var writer = _fixture.CreateContext();
                writer.GitHubBranchPublicationApprovals.Add(approval);
                writer.Events.Add(EventFor(seeded.Rows.Run.Id, approval));
                await writer.SaveChangesAsync();
            },
        };

        var reading = await ReadWithAsync(seeded, interceptor);

        Assert.Equal(1, interceptor.Fired);
        Assert.Equal(GitHubBranchPublicationApprovalReadingKind.Coherent, reading.Kind);
        Assert.Equal(approval.Id, reading.Approval!.Id);
        Assert.Equal([EvidenceCommand.RowsAndEvents], interceptor.Observed);
        Assert.Equal(GitHubBranchPublicationApprovalReadingKind.Coherent, (await ReadAsync(seeded)).Kind);
    }

    [Fact]
    public async Task One_database_command_observes_the_approval_evidence_per_reader_invocation_whatever_it_finds()
    {
        var none = await SeedAsync();
        var coherent = await SeedAsync();
        await ApproveAsync(coherent);
        var orphanEvent = await SeedAsync();
        await ApproveAsync(orphanEvent, row: false);
        var orphanRow = await SeedAsync();
        await ApproveAsync(orphanRow, recordEvent: false);

        foreach (var (name, seeded, expected) in new[]
                 {
                     ("none", none, GitHubBranchPublicationApprovalReadingKind.None),
                     ("coherent", coherent, GitHubBranchPublicationApprovalReadingKind.Coherent),
                     ("orphan event", orphanEvent, GitHubBranchPublicationApprovalReadingKind.Incoherent),
                     ("orphan row", orphanRow, GitHubBranchPublicationApprovalReadingKind.Incoherent),
                 })
        {
            var interceptor = new EvidenceBoundaryInterceptor();

            var reading = await ReadWithAsync(seeded, interceptor);

            Assert.True(reading.Kind == expected, $"{name}: {reading.Kind}");
            Assert.True(
                interceptor.Observed.SequenceEqual([EvidenceCommand.RowsAndEvents]),
                $"{name}: {string.Join(',', interceptor.Observed)}");
        }
    }

    [Fact]
    public async Task A_stable_orphan_stays_incoherent_when_nothing_commits_at_the_boundary_and_never_becomes_a_success()
    {
        var seeded = await SeedAsync();
        await ApproveAsync(seeded, row: false);
        var interceptor = new EvidenceBoundaryInterceptor { AtBoundary = () => Task.CompletedTask };

        var reading = await ReadWithAsync(seeded, interceptor);

        Assert.Equal(1, interceptor.Fired);
        Assert.Equal(GitHubBranchPublicationApprovalReadingKind.Incoherent, reading.Kind);
        Assert.Null(reading.Approval);
    }

    [Fact]
    public async Task A_competing_record_for_another_run_that_names_this_runs_target_stays_incoherent_at_the_boundary()
    {
        var seeded = await SeedAsync();
        var other = await SeedAsync();
        var foreign = ApprovalFor(other);
        var interceptor = new EvidenceBoundaryInterceptor
        {
            AtBoundary = async () =>
            {
                await using var writer = _fixture.CreateContext();
                writer.GitHubBranchPublicationApprovals.Add(foreign);
                writer.Events.Add(EventFor(other.Rows.Run.Id, foreign));
                await writer.SaveChangesAsync();
                await ExecuteAsync(
                    $"UPDATE {Approvals} SET \"TargetId\" = '{Upper(seeded.Target.Id)}' WHERE \"Id\" = '{Upper(foreign.Id)}'");
            },
        };

        var reading = await ReadWithAsync(seeded, interceptor);

        Assert.Equal(1, interceptor.Fired);
        Assert.Equal(GitHubBranchPublicationApprovalReadingKind.Incoherent, reading.Kind);
    }
}
