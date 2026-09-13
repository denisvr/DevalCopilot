using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevalCopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "attempts",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "Simulated");

            migrationBuilder.AddColumn<string>(
                name: "ProcessApprovedRoot",
                table: "attempts",
                type: "TEXT",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProcessArguments",
                table: "attempts",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ProcessExecutablePath",
                table: "attempts",
                type: "TEXT",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProcessExitCode",
                table: "attempts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProcessMaxBytesPerStream",
                table: "attempts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProcessMaxTotalCapturedBytes",
                table: "attempts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProcessOutcome",
                table: "attempts",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ProcessTimeout",
                table: "attempts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProcessWorkingDirectory",
                table: "attempts",
                type: "TEXT",
                maxLength: 1024,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Kind",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "ProcessApprovedRoot",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "ProcessArguments",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "ProcessExecutablePath",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "ProcessExitCode",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "ProcessMaxBytesPerStream",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "ProcessMaxTotalCapturedBytes",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "ProcessOutcome",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "ProcessTimeout",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "ProcessWorkingDirectory",
                table: "attempts");
        }
    }
}
