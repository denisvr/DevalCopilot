using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevalCopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanningSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "planning_sequences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CheckpointId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FingerprintSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ObjectiveSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ReasonCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    DetailCode = table.Column<string>(type: "TEXT", maxLength: 96, nullable: true),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    ArmedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    StopRequestedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    EndedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_planning_sequences", x => x.Id);
                    table.CheckConstraint("ck_planning_sequences_fingerprint", "length(\"FingerprintSha256\") = 64 AND \"FingerprintSha256\" NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("ck_planning_sequences_objective", "length(\"ObjectiveSha256\") = 64 AND \"ObjectiveSha256\" NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("ck_planning_sequences_revision", "\"Revision\" >= 1");
                    table.CheckConstraint("ck_planning_sequences_state", "\"State\" IN ('Armed', 'PreparingStep', 'AwaitingStepResult', 'Stopping', 'ReadyForImplementation', 'AwaitingHumanDecision', 'Stopped', 'NeedsAttention', 'Interrupted')");
                    table.ForeignKey(
                        name: "FK_planning_sequences_git_checkpoints_CheckpointId",
                        column: x => x.CheckpointId,
                        principalTable: "git_checkpoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_planning_sequences_git_workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "git_workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_planning_sequences_runs_RunId",
                        column: x => x.RunId,
                        principalTable: "runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "planning_sequence_steps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SequenceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    PredecessorAttemptId = table.Column<Guid>(type: "TEXT", nullable: true),
                    InputProposalMessageId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReservedAttemptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AttemptId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PreparationOwnerToken = table.Column<Guid>(type: "TEXT", nullable: false),
                    PreparationReservedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    PreparationDeadlineUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ReasonCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_planning_sequence_steps", x => x.Id);
                    table.CheckConstraint("ck_planning_sequence_steps_attempt_link", "(\"State\" IN ('Claimed', 'Concluded')) = (\"AttemptId\" IS NOT NULL) AND (\"AttemptId\" IS NULL OR \"AttemptId\" = \"ReservedAttemptId\")");
                    table.CheckConstraint("ck_planning_sequence_steps_ordinal", "\"Ordinal\" BETWEEN 0 AND 4");
                    table.CheckConstraint("ck_planning_sequence_steps_predecessor", "(\"Ordinal\" = 0) = (\"PredecessorAttemptId\" IS NULL) AND (\"Ordinal\" = 0) = (\"InputProposalMessageId\" IS NULL)");
                    table.CheckConstraint("ck_planning_sequence_steps_revision", "\"Revision\" >= 1");
                    table.CheckConstraint("ck_planning_sequence_steps_role", "(\"Ordinal\" = 0 AND \"Role\" = 'Planner') OR (\"Ordinal\" IN (1, 3) AND \"Role\" = 'CriticalReviewer') OR (\"Ordinal\" IN (2, 4) AND \"Role\" = 'Resolver')");
                    table.CheckConstraint("ck_planning_sequence_steps_state", "\"State\" IN ('Reserved', 'Claimed', 'Concluded', 'Revoked', 'Unresolved')");
                    table.ForeignKey(
                        name: "FK_planning_sequence_steps_attempts_AttemptId",
                        column: x => x.AttemptId,
                        principalTable: "attempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_planning_sequence_steps_attempts_PredecessorAttemptId",
                        column: x => x.PredecessorAttemptId,
                        principalTable: "attempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_planning_sequence_steps_collaboration_messages_InputProposalMessageId",
                        column: x => x.InputProposalMessageId,
                        principalTable: "collaboration_messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_planning_sequence_steps_planning_sequences_SequenceId",
                        column: x => x.SequenceId,
                        principalTable: "planning_sequences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_planning_sequence_steps_attempt",
                table: "planning_sequence_steps",
                column: "AttemptId",
                unique: true,
                filter: "\"AttemptId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_planning_sequence_steps_InputProposalMessageId",
                table: "planning_sequence_steps",
                column: "InputProposalMessageId");

            migrationBuilder.CreateIndex(
                name: "ix_planning_sequence_steps_ordinal",
                table: "planning_sequence_steps",
                columns: new[] { "SequenceId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_planning_sequence_steps_outstanding",
                table: "planning_sequence_steps",
                column: "SequenceId",
                unique: true,
                filter: "\"State\" IN ('Reserved', 'Claimed')");

            migrationBuilder.CreateIndex(
                name: "IX_planning_sequence_steps_PredecessorAttemptId",
                table: "planning_sequence_steps",
                column: "PredecessorAttemptId");

            migrationBuilder.CreateIndex(
                name: "ix_planning_sequence_steps_reserved_attempt",
                table: "planning_sequence_steps",
                column: "ReservedAttemptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_planning_sequences_CheckpointId",
                table: "planning_sequences",
                column: "CheckpointId");

            migrationBuilder.CreateIndex(
                name: "ix_planning_sequences_run",
                table: "planning_sequences",
                column: "RunId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_planning_sequences_workspace",
                table: "planning_sequences",
                column: "WorkspaceId");

            CreateGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            DropGuards(migrationBuilder);

            migrationBuilder.DropTable(
                name: "planning_sequence_steps");

            migrationBuilder.DropTable(
                name: "planning_sequences");
        }
    }
}
