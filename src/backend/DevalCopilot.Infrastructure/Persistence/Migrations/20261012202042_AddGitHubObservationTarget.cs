using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevalCopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGitHubObservationTarget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "github_observation_targets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LocalCommitOperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GitWorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommitSha = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    BranchName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Owner = table.Column<string>(type: "TEXT", maxLength: 39, nullable: false),
                    Repository = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_github_observation_targets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_github_observation_targets_git_workspaces_GitWorkspaceId",
                        column: x => x.GitWorkspaceId,
                        principalTable: "git_workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_github_observation_targets_local_commit_operations_LocalCommitOperationId",
                        column: x => x.LocalCommitOperationId,
                        principalTable: "local_commit_operations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_github_observation_targets_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_github_observation_targets_runs_RunId",
                        column: x => x.RunId,
                        principalTable: "runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_github_observation_targets_event",
                table: "github_observation_targets",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_github_observation_targets_GitWorkspaceId",
                table: "github_observation_targets",
                column: "GitWorkspaceId");

            migrationBuilder.CreateIndex(
                name: "ix_github_observation_targets_operation",
                table: "github_observation_targets",
                column: "LocalCommitOperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_github_observation_targets_ProjectId",
                table: "github_observation_targets",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "ix_github_observation_targets_run",
                table: "github_observation_targets",
                column: "RunId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "github_observation_targets");
        }
    }
}
