using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevalCopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLocalExecutionQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            DropAllGuards(migrationBuilder);

            migrationBuilder.AddColumn<string>(
                name: "AdmissionMode",
                table: "local_execution_sequences",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "Immediate");

            migrationBuilder.AddColumn<long>(
                name: "CoordinationNumber",
                table: "local_execution_sequences",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            // ADR-0062: every historical sequence is Immediate (set above) and receives only a stable order number, oldest first by its
            // arming; no queue authority is granted. This runs before the table rebuild below, whose copy enforces the new constraints.
            migrationBuilder.Sql(
                "UPDATE local_execution_sequences SET \"CoordinationNumber\" = (SELECT COUNT(*) FROM local_execution_sequences AS o "
                + "WHERE o.\"ArmedAtUtc\" < local_execution_sequences.\"ArmedAtUtc\" "
                + "OR (o.\"ArmedAtUtc\" = local_execution_sequences.\"ArmedAtUtc\" AND o.\"Id\" <= local_execution_sequences.\"Id\"));");

            migrationBuilder.CreateTable(
                name: "local_execution_admissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SequenceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AdmissionNumber = table.Column<long>(type: "INTEGER", nullable: false),
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
                    StepId = table.Column<Guid>(type: "TEXT", nullable: true),
                    WaitingReason = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    WaitingReasonRecordedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    EnqueuedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ReasonCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_local_execution_admissions", x => x.Id);
                    table.CheckConstraint("ck_local_execution_admissions_fingerprint", "length(\"SourceFingerprintSha256\") = 64 AND \"SourceFingerprintSha256\" NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("ck_local_execution_admissions_kind", "\"Kind\" IN ('Implementation', 'Verification', 'CodeReview', 'VerificationDiagnosis', 'ReviewCorrection', 'DiagnosisCorrection')");
                    table.CheckConstraint("ck_local_execution_admissions_number", "typeof(\"AdmissionNumber\") = 'integer' AND \"AdmissionNumber\" >= 1");
                    table.CheckConstraint("ck_local_execution_admissions_ordinal", "typeof(\"Ordinal\") = 'integer' AND \"Ordinal\" BETWEEN 0 AND 101");
                    table.CheckConstraint("ck_local_execution_admissions_pass", "\"PassNumber\" BETWEEN 0 AND 3");
                    table.CheckConstraint("ck_local_execution_admissions_reason", "(\"State\" IN ('Retired', 'Unresolved')) = (\"ReasonCode\" IS NOT NULL)");
                    table.CheckConstraint("ck_local_execution_admissions_revision", "\"Revision\" >= 1");
                    table.CheckConstraint("ck_local_execution_admissions_shape", "((\"Kind\" = 'Implementation') = (\"PassNumber\" = 0)) AND ((\"Kind\" = 'Implementation') = (\"Ordinal\" = 0)) AND ((\"Kind\" = 'Verification') = (\"RecipeOrdinal\" IS NOT NULL)) AND ((\"Kind\" = 'Implementation') = (\"PredecessorStepId\" IS NULL)) AND ((\"Kind\" = 'Implementation') = (\"ReportMessageId\" IS NULL)) AND ((\"Kind\" IN ('ReviewCorrection', 'DiagnosisCorrection')) = (\"SubjectAttemptId\" IS NOT NULL))");
                    table.CheckConstraint("ck_local_execution_admissions_state", "\"State\" IN ('Waiting', 'Reserved', 'Consumed', 'Retired', 'Unresolved')");
                    table.CheckConstraint("ck_local_execution_admissions_step_link", "(\"State\" NOT IN ('Reserved', 'Consumed', 'Unresolved') OR \"StepId\" IS NOT NULL) AND (\"State\" <> 'Waiting' OR \"StepId\" IS NULL)");
                    table.CheckConstraint("ck_local_execution_admissions_waiting_reason", "\"WaitingReason\" IN ('AwaitingAdmissionCheck', 'WaitingForWorkspace', 'WaitingForCapacity', 'WaitingForEarlierEligibleWork', 'WaitingForPreparationCapacity', 'Unavailable')");
                    table.ForeignKey(
                        name: "FK_local_execution_admissions_attempts_SubjectAttemptId",
                        column: x => x.SubjectAttemptId,
                        principalTable: "attempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_local_execution_admissions_collaboration_messages_ReportMessageId",
                        column: x => x.ReportMessageId,
                        principalTable: "collaboration_messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_local_execution_admissions_git_checkpoints_SourceCheckpointId",
                        column: x => x.SourceCheckpointId,
                        principalTable: "git_checkpoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_local_execution_admissions_local_execution_sequence_steps_PredecessorStepId",
                        column: x => x.PredecessorStepId,
                        principalTable: "local_execution_sequence_steps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_local_execution_admissions_local_execution_sequence_steps_StepId",
                        column: x => x.StepId,
                        principalTable: "local_execution_sequence_steps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_local_execution_admissions_local_execution_sequences_SequenceId",
                        column: x => x.SequenceId,
                        principalTable: "local_execution_sequences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "local_execution_order_counters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    LastNumber = table.Column<long>(type: "INTEGER", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_local_execution_order_counters", x => x.Id);
                    table.CheckConstraint("ck_local_execution_order_counters_last", "typeof(\"LastNumber\") = 'integer' AND \"LastNumber\" >= 0");
                    table.CheckConstraint("ck_local_execution_order_counters_revision", "\"Revision\" >= 0");
                    table.CheckConstraint("ck_local_execution_order_counters_singleton", "\"Id\" = 1");
                });

            migrationBuilder.InsertData(
                table: "local_execution_order_counters",
                columns: new[] { "Id", "LastNumber", "Revision" },
                values: new object[] { 1, 0L, 0L });

            migrationBuilder.Sql(
                "UPDATE local_execution_order_counters SET \"LastNumber\" = (SELECT COUNT(*) FROM local_execution_sequences) WHERE \"Id\" = 1;");

            migrationBuilder.CreateIndex(
                name: "ix_local_execution_admissions_number",
                table: "local_execution_admissions",
                column: "AdmissionNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_local_execution_admissions_ordinal",
                table: "local_execution_admissions",
                columns: new[] { "SequenceId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_local_execution_admissions_outstanding",
                table: "local_execution_admissions",
                column: "SequenceId",
                unique: true,
                filter: "\"State\" IN ('Waiting', 'Reserved')");

            migrationBuilder.CreateIndex(
                name: "IX_local_execution_admissions_PredecessorStepId",
                table: "local_execution_admissions",
                column: "PredecessorStepId");

            migrationBuilder.CreateIndex(
                name: "IX_local_execution_admissions_ReportMessageId",
                table: "local_execution_admissions",
                column: "ReportMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_local_execution_admissions_SourceCheckpointId",
                table: "local_execution_admissions",
                column: "SourceCheckpointId");

            migrationBuilder.CreateIndex(
                name: "ix_local_execution_admissions_state",
                table: "local_execution_admissions",
                column: "State");

            migrationBuilder.CreateIndex(
                name: "ix_local_execution_admissions_step",
                table: "local_execution_admissions",
                column: "StepId",
                unique: true,
                filter: "\"StepId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_local_execution_admissions_SubjectAttemptId",
                table: "local_execution_admissions",
                column: "SubjectAttemptId");

            // The state check, the new column checks and the coordination index live in the rebuilt table (written out in the Rebuild part).
            RebuildSequences(migrationBuilder, UpCreateTemporary, UpCopy, UpIndexes);

            CreateGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ADR-0062: the previous schema cannot represent a held (Reserved or Unresolved) admission, which is an execution owner whose
            // unused proof exists only here. The downgrade is refused before any guard, row or table changes, and no release exists: the
            // first statements abort the migration (and its transaction) through a named CHECK on a temporary table, so the guards, the
            // admissions, the order counter and the migration history are untouched. A waiting-only database still reverses below.
            migrationBuilder.Sql("DROP TABLE IF EXISTS temp.local_execution_queue_downgrade_check;");
            migrationBuilder.Sql(
                "CREATE TEMP TABLE local_execution_queue_downgrade_check (held INTEGER NOT NULL, "
                + "CONSTRAINT held_reservations_block_downgrade CHECK (held = 0));");
            migrationBuilder.Sql(
                "INSERT INTO local_execution_queue_downgrade_check (held) SELECT COUNT(*) FROM local_execution_admissions "
                + "WHERE \"State\" IN ('Reserved', 'Unresolved');");
            migrationBuilder.Sql("DROP TABLE temp.local_execution_queue_downgrade_check;");

            DropAllGuards(migrationBuilder);

            // The previous schema has no waiting state: a cycle still waiting at downgrade is closed as Interrupted, exactly like a host
            // restart (it never resumes), and its pending admissions disappear with their table. Nothing is granted or released.
            migrationBuilder.Sql(
                "UPDATE local_execution_sequences SET \"State\" = 'Interrupted', \"ReasonCode\" = 'host_restarted', "
                + "\"EndedAtUtc\" = \"UpdatedAtUtc\", \"Revision\" = \"Revision\" + 1 WHERE \"State\" = 'WaitingForAdmission';");

            migrationBuilder.DropTable(
                name: "local_execution_admissions");

            migrationBuilder.DropTable(
                name: "local_execution_order_counters");

            RebuildSequences(migrationBuilder, DownCreateTemporary, DownCopy, DownIndexes);

            RestorePreviousGuards(migrationBuilder);
        }
    }
}
