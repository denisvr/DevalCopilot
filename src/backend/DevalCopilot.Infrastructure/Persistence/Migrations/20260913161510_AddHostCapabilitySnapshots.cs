using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevalCopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHostCapabilitySnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "host_capability_snapshots",
                columns: table => new
                {
                    Capability = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ReasonCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ResolvedExecutablePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    ObservedVersion = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    EvidenceObservedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    NextProbeDueAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ProbeDispatchedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_host_capability_snapshots", x => x.Capability);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "host_capability_snapshots");
        }
    }
}
