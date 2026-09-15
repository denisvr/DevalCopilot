using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevalCopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CorrectGitWorkspaceCheckpointSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_git_changed_files_git_checkpoints_GitCheckpointId",
                table: "git_changed_files");

            migrationBuilder.DropIndex(
                name: "IX_git_changed_files_GitCheckpointId",
                table: "git_changed_files");

            migrationBuilder.DropColumn(
                name: "GitCheckpointId",
                table: "git_changed_files");

            migrationBuilder.AlterColumn<int>(
                name: "NextCheckpointNumber",
                table: "git_workspaces",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            // The first checkpoint migration was applied locally during development with EF's
            // implicit required-int default (0). Zero is not a valid reserved checkpoint
            // number, so normalize only that legacy scaffolded value before any workspace can
            // reserve its first immutable evidence record.
            migrationBuilder.Sql("UPDATE git_workspaces SET NextCheckpointNumber = 1 WHERE NextCheckpointNumber = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "NextCheckpointNumber",
                table: "git_workspaces",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldDefaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "GitCheckpointId",
                table: "git_changed_files",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_git_changed_files_GitCheckpointId",
                table: "git_changed_files",
                column: "GitCheckpointId");

            migrationBuilder.AddForeignKey(
                name: "FK_git_changed_files_git_checkpoints_GitCheckpointId",
                table: "git_changed_files",
                column: "GitCheckpointId",
                principalTable: "git_checkpoints",
                principalColumn: "Id");
        }
    }
}
