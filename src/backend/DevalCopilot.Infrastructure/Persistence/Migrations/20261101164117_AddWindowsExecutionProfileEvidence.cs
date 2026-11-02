using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevalCopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWindowsExecutionProfileEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CaptureCompletion",
                table: "verification_output_artifacts",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Diagnostic",
                table: "verification_executions",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EnvironmentProfileVersion",
                table: "verification_executions",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StandardErrorCompletion",
                table: "verification_executions",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StandardInputDelivery",
                table: "verification_executions",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StandardOutputCompletion",
                table: "verification_executions",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgentInvocationDiagnostic",
                table: "attempts",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgentInvocationDiagnosticVersion",
                table: "attempts",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgentStandardErrorCompletion",
                table: "attempts",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgentStandardInputDelivery",
                table: "attempts",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgentStandardOutputCompletion",
                table: "attempts",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProcessStandardErrorCompletion",
                table: "attempts",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProcessStandardInputDelivery",
                table: "attempts",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProcessStandardOutputCompletion",
                table: "attempts",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CaptureCompletion",
                table: "artifacts",
                type: "TEXT",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ADR-0072: once ANY new completion fact, diagnostic or profile version was recorded, the previous schema cannot represent
            // that used evidence. The downgrade is refused before any column changes: the first statements abort the migration (and its
            // transaction) through a named CHECK on a temporary table, so the evidence and the migration history are untouched.
            migrationBuilder.Sql("DROP TABLE IF EXISTS temp.windows_execution_profile_downgrade_check;");
            migrationBuilder.Sql(
                "CREATE TEMP TABLE windows_execution_profile_downgrade_check (used INTEGER NOT NULL, "
                + "CONSTRAINT used_execution_profile_evidence_blocks_downgrade CHECK (used = 0));");
            migrationBuilder.Sql(
                "INSERT INTO windows_execution_profile_downgrade_check (used) SELECT "
                + "(SELECT COUNT(*) FROM verification_output_artifacts WHERE \"CaptureCompletion\" IS NOT NULL) "
                + "+ (SELECT COUNT(*) FROM verification_executions WHERE \"Diagnostic\" IS NOT NULL OR \"EnvironmentProfileVersion\" IS NOT NULL OR \"StandardErrorCompletion\" IS NOT NULL OR \"StandardInputDelivery\" IS NOT NULL OR \"StandardOutputCompletion\" IS NOT NULL) "
                + "+ (SELECT COUNT(*) FROM attempts WHERE \"AgentInvocationDiagnostic\" IS NOT NULL OR \"AgentInvocationDiagnosticVersion\" IS NOT NULL OR \"AgentStandardErrorCompletion\" IS NOT NULL OR \"AgentStandardInputDelivery\" IS NOT NULL OR \"AgentStandardOutputCompletion\" IS NOT NULL OR \"ProcessStandardErrorCompletion\" IS NOT NULL OR \"ProcessStandardInputDelivery\" IS NOT NULL OR \"ProcessStandardOutputCompletion\" IS NOT NULL) "
                + "+ (SELECT COUNT(*) FROM artifacts WHERE \"CaptureCompletion\" IS NOT NULL) "
                + ";");
            migrationBuilder.Sql("DROP TABLE temp.windows_execution_profile_downgrade_check;");

            // Native column drops: an EF table rebuild would re-create guarded tables and fire their triggers.
            migrationBuilder.Sql("ALTER TABLE verification_output_artifacts DROP COLUMN \"CaptureCompletion\";");
            migrationBuilder.Sql("ALTER TABLE verification_executions DROP COLUMN \"Diagnostic\";");
            migrationBuilder.Sql("ALTER TABLE verification_executions DROP COLUMN \"EnvironmentProfileVersion\";");
            migrationBuilder.Sql("ALTER TABLE verification_executions DROP COLUMN \"StandardErrorCompletion\";");
            migrationBuilder.Sql("ALTER TABLE verification_executions DROP COLUMN \"StandardInputDelivery\";");
            migrationBuilder.Sql("ALTER TABLE verification_executions DROP COLUMN \"StandardOutputCompletion\";");
            migrationBuilder.Sql("ALTER TABLE attempts DROP COLUMN \"AgentInvocationDiagnostic\";");
            migrationBuilder.Sql("ALTER TABLE attempts DROP COLUMN \"AgentInvocationDiagnosticVersion\";");
            migrationBuilder.Sql("ALTER TABLE attempts DROP COLUMN \"AgentStandardErrorCompletion\";");
            migrationBuilder.Sql("ALTER TABLE attempts DROP COLUMN \"AgentStandardInputDelivery\";");
            migrationBuilder.Sql("ALTER TABLE attempts DROP COLUMN \"AgentStandardOutputCompletion\";");
            migrationBuilder.Sql("ALTER TABLE attempts DROP COLUMN \"ProcessStandardErrorCompletion\";");
            migrationBuilder.Sql("ALTER TABLE attempts DROP COLUMN \"ProcessStandardInputDelivery\";");
            migrationBuilder.Sql("ALTER TABLE attempts DROP COLUMN \"ProcessStandardOutputCompletion\";");
            migrationBuilder.Sql("ALTER TABLE artifacts DROP COLUMN \"CaptureCompletion\";");
        }
    }
}
