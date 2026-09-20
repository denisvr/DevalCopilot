using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevalCopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNeutralParticipantIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ActiveParticipant",
                table: "runs",
                newName: "ActiveParticipantKind");

            migrationBuilder.RenameColumn(
                name: "Actor",
                table: "events",
                newName: "ActorKind");

            migrationBuilder.RenameColumn(
                name: "Recipient",
                table: "collaboration_messages",
                newName: "RecipientKind");

            migrationBuilder.RenameColumn(
                name: "Actor",
                table: "collaboration_messages",
                newName: "ActorKind");

            migrationBuilder.AddColumn<string>(
                name: "ActiveAgentProvider",
                table: "runs",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActiveAgentRole",
                table: "runs",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActorAgentProvider",
                table: "events",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActorAgentRole",
                table: "events",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActorAgentProvider",
                table: "collaboration_messages",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActorAgentRole",
                table: "collaboration_messages",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecipientAgentProvider",
                table: "collaboration_messages",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecipientAgentRole",
                table: "collaboration_messages",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE runs
                SET ActiveAgentProvider = CASE ActiveParticipantKind
                        WHEN 'Codex' THEN 'Codex'
                        WHEN 'Claude' THEN 'ClaudeCode'
                    END,
                    ActiveParticipantKind = CASE ActiveParticipantKind
                        WHEN 'Codex' THEN 'Agent'
                        WHEN 'Claude' THEN 'Agent'
                        ELSE ActiveParticipantKind
                    END;

                UPDATE events
                SET ActorAgentRole = CASE
                        WHEN ActorKind IN ('Codex', 'Claude') THEN (
                            SELECT AgentRole FROM attempts
                            WHERE attempts.Id = events.AttemptId AND attempts.Kind = 'Agent')
                    END,
                    ActorAgentProvider = CASE
                        WHEN ActorKind IN ('Codex', 'Claude') THEN COALESCE(
                            (SELECT AgentProvider FROM attempts
                             WHERE attempts.Id = events.AttemptId AND attempts.Kind = 'Agent'),
                            CASE ActorKind WHEN 'Codex' THEN 'Codex' ELSE 'ClaudeCode' END)
                    END,
                    ActorKind = CASE ActorKind
                        WHEN 'Codex' THEN 'Agent'
                        WHEN 'Claude' THEN 'Agent'
                        ELSE ActorKind
                    END;

                UPDATE collaboration_messages
                SET ActorAgentRole = CASE
                        WHEN ActorKind IN ('Codex', 'Claude') THEN (
                            SELECT AgentRole FROM attempts
                            WHERE attempts.Id = collaboration_messages.AttemptId AND attempts.Kind = 'Agent')
                    END,
                    ActorAgentProvider = CASE
                        WHEN ActorKind IN ('Codex', 'Claude') THEN COALESCE(
                            (SELECT AgentProvider FROM attempts
                             WHERE attempts.Id = collaboration_messages.AttemptId AND attempts.Kind = 'Agent'),
                            CASE ActorKind WHEN 'Codex' THEN 'Codex' ELSE 'ClaudeCode' END)
                    END,
                    RecipientAgentProvider = CASE RecipientKind
                        WHEN 'Codex' THEN 'Codex'
                        WHEN 'Claude' THEN 'ClaudeCode'
                    END,
                    ActorKind = CASE ActorKind
                        WHEN 'Codex' THEN 'Agent'
                        WHEN 'Claude' THEN 'Agent'
                        ELSE ActorKind
                    END,
                    RecipientKind = CASE RecipientKind
                        WHEN 'Codex' THEN 'Agent'
                        WHEN 'Claude' THEN 'Agent'
                        ELSE RecipientKind
                    END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE runs
                SET ActiveParticipantKind = CASE
                    WHEN ActiveParticipantKind = 'Agent' AND ActiveAgentProvider = 'Codex' THEN 'Codex'
                    WHEN ActiveParticipantKind = 'Agent' AND ActiveAgentProvider = 'ClaudeCode' THEN 'Claude'
                    ELSE ActiveParticipantKind
                END;

                UPDATE events
                SET ActorKind = CASE
                    WHEN ActorKind = 'Agent' AND ActorAgentProvider = 'Codex' THEN 'Codex'
                    WHEN ActorKind = 'Agent' AND ActorAgentProvider = 'ClaudeCode' THEN 'Claude'
                    ELSE ActorKind
                END;

                UPDATE collaboration_messages
                SET ActorKind = CASE
                        WHEN ActorKind = 'Agent' AND ActorAgentProvider = 'Codex' THEN 'Codex'
                        WHEN ActorKind = 'Agent' AND ActorAgentProvider = 'ClaudeCode' THEN 'Claude'
                        ELSE ActorKind
                    END,
                    RecipientKind = CASE
                        WHEN RecipientKind = 'Agent' AND RecipientAgentProvider = 'Codex' THEN 'Codex'
                        WHEN RecipientKind = 'Agent' AND RecipientAgentProvider = 'ClaudeCode' THEN 'Claude'
                        ELSE RecipientKind
                    END;
                """);

            migrationBuilder.DropColumn(
                name: "ActiveAgentProvider",
                table: "runs");

            migrationBuilder.DropColumn(
                name: "ActiveAgentRole",
                table: "runs");

            migrationBuilder.DropColumn(
                name: "ActorAgentProvider",
                table: "events");

            migrationBuilder.DropColumn(
                name: "ActorAgentRole",
                table: "events");

            migrationBuilder.DropColumn(
                name: "ActorAgentProvider",
                table: "collaboration_messages");

            migrationBuilder.DropColumn(
                name: "ActorAgentRole",
                table: "collaboration_messages");

            migrationBuilder.DropColumn(
                name: "RecipientAgentProvider",
                table: "collaboration_messages");

            migrationBuilder.DropColumn(
                name: "RecipientAgentRole",
                table: "collaboration_messages");

            migrationBuilder.RenameColumn(
                name: "ActiveParticipantKind",
                table: "runs",
                newName: "ActiveParticipant");

            migrationBuilder.RenameColumn(
                name: "ActorKind",
                table: "events",
                newName: "Actor");

            migrationBuilder.RenameColumn(
                name: "RecipientKind",
                table: "collaboration_messages",
                newName: "Recipient");

            migrationBuilder.RenameColumn(
                name: "ActorKind",
                table: "collaboration_messages",
                newName: "Actor");
        }
    }
}
