using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevalCopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVerificationExecutions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NextVerificationExecutionNumber",
                table: "projects",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "verification_executions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExecutionNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    GitWorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GitCheckpointId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VerificationCommandId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspacePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    CheckpointFingerprintSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CommandName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ExecutablePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    Arguments = table.Column<string>(type: "TEXT", nullable: false),
                    TimeoutSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ClaimedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DispatchedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    ExitCode = table.Column<int>(type: "INTEGER", nullable: true),
                    CompletionFingerprintSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_verification_executions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_verification_executions_git_checkpoints_GitCheckpointId",
                        column: x => x.GitCheckpointId,
                        principalTable: "git_checkpoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_verification_executions_git_workspaces_GitWorkspaceId",
                        column: x => x.GitWorkspaceId,
                        principalTable: "git_workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_verification_executions_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_verification_executions_verification_commands_VerificationCommandId",
                        column: x => x.VerificationCommandId,
                        principalTable: "verification_commands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_verification_executions_GitCheckpointId",
                table: "verification_executions",
                column: "GitCheckpointId");

            migrationBuilder.CreateIndex(
                name: "IX_verification_executions_GitWorkspaceId_Status",
                table: "verification_executions",
                columns: new[] { "GitWorkspaceId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_verification_executions_ProjectId_ExecutionNumber",
                table: "verification_executions",
                columns: new[] { "ProjectId", "ExecutionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_verification_executions_VerificationCommandId",
                table: "verification_executions",
                column: "VerificationCommandId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "verification_executions");

            migrationBuilder.DropColumn(
                name: "NextVerificationExecutionNumber",
                table: "projects");
        }
    }
}
