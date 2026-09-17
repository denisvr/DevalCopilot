using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevalCopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderLaunchTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LaunchKind",
                table: "host_capability_snapshots",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolvedScriptPath",
                table: "host_capability_snapshots",
                type: "TEXT",
                maxLength: 1024,
                nullable: true);

            // Historical reconstruction, not fabrication: before this migration, a row could only
            // ever have gained a ResolvedExecutablePath through the direct-executable discovery
            // path — the NodeScript launch kind did not exist yet. Every such row (whether its
            // current ReasonCode is None, or a later failure that still retains that
            // last-known-good path) is therefore truthfully backfilled as DirectExecutable.
            // ResolvedScriptPath is never backfilled: no prior row could ever have observed one.
            migrationBuilder.Sql(
                """
                UPDATE host_capability_snapshots
                SET LaunchKind = 'DirectExecutable'
                WHERE ResolvedExecutablePath IS NOT NULL AND LaunchKind IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LaunchKind",
                table: "host_capability_snapshots");

            migrationBuilder.DropColumn(
                name: "ResolvedScriptPath",
                table: "host_capability_snapshots");
        }
    }
}
