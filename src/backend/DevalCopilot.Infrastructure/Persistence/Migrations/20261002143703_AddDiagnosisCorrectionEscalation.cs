using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevalCopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDiagnosisCorrectionEscalation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SnapshotSha256",
                table: "attempt_verification_evidence",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "diagnosis_correction_escalations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VerificationDiagnosisAttemptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CollaborationMessageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_diagnosis_correction_escalations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_diagnosis_correction_escalations_attempts_VerificationDiagnosisAttemptId",
                        column: x => x.VerificationDiagnosisAttemptId,
                        principalTable: "attempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_diagnosis_correction_escalations_collaboration_messages_CollaborationMessageId",
                        column: x => x.CollaborationMessageId,
                        principalTable: "collaboration_messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_diagnosis_correction_escalations_runs_RunId",
                        column: x => x.RunId,
                        principalTable: "runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_diagnosis_correction_escalations_CollaborationMessageId",
                table: "diagnosis_correction_escalations",
                column: "CollaborationMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_diagnosis_correction_escalations_RunId",
                table: "diagnosis_correction_escalations",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_diagnosis_correction_escalations_VerificationDiagnosisAttemptId",
                table: "diagnosis_correction_escalations",
                column: "VerificationDiagnosisAttemptId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "diagnosis_correction_escalations");

            migrationBuilder.DropColumn(
                name: "SnapshotSha256",
                table: "attempt_verification_evidence");
        }
    }
}
