using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevalCopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttemptVerificationEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "attempt_verification_evidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AttemptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VerificationCommandId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VerificationExecutionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attempt_verification_evidence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_attempt_verification_evidence_attempts_AttemptId",
                        column: x => x.AttemptId,
                        principalTable: "attempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_attempt_verification_evidence_verification_commands_VerificationCommandId",
                        column: x => x.VerificationCommandId,
                        principalTable: "verification_commands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_attempt_verification_evidence_verification_executions_VerificationExecutionId",
                        column: x => x.VerificationExecutionId,
                        principalTable: "verification_executions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_attempt_verification_evidence_AttemptId_Sequence",
                table: "attempt_verification_evidence",
                columns: new[] { "AttemptId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_attempt_verification_evidence_AttemptId_VerificationCommandId",
                table: "attempt_verification_evidence",
                columns: new[] { "AttemptId", "VerificationCommandId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_attempt_verification_evidence_AttemptId_VerificationExecutionId",
                table: "attempt_verification_evidence",
                columns: new[] { "AttemptId", "VerificationExecutionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_attempt_verification_evidence_VerificationCommandId",
                table: "attempt_verification_evidence",
                column: "VerificationCommandId");

            migrationBuilder.CreateIndex(
                name: "IX_attempt_verification_evidence_VerificationExecutionId",
                table: "attempt_verification_evidence",
                column: "VerificationExecutionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attempt_verification_evidence");
        }
    }
}
