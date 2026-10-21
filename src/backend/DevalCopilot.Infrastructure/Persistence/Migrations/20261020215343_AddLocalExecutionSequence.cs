using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevalCopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLocalExecutionSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "local_execution_sequences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CheckpointId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FingerprintSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PlanProposalMessageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlanAuthority = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    PlanInputsSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ConfirmationSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SettingsSnapshot = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    RecipeCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CorrectionAllowance = table.Column<int>(type: "INTEGER", nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ReasonCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    DetailCode = table.Column<string>(type: "TEXT", maxLength: 96, nullable: true),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    ArmedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    AdvancementDeadlineUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    StopRequestedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    EndedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    FinalReportMessageId = table.Column<Guid>(type: "TEXT", nullable: true),
                    FinalReviewAttemptId = table.Column<Guid>(type: "TEXT", nullable: true),
                    FinalEscalationMessageId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_local_execution_sequences", x => x.Id);
                    table.CheckConstraint("ck_local_execution_sequences_allowance", "\"CorrectionAllowance\" BETWEEN 0 AND 2");
                    table.CheckConstraint("ck_local_execution_sequences_authority", "\"PlanAuthority\" IN ('AcceptedRoot', 'AcceptedRevision', 'HumanAuthorizedRevision')");
                    table.CheckConstraint("ck_local_execution_sequences_confirmation", "length(\"ConfirmationSha256\") = 64 AND \"ConfirmationSha256\" NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("ck_local_execution_sequences_final", "(\"State\" = 'ReadyForHumanApproval') = (\"FinalReviewAttemptId\" IS NOT NULL) AND (\"State\" = 'HumanEscalation') = (\"FinalEscalationMessageId\" IS NOT NULL) AND (\"State\" IN ('ReadyForHumanApproval', 'HumanEscalation')) = (\"FinalReportMessageId\" IS NOT NULL)");
                    table.CheckConstraint("ck_local_execution_sequences_fingerprint", "length(\"FingerprintSha256\") = 64 AND \"FingerprintSha256\" NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("ck_local_execution_sequences_plan_inputs", "length(\"PlanInputsSha256\") = 64 AND \"PlanInputsSha256\" NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("ck_local_execution_sequences_recipes", "\"RecipeCount\" BETWEEN 1 AND 32");
                    table.CheckConstraint("ck_local_execution_sequences_revision", "\"Revision\" >= 1");
                    table.CheckConstraint("ck_local_execution_sequences_state", "\"State\" IN ('Armed', 'PreparingStep', 'AwaitingStepResult', 'Stopping', 'ReadyForHumanApproval', 'HumanEscalation', 'Stopped', 'NeedsAttention', 'Interrupted')");
                    table.ForeignKey(
                        name: "FK_local_execution_sequences_attempts_FinalReviewAttemptId",
                        column: x => x.FinalReviewAttemptId,
                        principalTable: "attempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_local_execution_sequences_collaboration_messages_FinalEscalationMessageId",
                        column: x => x.FinalEscalationMessageId,
                        principalTable: "collaboration_messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_local_execution_sequences_collaboration_messages_FinalReportMessageId",
                        column: x => x.FinalReportMessageId,
                        principalTable: "collaboration_messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_local_execution_sequences_collaboration_messages_PlanProposalMessageId",
                        column: x => x.PlanProposalMessageId,
                        principalTable: "collaboration_messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_local_execution_sequences_git_checkpoints_CheckpointId",
                        column: x => x.CheckpointId,
                        principalTable: "git_checkpoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_local_execution_sequences_git_workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "git_workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_local_execution_sequences_runs_RunId",
                        column: x => x.RunId,
                        principalTable: "runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "local_execution_sequence_recipes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SequenceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    VerificationCommandId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommandNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    CommandName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ExecutablePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    ArgumentsJson = table.Column<string>(type: "TEXT", nullable: false),
                    TimeoutSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConfiguredAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_local_execution_sequence_recipes", x => x.Id);
                    table.CheckConstraint("ck_local_execution_sequence_recipes_enabled", "\"IsEnabled\" = 1");
                    table.CheckConstraint("ck_local_execution_sequence_recipes_ordinal", "\"Ordinal\" BETWEEN 0 AND 31");
                    table.CheckConstraint("ck_local_execution_sequence_recipes_timeout", "\"TimeoutSeconds\" BETWEEN 1 AND 900");
                    table.ForeignKey(
                        name: "FK_local_execution_sequence_recipes_local_execution_sequences_SequenceId",
                        column: x => x.SequenceId,
                        principalTable: "local_execution_sequences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "local_execution_sequence_steps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SequenceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    PassNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    RecipeOrdinal = table.Column<int>(type: "INTEGER", nullable: true),
                    PredecessorStepId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SourceCheckpointId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceFingerprintSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ReportMessageId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SubjectAttemptId = table.Column<Guid>(type: "TEXT", nullable: true),
                    State = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ReservedOperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OperationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PreparationOwnerToken = table.Column<Guid>(type: "TEXT", nullable: false),
                    PreparationReservedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    PreparationDeadlineUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ClaimedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ResultDeadlineUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ReasonCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_local_execution_sequence_steps", x => x.Id);
                    table.CheckConstraint("ck_local_execution_sequence_steps_kind", "\"Kind\" IN ('Implementation', 'Verification', 'CodeReview', 'VerificationDiagnosis', 'ReviewCorrection', 'DiagnosisCorrection')");
                    table.CheckConstraint("ck_local_execution_sequence_steps_operation_link", "(\"State\" IN ('Claimed', 'Concluded')) = (\"OperationId\" IS NOT NULL) AND (\"OperationId\" IS NULL OR \"OperationId\" = \"ReservedOperationId\") AND (\"OperationId\" IS NOT NULL) = (\"ClaimedAtUtc\" IS NOT NULL) AND (\"OperationId\" IS NOT NULL) = (\"ResultDeadlineUtc\" IS NOT NULL)");
                    table.CheckConstraint("ck_local_execution_sequence_steps_ordinal", "\"Ordinal\" BETWEEN 0 AND 101");
                    table.CheckConstraint("ck_local_execution_sequence_steps_pass", "\"PassNumber\" BETWEEN 0 AND 3");
                    table.CheckConstraint("ck_local_execution_sequence_steps_revision", "\"Revision\" >= 1");
                    table.CheckConstraint("ck_local_execution_sequence_steps_shape", "((\"Kind\" = 'Implementation') = (\"PassNumber\" = 0)) AND ((\"Kind\" = 'Implementation') = (\"Ordinal\" = 0)) AND ((\"Kind\" = 'Verification') = (\"RecipeOrdinal\" IS NOT NULL)) AND ((\"Kind\" = 'Implementation') = (\"PredecessorStepId\" IS NULL)) AND ((\"Kind\" = 'Implementation') = (\"ReportMessageId\" IS NULL)) AND ((\"Kind\" IN ('ReviewCorrection', 'DiagnosisCorrection')) = (\"SubjectAttemptId\" IS NOT NULL))");
                    table.CheckConstraint("ck_local_execution_sequence_steps_state", "\"State\" IN ('Reserved', 'Claimed', 'Concluded', 'Revoked', 'Unresolved')");
                    table.ForeignKey(
                        name: "FK_local_execution_sequence_steps_attempts_SubjectAttemptId",
                        column: x => x.SubjectAttemptId,
                        principalTable: "attempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_local_execution_sequence_steps_collaboration_messages_ReportMessageId",
                        column: x => x.ReportMessageId,
                        principalTable: "collaboration_messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_local_execution_sequence_steps_git_checkpoints_SourceCheckpointId",
                        column: x => x.SourceCheckpointId,
                        principalTable: "git_checkpoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_local_execution_sequence_steps_local_execution_sequence_steps_PredecessorStepId",
                        column: x => x.PredecessorStepId,
                        principalTable: "local_execution_sequence_steps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_local_execution_sequence_steps_local_execution_sequences_SequenceId",
                        column: x => x.SequenceId,
                        principalTable: "local_execution_sequences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_local_execution_sequence_recipes_command",
                table: "local_execution_sequence_recipes",
                columns: new[] { "SequenceId", "VerificationCommandId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_local_execution_sequence_recipes_number",
                table: "local_execution_sequence_recipes",
                columns: new[] { "SequenceId", "CommandNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_local_execution_sequence_recipes_ordinal",
                table: "local_execution_sequence_recipes",
                columns: new[] { "SequenceId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_local_execution_sequence_steps_membership",
                table: "local_execution_sequence_steps",
                columns: new[] { "SequenceId", "PassNumber", "RecipeOrdinal" },
                unique: true,
                filter: "\"RecipeOrdinal\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_local_execution_sequence_steps_operation",
                table: "local_execution_sequence_steps",
                column: "OperationId",
                unique: true,
                filter: "\"OperationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_local_execution_sequence_steps_ordinal",
                table: "local_execution_sequence_steps",
                columns: new[] { "SequenceId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_local_execution_sequence_steps_outstanding",
                table: "local_execution_sequence_steps",
                column: "SequenceId",
                unique: true,
                filter: "\"State\" IN ('Reserved', 'Claimed')");

            migrationBuilder.CreateIndex(
                name: "IX_local_execution_sequence_steps_PredecessorStepId",
                table: "local_execution_sequence_steps",
                column: "PredecessorStepId");

            migrationBuilder.CreateIndex(
                name: "IX_local_execution_sequence_steps_ReportMessageId",
                table: "local_execution_sequence_steps",
                column: "ReportMessageId");

            migrationBuilder.CreateIndex(
                name: "ix_local_execution_sequence_steps_reserved_operation",
                table: "local_execution_sequence_steps",
                column: "ReservedOperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_local_execution_sequence_steps_SourceCheckpointId",
                table: "local_execution_sequence_steps",
                column: "SourceCheckpointId");

            migrationBuilder.CreateIndex(
                name: "IX_local_execution_sequence_steps_SubjectAttemptId",
                table: "local_execution_sequence_steps",
                column: "SubjectAttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_local_execution_sequences_CheckpointId",
                table: "local_execution_sequences",
                column: "CheckpointId");

            migrationBuilder.CreateIndex(
                name: "ix_local_execution_sequences_final_escalation",
                table: "local_execution_sequences",
                column: "FinalEscalationMessageId",
                unique: true,
                filter: "\"FinalEscalationMessageId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_local_execution_sequences_FinalReportMessageId",
                table: "local_execution_sequences",
                column: "FinalReportMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_local_execution_sequences_FinalReviewAttemptId",
                table: "local_execution_sequences",
                column: "FinalReviewAttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_local_execution_sequences_PlanProposalMessageId",
                table: "local_execution_sequences",
                column: "PlanProposalMessageId");

            migrationBuilder.CreateIndex(
                name: "ix_local_execution_sequences_project",
                table: "local_execution_sequences",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "ix_local_execution_sequences_run",
                table: "local_execution_sequences",
                column: "RunId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_local_execution_sequences_workspace",
                table: "local_execution_sequences",
                column: "WorkspaceId");

            CreateGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            DropGuards(migrationBuilder);

            migrationBuilder.DropTable(
                name: "local_execution_sequence_recipes");

            migrationBuilder.DropTable(
                name: "local_execution_sequence_steps");

            migrationBuilder.DropTable(
                name: "local_execution_sequences");
        }
    }
}
