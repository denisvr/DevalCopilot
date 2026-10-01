using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevalCopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanningImplementationAuthorization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "planning_implementation_authorizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EscalationMessageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FinalProposalMessageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CheckpointId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FingerprintSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    HumanInstructionMessageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ConsumedByAttemptId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_planning_implementation_authorizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_planning_implementation_authorizations_attempts_ConsumedByAttemptId",
                        column: x => x.ConsumedByAttemptId,
                        principalTable: "attempts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_planning_implementation_authorizations_collaboration_messages_EscalationMessageId",
                        column: x => x.EscalationMessageId,
                        principalTable: "collaboration_messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_planning_implementation_authorizations_collaboration_messages_FinalProposalMessageId",
                        column: x => x.FinalProposalMessageId,
                        principalTable: "collaboration_messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_planning_implementation_authorizations_collaboration_messages_HumanInstructionMessageId",
                        column: x => x.HumanInstructionMessageId,
                        principalTable: "collaboration_messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_planning_implementation_authorizations_runs_RunId",
                        column: x => x.RunId,
                        principalTable: "runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_planning_implementation_authorizations_attempt_consumed",
                table: "planning_implementation_authorizations",
                column: "ConsumedByAttemptId",
                unique: true,
                filter: "\"ConsumedByAttemptId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_planning_implementation_authorizations_escalation",
                table: "planning_implementation_authorizations",
                column: "EscalationMessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_planning_implementation_authorizations_final_proposal",
                table: "planning_implementation_authorizations",
                column: "FinalProposalMessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_planning_implementation_authorizations_instruction",
                table: "planning_implementation_authorizations",
                column: "HumanInstructionMessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_planning_implementation_authorizations_RunId",
                table: "planning_implementation_authorizations",
                column: "RunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "planning_implementation_authorizations");
        }
    }
}
