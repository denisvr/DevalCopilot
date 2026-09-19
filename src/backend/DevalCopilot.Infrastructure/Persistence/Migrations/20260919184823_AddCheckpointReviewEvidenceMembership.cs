using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevalCopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckpointReviewEvidenceMembership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The new table is created — and backfilled from the still-present legacy columns —
            // before any of those legacy columns are ever dropped, so no existing decided
            // review's evidence is ever lost. Every existing review had at most one execution
            // snapshot, so this always produces exactly zero or one membership row per review.
            migrationBuilder.CreateTable(
                name: "checkpoint_review_evidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CheckpointReviewId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VerificationCommandId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VerificationExecutionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VerificationExecutionNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    VerificationExecutionCheckpointFingerprintSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    VerificationExecutionStatus = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    VerificationExecutionOutcome = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    VerificationExecutionExitCode = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_checkpoint_review_evidence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_checkpoint_review_evidence_checkpoint_reviews_CheckpointReviewId",
                        column: x => x.CheckpointReviewId,
                        principalTable: "checkpoint_reviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_checkpoint_review_evidence_verification_commands_VerificationCommandId",
                        column: x => x.VerificationCommandId,
                        principalTable: "verification_commands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_checkpoint_review_evidence_verification_executions_VerificationExecutionId",
                        column: x => x.VerificationExecutionId,
                        principalTable: "verification_executions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO checkpoint_review_evidence
                    (Id, CheckpointReviewId, VerificationCommandId, VerificationExecutionId, VerificationExecutionNumber,
                     VerificationExecutionCheckpointFingerprintSha256, VerificationExecutionStatus, VerificationExecutionOutcome, VerificationExecutionExitCode)
                SELECT
                    lower(hex(randomblob(4))) || '-' || lower(hex(randomblob(2))) || '-' || lower(hex(randomblob(2))) || '-' ||
                        lower(hex(randomblob(2))) || '-' || lower(hex(randomblob(6))),
                    cr.Id,
                    ve.VerificationCommandId,
                    cr.VerificationExecutionId,
                    cr.VerificationExecutionNumber,
                    cr.VerificationExecutionCheckpointFingerprintSha256,
                    cr.VerificationExecutionStatus,
                    cr.VerificationExecutionOutcome,
                    cr.VerificationExecutionExitCode
                FROM checkpoint_reviews cr
                JOIN verification_executions ve ON ve.Id = cr.VerificationExecutionId
                WHERE cr.VerificationExecutionId IS NOT NULL;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_checkpoint_reviews_verification_executions_VerificationExecutionId",
                table: "checkpoint_reviews");

            migrationBuilder.DropIndex(
                name: "IX_checkpoint_reviews_GitCheckpointId_VerificationExecutionId",
                table: "checkpoint_reviews");

            migrationBuilder.DropIndex(
                name: "IX_checkpoint_reviews_VerificationExecutionId",
                table: "checkpoint_reviews");

            migrationBuilder.DropColumn(
                name: "VerificationExecutionCheckpointFingerprintSha256",
                table: "checkpoint_reviews");

            migrationBuilder.DropColumn(
                name: "VerificationExecutionExitCode",
                table: "checkpoint_reviews");

            migrationBuilder.DropColumn(
                name: "VerificationExecutionId",
                table: "checkpoint_reviews");

            migrationBuilder.DropColumn(
                name: "VerificationExecutionNumber",
                table: "checkpoint_reviews");

            migrationBuilder.DropColumn(
                name: "VerificationExecutionOutcome",
                table: "checkpoint_reviews");

            migrationBuilder.DropColumn(
                name: "VerificationExecutionStatus",
                table: "checkpoint_reviews");

            migrationBuilder.CreateIndex(
                name: "IX_checkpoint_reviews_GitCheckpointId",
                table: "checkpoint_reviews",
                column: "GitCheckpointId");

            migrationBuilder.CreateIndex(
                name: "IX_checkpoint_review_evidence_CheckpointReviewId_VerificationCommandId",
                table: "checkpoint_review_evidence",
                columns: new[] { "CheckpointReviewId", "VerificationCommandId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_checkpoint_review_evidence_CheckpointReviewId_VerificationExecutionId",
                table: "checkpoint_review_evidence",
                columns: new[] { "CheckpointReviewId", "VerificationExecutionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_checkpoint_review_evidence_VerificationCommandId",
                table: "checkpoint_review_evidence",
                column: "VerificationCommandId");

            migrationBuilder.CreateIndex(
                name: "IX_checkpoint_review_evidence_VerificationExecutionId",
                table: "checkpoint_review_evidence",
                column: "VerificationExecutionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_checkpoint_reviews_GitCheckpointId",
                table: "checkpoint_reviews");

            migrationBuilder.AddColumn<string>(
                name: "VerificationExecutionCheckpointFingerprintSha256",
                table: "checkpoint_reviews",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VerificationExecutionExitCode",
                table: "checkpoint_reviews",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VerificationExecutionId",
                table: "checkpoint_reviews",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VerificationExecutionNumber",
                table: "checkpoint_reviews",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerificationExecutionOutcome",
                table: "checkpoint_reviews",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerificationExecutionStatus",
                table: "checkpoint_reviews",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            // Best-effort, lossy by construction for a review that claimed more than one
            // evidence member (a shape only this slice's real Codex reviews ever produce): picks
            // the lowest-numbered execution as the legacy single snapshot. Rolling back this
            // migration after a real multi-command review has been recorded is a deliberate,
            // documented data-loss trade-off of downgrading the schema, never attempted silently.
            migrationBuilder.Sql(
                """
                UPDATE checkpoint_reviews
                SET
                    VerificationExecutionId = e.VerificationExecutionId,
                    VerificationExecutionNumber = e.VerificationExecutionNumber,
                    VerificationExecutionCheckpointFingerprintSha256 = e.VerificationExecutionCheckpointFingerprintSha256,
                    VerificationExecutionStatus = e.VerificationExecutionStatus,
                    VerificationExecutionOutcome = e.VerificationExecutionOutcome,
                    VerificationExecutionExitCode = e.VerificationExecutionExitCode
                FROM (
                    SELECT cre.*
                    FROM checkpoint_review_evidence cre
                    WHERE cre.VerificationExecutionNumber = (
                        SELECT MIN(inner_cre.VerificationExecutionNumber)
                        FROM checkpoint_review_evidence inner_cre
                        WHERE inner_cre.CheckpointReviewId = cre.CheckpointReviewId
                    )
                ) e
                WHERE checkpoint_reviews.Id = e.CheckpointReviewId;
                """);

            migrationBuilder.DropTable(
                name: "checkpoint_review_evidence");

            migrationBuilder.CreateIndex(
                name: "IX_checkpoint_reviews_GitCheckpointId_VerificationExecutionId",
                table: "checkpoint_reviews",
                columns: new[] { "GitCheckpointId", "VerificationExecutionId" });

            migrationBuilder.CreateIndex(
                name: "IX_checkpoint_reviews_VerificationExecutionId",
                table: "checkpoint_reviews",
                column: "VerificationExecutionId");

            migrationBuilder.AddForeignKey(
                name: "FK_checkpoint_reviews_verification_executions_VerificationExecutionId",
                table: "checkpoint_reviews",
                column: "VerificationExecutionId",
                principalTable: "verification_executions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
