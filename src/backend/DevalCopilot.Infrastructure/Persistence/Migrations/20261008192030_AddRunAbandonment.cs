using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevalCopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRunAbandonment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AbandonedAtUtc",
                table: "runs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AbandonmentReason",
                table: "runs",
                type: "TEXT",
                maxLength: 2048,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Native column drops, not DropColumn: the SQLite provider implements DropColumn by rebuilding the whole runs table, and
            // the attempts seam guard trigger of AddLocalCommitAttentionExclusion joins runs, so the rebuild's final rename fails with
            // "error in trigger ...: no such table: main.runs". ALTER TABLE ... DROP COLUMN never rebuilds, leaves every trigger,
            // index and other column exactly as it was, and neither dropped column is referenced by any of them.
            migrationBuilder.Sql("ALTER TABLE \"runs\" DROP COLUMN \"AbandonedAtUtc\";");
            migrationBuilder.Sql("ALTER TABLE \"runs\" DROP COLUMN \"AbandonmentReason\";");
        }
    }
}
