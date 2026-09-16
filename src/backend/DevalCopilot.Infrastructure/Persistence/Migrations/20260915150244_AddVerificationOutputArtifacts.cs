using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevalCopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVerificationOutputArtifacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "verification_output_artifacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    VerificationExecutionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Purpose = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    RelativeStoragePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ByteLength = table.Column<long>(type: "INTEGER", nullable: false),
                    Truncated = table.Column<bool>(type: "INTEGER", nullable: true),
                    CaptureOutcome = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_verification_output_artifacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_verification_output_artifacts_verification_executions_VerificationExecutionId",
                        column: x => x.VerificationExecutionId,
                        principalTable: "verification_executions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_verification_output_artifacts_VerificationExecutionId_Purpose",
                table: "verification_output_artifacts",
                columns: new[] { "VerificationExecutionId", "Purpose" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "verification_output_artifacts");
        }
    }
}
