using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevalCopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRepositoryWorkspacePreparation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Defaulted to 1, the same convention as NextExecutionNumber/NextBaselineNumber in
            // the prior migration: a legacy row's first reserved workspace number is 1, exactly
            // like a freshly registered project's.
            migrationBuilder.AddColumn<int>(
                name: "NextWorkspaceNumber",
                table: "projects",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<byte[]>(
                name: "PhysicalFileId",
                table: "projects",
                type: "BLOB",
                maxLength: 16,
                nullable: true);

            // Stored as the C# enum member's name (HasConversion<string>()) — the default for
            // every existing row must be a real, parseable member name, never an empty string,
            // which Enum.Parse would fail on the moment that row is next read.
            migrationBuilder.AddColumn<string>(
                name: "PhysicalIdentityFailureReason",
                table: "projects",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<string>(
                name: "PhysicalIdentityStatus",
                table: "projects",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "Unresolved");

            migrationBuilder.AddColumn<ulong>(
                name: "PhysicalVolumeSerialNumber",
                table: "projects",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "git_workspaces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    WorkspacePath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    BranchName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    SourceCommitSha = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    SourceBranchName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastFailureReasonCode = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_git_workspaces", x => x.Id);
                    table.ForeignKey(
                        name: "FK_git_workspaces_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "repository_mutation_leases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PhysicalVolumeSerialNumber = table.Column<ulong>(type: "INTEGER", nullable: false),
                    PhysicalFileId = table.Column<byte[]>(type: "BLOB", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    AcquiredAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ReleasedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    SupersededAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_repository_mutation_leases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_repository_mutation_leases_git_workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "git_workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_repository_mutation_leases_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_git_workspaces_ProjectId_WorkspaceNumber",
                table: "git_workspaces",
                columns: new[] { "ProjectId", "WorkspaceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_repository_mutation_leases_physical_identity_active",
                table: "repository_mutation_leases",
                columns: new[] { "PhysicalVolumeSerialNumber", "PhysicalFileId" },
                unique: true,
                filter: "\"Status\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_repository_mutation_leases_ProjectId",
                table: "repository_mutation_leases",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_repository_mutation_leases_WorkspaceId",
                table: "repository_mutation_leases",
                column: "WorkspaceId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "repository_mutation_leases");

            migrationBuilder.DropTable(
                name: "git_workspaces");

            migrationBuilder.DropColumn(
                name: "NextWorkspaceNumber",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "PhysicalFileId",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "PhysicalIdentityFailureReason",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "PhysicalIdentityStatus",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "PhysicalVolumeSerialNumber",
                table: "projects");
        }
    }
}
