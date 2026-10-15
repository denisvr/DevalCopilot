using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevalCopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGitHubBranchPublicationApproval : Migration
    {
        private const string ImmutableMessage = "github_branch_publication_approval.immutable";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "github_branch_publication_approvals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LocalCommitOperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GitWorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommitSha = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    BranchName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    TargetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TargetEventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TargetOwner = table.Column<string>(type: "TEXT", maxLength: 39, nullable: false),
                    TargetRepository = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    TargetRecordedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    BindingId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BindingEventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RepositoryNodeId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    BindingOwner = table.Column<string>(type: "TEXT", maxLength: 39, nullable: false),
                    BindingRepository = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    BindingObservedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    BindingRecordedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_github_branch_publication_approvals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_github_branch_publication_approvals_git_workspaces_GitWorkspaceId",
                        column: x => x.GitWorkspaceId,
                        principalTable: "git_workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_github_branch_publication_approvals_github_observation_targets_TargetId",
                        column: x => x.TargetId,
                        principalTable: "github_observation_targets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_github_branch_publication_approvals_github_repository_identity_bindings_BindingId",
                        column: x => x.BindingId,
                        principalTable: "github_repository_identity_bindings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_github_branch_publication_approvals_local_commit_operations_LocalCommitOperationId",
                        column: x => x.LocalCommitOperationId,
                        principalTable: "local_commit_operations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_github_branch_publication_approvals_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_github_branch_publication_approvals_runs_RunId",
                        column: x => x.RunId,
                        principalTable: "runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_github_branch_publication_approvals_binding",
                table: "github_branch_publication_approvals",
                column: "BindingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_github_branch_publication_approvals_event",
                table: "github_branch_publication_approvals",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_github_branch_publication_approvals_GitWorkspaceId",
                table: "github_branch_publication_approvals",
                column: "GitWorkspaceId");

            migrationBuilder.CreateIndex(
                name: "ix_github_branch_publication_approvals_operation",
                table: "github_branch_publication_approvals",
                column: "LocalCommitOperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_github_branch_publication_approvals_ProjectId",
                table: "github_branch_publication_approvals",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "ix_github_branch_publication_approvals_run",
                table: "github_branch_publication_approvals",
                column: "RunId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_github_branch_publication_approvals_target",
                table: "github_branch_publication_approvals",
                column: "TargetId",
                unique: true);

            // ADR-0048: an approval is immutable consent. SQLite runs a trigger inside the writer's own transaction, so no statement can
            // update or delete a recorded approval, whatever the caller, and the restrictive foreign keys keep what it names in place.
            migrationBuilder.Sql(
                "CREATE TRIGGER trg_github_branch_publication_approvals_no_update BEFORE UPDATE ON github_branch_publication_approvals "
                + $"BEGIN SELECT RAISE(ABORT, '{ImmutableMessage}'); END;");
            migrationBuilder.Sql(
                "CREATE TRIGGER trg_github_branch_publication_approvals_no_delete BEFORE DELETE ON github_branch_publication_approvals "
                + $"BEGIN SELECT RAISE(ABORT, '{ImmutableMessage}'); END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_github_branch_publication_approvals_no_update;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_github_branch_publication_approvals_no_delete;");
            migrationBuilder.DropTable(
                name: "github_branch_publication_approvals");
        }
    }
}
