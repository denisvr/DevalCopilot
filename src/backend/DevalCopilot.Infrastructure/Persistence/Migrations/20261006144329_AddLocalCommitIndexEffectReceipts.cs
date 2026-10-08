using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevalCopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLocalCommitIndexEffectReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "IndexAcquiredAtUtc",
                table: "local_commit_operations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IndexAdministrativeDirectoryIdentity",
                table: "local_commit_operations",
                type: "TEXT",
                maxLength: 49,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IndexLockIdentity",
                table: "local_commit_operations",
                type: "TEXT",
                maxLength: 49,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "IndexLockLength",
                table: "local_commit_operations",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IndexPreimageIdentity",
                table: "local_commit_operations",
                type: "TEXT",
                maxLength: 49,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "IndexPreimageLength",
                table: "local_commit_operations",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IndexQuarantineName",
                table: "local_commit_operations",
                type: "TEXT",
                maxLength: 240,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "IndexReplacementPlannedAtUtc",
                table: "local_commit_operations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreparedIndexArtifactIdentity",
                table: "local_commit_operations",
                type: "TEXT",
                maxLength: 49,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PreparedIndexArtifactLength",
                table: "local_commit_operations",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IndexAcquiredAtUtc",
                table: "local_commit_operations");

            migrationBuilder.DropColumn(
                name: "IndexAdministrativeDirectoryIdentity",
                table: "local_commit_operations");

            migrationBuilder.DropColumn(
                name: "IndexLockIdentity",
                table: "local_commit_operations");

            migrationBuilder.DropColumn(
                name: "IndexLockLength",
                table: "local_commit_operations");

            migrationBuilder.DropColumn(
                name: "IndexPreimageIdentity",
                table: "local_commit_operations");

            migrationBuilder.DropColumn(
                name: "IndexPreimageLength",
                table: "local_commit_operations");

            migrationBuilder.DropColumn(
                name: "IndexQuarantineName",
                table: "local_commit_operations");

            migrationBuilder.DropColumn(
                name: "IndexReplacementPlannedAtUtc",
                table: "local_commit_operations");

            migrationBuilder.DropColumn(
                name: "PreparedIndexArtifactIdentity",
                table: "local_commit_operations");

            migrationBuilder.DropColumn(
                name: "PreparedIndexArtifactLength",
                table: "local_commit_operations");
        }
    }
}
