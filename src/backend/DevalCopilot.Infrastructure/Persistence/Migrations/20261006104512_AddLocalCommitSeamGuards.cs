using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevalCopilot.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// ADR-0029: the durable Committing workspace reservation must serialize against competing Agent claims, checkpoint capture,
    /// review recording, verification claims and recipe mutations at the write seam of each of them. SQLite runs every trigger
    /// inside the writer's own write transaction, atomically with the row it guards, so a claim that read Ready before the
    /// reservation can never commit past it. Only a workspace whose status is exactly Committing is affected.
    /// </summary>
    public partial class AddLocalCommitSeamGuards : Migration
    {
        private const string Message = "local_commit.workspace_committing";

        private static readonly (string Name, string Event, string Table, string When)[] Guards =
        [
            (
                "trg_local_commit_guard_agent_attempts",
                "INSERT",
                "attempts",
                "NEW.\"Kind\" = 'Agent' AND EXISTS (SELECT 1 FROM git_workspaces AS w JOIN runs AS r ON r.\"ProjectId\" = w.\"ProjectId\" "
                    + "WHERE r.\"Id\" = NEW.\"RunId\" AND w.\"Status\" = 'Committing')"),
            (
                "trg_local_commit_guard_checkpoints",
                "INSERT",
                "git_checkpoints",
                "EXISTS (SELECT 1 FROM git_workspaces AS w WHERE w.\"Id\" = NEW.\"WorkspaceId\" AND w.\"Status\" = 'Committing')"),
            (
                "trg_local_commit_guard_reviews",
                "INSERT",
                "checkpoint_reviews",
                "EXISTS (SELECT 1 FROM git_workspaces AS w WHERE w.\"Id\" = NEW.\"GitWorkspaceId\" AND w.\"Status\" = 'Committing')"),
            (
                "trg_local_commit_guard_verification_executions",
                "INSERT",
                "verification_executions",
                "EXISTS (SELECT 1 FROM git_workspaces AS w WHERE w.\"Id\" = NEW.\"GitWorkspaceId\" AND w.\"Status\" = 'Committing')"),
            (
                "trg_local_commit_guard_recipes_insert",
                "INSERT",
                "verification_commands",
                "EXISTS (SELECT 1 FROM git_workspaces AS w WHERE w.\"ProjectId\" = NEW.\"ProjectId\" AND w.\"Status\" = 'Committing')"),
            (
                "trg_local_commit_guard_recipes_update",
                "UPDATE",
                "verification_commands",
                "EXISTS (SELECT 1 FROM git_workspaces AS w WHERE w.\"ProjectId\" = NEW.\"ProjectId\" AND w.\"Status\" = 'Committing')"),
            (
                "trg_local_commit_guard_recipes_delete",
                "DELETE",
                "verification_commands",
                "EXISTS (SELECT 1 FROM git_workspaces AS w WHERE w.\"ProjectId\" = OLD.\"ProjectId\" AND w.\"Status\" = 'Committing')"),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var (name, trigger, table, when) in Guards)
            {
                migrationBuilder.Sql(
                    $"CREATE TRIGGER {name} BEFORE {trigger} ON {table} WHEN {when} BEGIN SELECT RAISE(ABORT, '{Message}'); END;");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var (name, _, _, _) in Guards)
            {
                migrationBuilder.Sql($"DROP TRIGGER IF EXISTS {name};");
            }
        }
    }
}
