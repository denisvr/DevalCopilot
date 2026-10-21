using System.Data.Common;
using System.Net;
using DevalCopilot.Application.Features.Runs.Commands.BindGitHubRepositoryIdentity;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubRepositoryIdentityBinding;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// The binding reader reads its binding candidates and the run's bound-event candidates in one statement (ADR-0043), so a competing
/// protected POST that commits the atomic binding-and-event pair is seen entirely or not at all, never as a mixed reading that the handler
/// reports as unavailable. Each test synchronizes at an exact point of the real reader, proves the competing commit really happened there and
/// that the read was one statement naming both tables (so the hook cannot silently stop firing and turn the test vacuous), and then asserts
/// the coherent before or after reading and the canonical replay or read-model result.
/// </summary>
public sealed class GitHubRepositoryIdentityBindingReadConsistencyTests : GitHubRepositoryIdentityBindingTestBase
{
    private const string BindingTable = "\"github_repository_identity_bindings\"";
    private const string EventTable = "\"events\"";

    /// <summary>Commits a competing pair once, immediately before the reader's statement executes.</summary>
    private sealed class BeforeStatement(Func<Task> commit) : DbCommandInterceptor
    {
        public List<string> BindingStatements { get; } = [];

        public bool Committed { get; private set; }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(BindingTable, StringComparison.Ordinal))
            {
                BindingStatements.Add(command.CommandText);
                if (!Committed)
                {
                    Committed = true;
                    await commit();
                }
            }

            return result;
        }
    }

    /// <summary>Commits a competing pair once at the next transaction start after the reader's statement (SQLite takes its write lock when
    /// the final Run lock transaction begins, so the competitor must commit first).</summary>
    private sealed class AfterStatement(Func<Task> commit)
    {
        public List<string> BindingStatements { get; } = [];

        public bool Committed { get; private set; }

        private Func<Task> Commit => commit;

        public DbCommandInterceptor Commands => new Statements(this);

        public DbTransactionInterceptor Transactions => new Starts(this);

        private sealed class Statements(AfterStatement owner) : DbCommandInterceptor
        {
            public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
                DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
            {
                if (command.CommandText.Contains(BindingTable, StringComparison.Ordinal))
                {
                    owner.BindingStatements.Add(command.CommandText);
                }

                return ValueTask.FromResult(result);
            }
        }

        private sealed class Starts(AfterStatement owner) : DbTransactionInterceptor
        {
            public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
                DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result,
                CancellationToken cancellationToken = default)
            {
                if (owner.BindingStatements.Count > 0 && !owner.Committed)
                {
                    owner.Committed = true;
                    await owner.Commit();
                }

                return result;
            }
        }
    }

    private DevalCopilotDbContext ObserverDb(params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<DevalCopilotDbContext>().UseSqlite($"Data Source={DatabasePath}").AddInterceptors(interceptors).Options);

    private Func<Task> CompetingPostAsync(Delivered delivered) => async () =>
    {
        var writer = await PostBindingAsync(delivered.Host, delivered.Ids.RunId, BodyFor(delivered));
        Assert.Equal(HttpStatusCode.OK, writer.Status);
        Assert.Equal((1, 1), await BindingCountsAsync(delivered.Ids.RunId));
    };

    private static void AssertOneStatementNamingBothTables(IReadOnlyList<string> bindingStatements)
    {
        // The first statement that touches the bindings table is the reader's, and it is the same statement that reads the events.
        Assert.NotEmpty(bindingStatements);
        Assert.Contains(EventTable, bindingStatements[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_pair_committed_just_before_the_reader_statement_is_read_whole_and_replayed()
    {
        var delivered = await DeliveredAsync();
        var hook = new BeforeStatement(CompetingPostAsync(delivered));
        await using var db = ObserverDb(hook);
        var handler = new BindGitHubRepositoryIdentityCommandHandler(db, delivered.Observer, TimeProvider.System);

        var result = await handler.HandleAsync(new BindGitHubRepositoryIdentityCommand(
            delivered.Ids.RunId, delivered.OperationId, delivered.Sha, delivered.TargetId, NodeId), CancellationToken.None);

        Assert.True(hook.Committed, "The competing pair was never committed at the synchronization point.");
        AssertOneStatementNamingBothTables(hook.BindingStatements);
        Assert.True(result.IsSuccess, string.Join(",", result.Errors.Select(error => error.Code)));
        Assert.Equal((1, 1), await BindingCountsAsync(delivered.Ids.RunId));
    }

    [Fact]
    public async Task A_pair_committed_after_the_reader_statement_is_absent_from_it_and_replayed_under_the_run_lock()
    {
        var delivered = await DeliveredAsync();
        var hook = new AfterStatement(CompetingPostAsync(delivered));
        await using var db = ObserverDb(hook.Commands, hook.Transactions);
        var handler = new BindGitHubRepositoryIdentityCommandHandler(db, delivered.Observer, TimeProvider.System);

        var result = await handler.HandleAsync(new BindGitHubRepositoryIdentityCommand(
            delivered.Ids.RunId, delivered.OperationId, delivered.Sha, delivered.TargetId, NodeId), CancellationToken.None);

        Assert.True(hook.Committed, "The competing pair was never committed at the synchronization point.");
        AssertOneStatementNamingBothTables(hook.BindingStatements);
        Assert.True(result.IsSuccess, string.Join(",", result.Errors.Select(error => error.Code)));
        Assert.Equal((1, 1), await BindingCountsAsync(delivered.Ids.RunId));
    }

    [Fact]
    public async Task A_read_model_query_reads_a_pair_committed_just_before_its_reader_statement_whole()
    {
        var delivered = await DeliveredAsync();
        var hook = new BeforeStatement(CompetingPostAsync(delivered));
        await using var db = ObserverDb(hook);

        var seen = await new GetGitHubRepositoryIdentityBindingQueryHandler(db)
            .HandleAsync(new GetGitHubRepositoryIdentityBindingQuery(delivered.Ids.RunId), CancellationToken.None);

        Assert.True(hook.Committed, "The competing pair was never committed at the synchronization point.");
        AssertOneStatementNamingBothTables(hook.BindingStatements);
        Assert.Equal(GitHubRepositoryIdentityBindingState.Available, seen.Value.State);
    }

    [Fact]
    public async Task A_read_model_query_reads_not_recorded_before_a_pair_and_available_after_it_never_unavailable()
    {
        var delivered = await DeliveredAsync();
        var statements = new AfterStatement(CompetingPostAsync(delivered));
        await using var db = ObserverDb(statements.Commands);
        var handler = new GetGitHubRepositoryIdentityBindingQueryHandler(db);

        var first = await handler.HandleAsync(new GetGitHubRepositoryIdentityBindingQuery(delivered.Ids.RunId), CancellationToken.None);
        AssertOneStatementNamingBothTables(statements.BindingStatements);
        Assert.Equal(GitHubRepositoryIdentityBindingState.NotRecorded, first.Value.State);

        await CompetingPostAsync(delivered)();
        var settled = await handler.HandleAsync(new GetGitHubRepositoryIdentityBindingQuery(delivered.Ids.RunId), CancellationToken.None);
        Assert.Equal(GitHubRepositoryIdentityBindingState.Available, settled.Value.State);
    }
}
