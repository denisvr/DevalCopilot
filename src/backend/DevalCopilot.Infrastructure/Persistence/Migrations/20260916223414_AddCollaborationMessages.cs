using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevalCopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCollaborationMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "collaboration_messages",
                columns: table => new
                {
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AttemptId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProtocolVersion = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    Actor = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Recipient = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    InReplyToMessageId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 600, nullable: false),
                    StructuredContentJson = table.Column<string>(type: "TEXT", maxLength: 5000, nullable: false),
                    Provenance = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collaboration_messages", x => x.Sequence);
                    table.ForeignKey(
                        name: "FK_collaboration_messages_attempts_AttemptId",
                        column: x => x.AttemptId,
                        principalTable: "attempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_collaboration_messages_runs_RunId",
                        column: x => x.RunId,
                        principalTable: "runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_collaboration_messages_AttemptId",
                table: "collaboration_messages",
                column: "AttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_collaboration_messages_Id",
                table: "collaboration_messages",
                column: "Id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_collaboration_messages_RunId_InReplyToMessageId",
                table: "collaboration_messages",
                columns: new[] { "RunId", "InReplyToMessageId" });

            migrationBuilder.CreateIndex(
                name: "IX_collaboration_messages_RunId_Sequence",
                table: "collaboration_messages",
                columns: new[] { "RunId", "Sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "collaboration_messages");
        }
    }
}
