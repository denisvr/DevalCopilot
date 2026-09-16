using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevalCopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckpointReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "checkpoint_reviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GitWorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GitCheckpointId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CheckpointNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    CheckpointFingerprintSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    VerificationExecutionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    VerificationExecutionNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    VerificationExecutionCheckpointFingerprintSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    VerificationExecutionStatus = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    VerificationExecutionOutcome = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    VerificationExecutionExitCode = table.Column<int>(type: "INTEGER", nullable: true),
                    ActorKind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Decision = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    RecordedAtUtcTicks = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_checkpoint_reviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_checkpoint_reviews_git_checkpoints_GitCheckpointId",
                        column: x => x.GitCheckpointId,
                        principalTable: "git_checkpoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_checkpoint_reviews_git_workspaces_GitWorkspaceId",
                        column: x => x.GitWorkspaceId,
                        principalTable: "git_workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_checkpoint_reviews_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_checkpoint_reviews_verification_executions_VerificationExecutionId",
                        column: x => x.VerificationExecutionId,
                        principalTable: "verification_executions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_checkpoint_reviews_GitCheckpointId_VerificationExecutionId",
                table: "checkpoint_reviews",
                columns: new[] { "GitCheckpointId", "VerificationExecutionId" });

            migrationBuilder.CreateIndex(
                name: "IX_checkpoint_reviews_GitWorkspaceId",
                table: "checkpoint_reviews",
                column: "GitWorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_checkpoint_reviews_ProjectId_RecordedAtUtcTicks_Id",
                table: "checkpoint_reviews",
                columns: new[] { "ProjectId", "RecordedAtUtcTicks", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_checkpoint_reviews_VerificationExecutionId",
                table: "checkpoint_reviews",
                column: "VerificationExecutionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "checkpoint_reviews");
        }
    }
}
