using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevalCopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLocalCommitOperation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "local_commit_operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GitWorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RepositoryMutationLeaseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GitCheckpointId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CheckpointNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    CheckpointFingerprintSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CodeReviewAttemptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CodeReviewApprovalMessageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExecutionReportMessageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AgentCheckpointReviewId = table.Column<Guid>(type: "TEXT", nullable: false),
                    HumanCheckpointReviewId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RequestSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AuthoritySha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    NormalizedMessage = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    BranchName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    ParentCommitSha = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    TreeSha = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    CommitSha = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    AuthorName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    AuthorEmail = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    CommitTimeUnixSeconds = table.Column<long>(type: "INTEGER", nullable: false),
                    IndexPreimageSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PreparedIndexSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PreparedIndexRelativePath = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    ChangedPathCount = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ExecutionStartedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    OutcomeReasonCode = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_local_commit_operations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_local_commit_operations_attempts_CodeReviewAttemptId",
                        column: x => x.CodeReviewAttemptId,
                        principalTable: "attempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_local_commit_operations_checkpoint_reviews_AgentCheckpointReviewId",
                        column: x => x.AgentCheckpointReviewId,
                        principalTable: "checkpoint_reviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_local_commit_operations_checkpoint_reviews_HumanCheckpointReviewId",
                        column: x => x.HumanCheckpointReviewId,
                        principalTable: "checkpoint_reviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_local_commit_operations_git_checkpoints_GitCheckpointId",
                        column: x => x.GitCheckpointId,
                        principalTable: "git_checkpoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_local_commit_operations_git_workspaces_GitWorkspaceId",
                        column: x => x.GitWorkspaceId,
                        principalTable: "git_workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_local_commit_operations_repository_mutation_leases_RepositoryMutationLeaseId",
                        column: x => x.RepositoryMutationLeaseId,
                        principalTable: "repository_mutation_leases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_local_commit_operations_runs_RunId",
                        column: x => x.RunId,
                        principalTable: "runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "local_commit_authority_members",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    SubjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommandId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Digest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_local_commit_authority_members", x => x.Id);
                    table.ForeignKey(
                        name: "FK_local_commit_authority_members_local_commit_operations_OperationId",
                        column: x => x.OperationId,
                        principalTable: "local_commit_operations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_local_commit_authority_members_position",
                table: "local_commit_authority_members",
                columns: new[] { "OperationId", "Kind", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_local_commit_operations_AgentCheckpointReviewId",
                table: "local_commit_operations",
                column: "AgentCheckpointReviewId");

            migrationBuilder.CreateIndex(
                name: "IX_local_commit_operations_CodeReviewAttemptId",
                table: "local_commit_operations",
                column: "CodeReviewAttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_local_commit_operations_GitCheckpointId",
                table: "local_commit_operations",
                column: "GitCheckpointId");

            migrationBuilder.CreateIndex(
                name: "IX_local_commit_operations_HumanCheckpointReviewId",
                table: "local_commit_operations",
                column: "HumanCheckpointReviewId");

            migrationBuilder.CreateIndex(
                name: "IX_local_commit_operations_RepositoryMutationLeaseId",
                table: "local_commit_operations",
                column: "RepositoryMutationLeaseId");

            migrationBuilder.CreateIndex(
                name: "ix_local_commit_operations_run",
                table: "local_commit_operations",
                column: "RunId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_local_commit_operations_workspace",
                table: "local_commit_operations",
                column: "GitWorkspaceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "local_commit_authority_members");

            migrationBuilder.DropTable(
                name: "local_commit_operations");
        }
    }
}
