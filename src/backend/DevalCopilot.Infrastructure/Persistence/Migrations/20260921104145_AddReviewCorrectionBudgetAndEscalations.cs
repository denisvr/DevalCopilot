using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevalCopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewCorrectionBudgetAndEscalations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaximumReviewCorrectionAttempts",
                table: "runs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_collaboration_messages_Id",
                table: "collaboration_messages",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "review_correction_escalations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ImplementationReviewAttemptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CollaborationMessageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_review_correction_escalations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_review_correction_escalations_attempts_ImplementationReviewAttemptId",
                        column: x => x.ImplementationReviewAttemptId,
                        principalTable: "attempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_review_correction_escalations_collaboration_messages_CollaborationMessageId",
                        column: x => x.CollaborationMessageId,
                        principalTable: "collaboration_messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_review_correction_escalations_runs_RunId",
                        column: x => x.RunId,
                        principalTable: "runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "review_correction_authorizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EscalationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    HumanInstructionMessageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ConsumedByAttemptId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_review_correction_authorizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_review_correction_authorizations_collaboration_messages_HumanInstructionMessageId",
                        column: x => x.HumanInstructionMessageId,
                        principalTable: "collaboration_messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_review_correction_authorizations_review_correction_escalations_EscalationId",
                        column: x => x.EscalationId,
                        principalTable: "review_correction_escalations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_review_correction_authorizations_runs_RunId",
                        column: x => x.RunId,
                        principalTable: "runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_review_correction_authorizations_attempt_consumed",
                table: "review_correction_authorizations",
                column: "ConsumedByAttemptId",
                unique: true,
                filter: "\"ConsumedByAttemptId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_review_correction_authorizations_HumanInstructionMessageId",
                table: "review_correction_authorizations",
                column: "HumanInstructionMessageId");

            migrationBuilder.CreateIndex(
                name: "ix_review_correction_authorizations_one_available",
                table: "review_correction_authorizations",
                column: "EscalationId",
                unique: true,
                filter: "\"ConsumedByAttemptId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_review_correction_authorizations_RunId",
                table: "review_correction_authorizations",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_review_correction_escalations_CollaborationMessageId",
                table: "review_correction_escalations",
                column: "CollaborationMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_review_correction_escalations_ImplementationReviewAttemptId",
                table: "review_correction_escalations",
                column: "ImplementationReviewAttemptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_review_correction_escalations_RunId",
                table: "review_correction_escalations",
                column: "RunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "review_correction_authorizations");

            migrationBuilder.DropTable(
                name: "review_correction_escalations");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_collaboration_messages_Id",
                table: "collaboration_messages");

            migrationBuilder.DropColumn(
                name: "MaximumReviewCorrectionAttempts",
                table: "runs");
        }
    }
}
