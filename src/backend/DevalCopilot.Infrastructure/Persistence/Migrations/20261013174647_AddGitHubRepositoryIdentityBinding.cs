using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevalCopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGitHubRepositoryIdentityBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "github_repository_identity_bindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LocalCommitOperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GitWorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ObservationTargetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommitSha = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    BranchName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    RepositoryNodeId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Owner = table.Column<string>(type: "TEXT", maxLength: 39, nullable: false),
                    Repository = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ObservedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_github_repository_identity_bindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_github_repository_identity_bindings_git_workspaces_GitWorkspaceId",
                        column: x => x.GitWorkspaceId,
                        principalTable: "git_workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_github_repository_identity_bindings_github_observation_targets_ObservationTargetId",
                        column: x => x.ObservationTargetId,
                        principalTable: "github_observation_targets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_github_repository_identity_bindings_local_commit_operations_LocalCommitOperationId",
                        column: x => x.LocalCommitOperationId,
                        principalTable: "local_commit_operations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_github_repository_identity_bindings_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_github_repository_identity_bindings_runs_RunId",
                        column: x => x.RunId,
                        principalTable: "runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_github_repository_identity_bindings_event",
                table: "github_repository_identity_bindings",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_github_repository_identity_bindings_GitWorkspaceId",
                table: "github_repository_identity_bindings",
                column: "GitWorkspaceId");

            migrationBuilder.CreateIndex(
                name: "ix_github_repository_identity_bindings_operation",
                table: "github_repository_identity_bindings",
                column: "LocalCommitOperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_github_repository_identity_bindings_ProjectId",
                table: "github_repository_identity_bindings",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "ix_github_repository_identity_bindings_run",
                table: "github_repository_identity_bindings",
                column: "RunId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_github_repository_identity_bindings_target",
                table: "github_repository_identity_bindings",
                column: "ObservationTargetId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "github_repository_identity_bindings");
        }
    }
}
