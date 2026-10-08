using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevalCopilot.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// ADR-0029 R6: the seven write-seam guards of <c>AddLocalCommitSeamGuards</c> tested only a Committing workspace, but an ambiguous
    /// outcome turns the workspace into NeedsAttention while its operation (and run) stay open, and a competing writer could then
    /// commit past an unresolved reservation. Each guard is recreated here over the whole admitted, nonterminal reservation: its
    /// workspace is Committing, or NeedsAttention while an operation of that exact workspace and project is not Completed, Failed or
    /// Interrupted. A historical NeedsAttention workspace, a terminal operation and every other project stay unguarded. SQLite runs
    /// each trigger inside the writer's own write transaction, so the exclusion stays atomic with the row it guards.
    /// </summary>
    public partial class AddLocalCommitAttentionExclusion : Migration
    {
        private const string Message = "local_commit.workspace_committing";

        private const string OpenOperation =
            "EXISTS (SELECT 1 FROM local_commit_operations AS o WHERE o.\"GitWorkspaceId\" = w.\"Id\" AND o.\"ProjectId\" = w.\"ProjectId\" "
            + "AND o.\"Status\" NOT IN ('Completed', 'Failed', 'Interrupted'))";

        private const string Reserved =
            "(w.\"Status\" = 'Committing' OR (w.\"Status\" = 'NeedsAttention' AND " + OpenOperation + "))";

        private const string CommittingOnly = "w.\"Status\" = 'Committing'";

        private const string Workspaces = "git_workspaces AS w";

        private const string WorkspacesOfRun = "git_workspaces AS w JOIN runs AS r ON r.\"ProjectId\" = w.\"ProjectId\"";

        /// <summary>Name, event, table, the extra condition on the written row, the workspace source, and the match to the workspace.</summary>
        private static readonly (string Name, string Event, string Table, string Row, string From, string Match)[] Guards =
        [
            (
                "trg_local_commit_guard_agent_attempts", "INSERT", "attempts", "NEW.\"Kind\" = 'Agent' AND ",
                WorkspacesOfRun, "r.\"Id\" = NEW.\"RunId\""),
            ("trg_local_commit_guard_checkpoints", "INSERT", "git_checkpoints", string.Empty, Workspaces, "w.\"Id\" = NEW.\"WorkspaceId\""),
            ("trg_local_commit_guard_reviews", "INSERT", "checkpoint_reviews", string.Empty, Workspaces, "w.\"Id\" = NEW.\"GitWorkspaceId\""),
            (
                "trg_local_commit_guard_verification_executions", "INSERT", "verification_executions", string.Empty, Workspaces,
                "w.\"Id\" = NEW.\"GitWorkspaceId\""),
            ("trg_local_commit_guard_recipes_insert", "INSERT", "verification_commands", string.Empty, Workspaces, "w.\"ProjectId\" = NEW.\"ProjectId\""),
            ("trg_local_commit_guard_recipes_update", "UPDATE", "verification_commands", string.Empty, Workspaces, "w.\"ProjectId\" = NEW.\"ProjectId\""),
            ("trg_local_commit_guard_recipes_delete", "DELETE", "verification_commands", string.Empty, Workspaces, "w.\"ProjectId\" = OLD.\"ProjectId\""),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) => Recreate(migrationBuilder, Reserved);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => Recreate(migrationBuilder, CommittingOnly);

        private static void Recreate(MigrationBuilder migrationBuilder, string reservation)
        {
            foreach (var (name, trigger, table, row, from, match) in Guards)
            {
                migrationBuilder.Sql($"DROP TRIGGER IF EXISTS {name};");
                migrationBuilder.Sql(
                    $"CREATE TRIGGER {name} BEFORE {trigger} ON {table} WHEN {row}EXISTS (SELECT 1 FROM {from} WHERE {match} AND {reservation}) "
                    + $"BEGIN SELECT RAISE(ABORT, '{Message}'); END;");
            }
        }
    }
}
