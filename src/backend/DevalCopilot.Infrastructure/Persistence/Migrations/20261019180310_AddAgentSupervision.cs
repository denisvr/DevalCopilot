using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevalCopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentSupervision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent_supervisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AttemptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    HostInstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ObserverId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastHeartbeatAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Phase = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    PhaseEnteredAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    PhaseDeadlineUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    AttentionReason = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    AttentionAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ClosureReason = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ClosedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_supervisions", x => x.Id);
                    table.CheckConstraint("ck_agent_supervisions_attention", "(\"AttentionReason\" IS NULL) = (\"AttentionAtUtc\" IS NULL) AND (\"AttentionReason\" IS NULL OR \"AttentionReason\" IN ('stale_heartbeat', 'phase_deadline_elapsed', 'observer_ended_without_terminal_result', 'host_interrupted'))");
                    table.CheckConstraint("ck_agent_supervisions_closure", "(\"ClosureReason\" IS NULL) = (\"ClosedAtUtc\" IS NULL) AND (\"ClosureReason\" IS NULL OR \"ClosureReason\" IN ('attempt_terminal', 'observer_ended', 'host_interrupted'))");
                    table.CheckConstraint("ck_agent_supervisions_identity", "\"Id\" <> '00000000-0000-0000-0000-000000000000' AND \"RunId\" <> '00000000-0000-0000-0000-000000000000' AND \"AttemptId\" <> '00000000-0000-0000-0000-000000000000' AND \"HostInstanceId\" <> '00000000-0000-0000-0000-000000000000' AND \"ObserverId\" <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("ck_agent_supervisions_phase", "\"Phase\" IN ('Preparing', 'AwaitingAdapterResult', 'RecordingResult')");
                    table.CheckConstraint("ck_agent_supervisions_revision", "\"Revision\" >= 1");
                    table.ForeignKey(
                        name: "FK_agent_supervisions_attempts_AttemptId",
                        column: x => x.AttemptId,
                        principalTable: "attempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agent_supervisions_runs_RunId",
                        column: x => x.RunId,
                        principalTable: "runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agent_supervisions_attempt",
                table: "agent_supervisions",
                column: "AttemptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_agent_supervisions_open",
                table: "agent_supervisions",
                column: "HostInstanceId",
                filter: "\"ClosedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_agent_supervisions_run",
                table: "agent_supervisions",
                column: "RunId");

            CreateGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            DropGuards(migrationBuilder);

            migrationBuilder.DropTable(
                name: "agent_supervisions");
        }
    }
}
