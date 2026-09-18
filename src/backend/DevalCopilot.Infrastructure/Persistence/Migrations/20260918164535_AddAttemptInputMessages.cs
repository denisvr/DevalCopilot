using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevalCopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttemptInputMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "attempt_input_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AttemptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CollaborationMessageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attempt_input_messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_attempt_input_messages_attempts_AttemptId",
                        column: x => x.AttemptId,
                        principalTable: "attempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_attempt_input_messages_AttemptId_CollaborationMessageId",
                table: "attempt_input_messages",
                columns: new[] { "AttemptId", "CollaborationMessageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_attempt_input_messages_AttemptId_Sequence",
                table: "attempt_input_messages",
                columns: new[] { "AttemptId", "Sequence" },
                unique: true);

            // Historical reconstruction, not fabrication: before this migration, the only input
            // an Agent attempt could ever record was the single message named by
            // AgentInputCollaborationMessageId (set only for a Claude critical-review attempt).
            // Every such row is truthfully backfilled as this attempt's sole input, at Sequence 0
            // — the exact shape a critical-review attempt's input set has always had. Must run
            // before the column is dropped below.
            migrationBuilder.Sql(
                """
                INSERT INTO attempt_input_messages (Id, AttemptId, CollaborationMessageId, Sequence)
                SELECT
                    lower(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(6))),
                    Id,
                    AgentInputCollaborationMessageId,
                    0
                FROM attempts
                WHERE AgentInputCollaborationMessageId IS NOT NULL;
                """);

            // Never a second, competing source of truth for an attempt's input identity now that
            // attempt_input_messages is the one authoritative record — see Attempt.cs.
            migrationBuilder.DropColumn(
                name: "AgentInputCollaborationMessageId",
                table: "attempts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AgentInputCollaborationMessageId",
                table: "attempts",
                type: "TEXT",
                nullable: true);

            // Best-effort reconstruction: only ever lossless for an attempt whose input set was
            // exactly one message (every pre-existing critical-review attempt); an attempt with
            // more than one input message (a challenge-resolution attempt, introduced after this
            // migration) has no single id this older column shape can represent, so only the
            // Sequence-0 message is ever restored here.
            migrationBuilder.Sql(
                """
                UPDATE attempts
                SET AgentInputCollaborationMessageId = (
                    SELECT CollaborationMessageId
                    FROM attempt_input_messages
                    WHERE attempt_input_messages.AttemptId = attempts.Id AND attempt_input_messages.Sequence = 0
                )
                WHERE Id IN (SELECT AttemptId FROM attempt_input_messages WHERE Sequence = 0);
                """);

            migrationBuilder.DropTable(
                name: "attempt_input_messages");
        }
    }
}
