using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevalCopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGitWorkspaceCheckpoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NextCheckpointNumber",
                table: "git_workspaces",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "git_checkpoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CheckpointNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    CapturedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    HeadCommitSha = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    FingerprintSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_git_checkpoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_git_checkpoints_git_workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "git_workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "git_changed_files",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CheckpointId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Path = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    PreviousPath = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    IndexStatus = table.Column<string>(type: "TEXT", maxLength: 1, nullable: false),
                    WorkTreeStatus = table.Column<string>(type: "TEXT", maxLength: 1, nullable: false),
                    GitCheckpointId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_git_changed_files", x => x.Id);
                    table.ForeignKey(
                        name: "FK_git_changed_files_git_checkpoints_CheckpointId",
                        column: x => x.CheckpointId,
                        principalTable: "git_checkpoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_git_changed_files_git_checkpoints_GitCheckpointId",
                        column: x => x.GitCheckpointId,
                        principalTable: "git_checkpoints",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_git_changed_files_CheckpointId",
                table: "git_changed_files",
                column: "CheckpointId");

            migrationBuilder.CreateIndex(
                name: "IX_git_changed_files_GitCheckpointId",
                table: "git_changed_files",
                column: "GitCheckpointId");

            migrationBuilder.CreateIndex(
                name: "IX_git_checkpoints_WorkspaceId_CheckpointNumber",
                table: "git_checkpoints",
                columns: new[] { "WorkspaceId", "CheckpointNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "git_changed_files");

            migrationBuilder.DropTable(
                name: "git_checkpoints");

            migrationBuilder.DropColumn(
                name: "NextCheckpointNumber",
                table: "git_workspaces");
        }
    }
}
