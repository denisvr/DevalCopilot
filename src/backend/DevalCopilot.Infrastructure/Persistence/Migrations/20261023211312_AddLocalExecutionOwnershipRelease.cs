using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevalCopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLocalExecutionOwnershipRelease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "local_execution_dispatch_witnesses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    SequenceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StepId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OperationFamily = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    AgentAttemptId = table.Column<Guid>(type: "TEXT", nullable: true),
                    VerificationExecutionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    PassNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    RecipeOrdinal = table.Column<int>(type: "INTEGER", nullable: true),
                    PredecessorStepId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SourceCheckpointId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceFingerprintSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ReportMessageId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SubjectAttemptId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AgentResponseContract = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    RecipeCommandId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AdmissionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DispatchedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    HostLaunchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Nonce = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_local_execution_dispatch_witnesses", x => x.Id);
                    table.CheckConstraint("ck_local_execution_dispatch_witnesses_contract", "\"AgentResponseContract\" IS NULL OR \"AgentResponseContract\" IN ('ImplementationReport', 'ImplementationReview', 'ReviewCorrection', 'VerificationDiagnosis')");
                    table.CheckConstraint("ck_local_execution_dispatch_witnesses_family", "(\"OperationFamily\" = 'Agent' AND \"AgentAttemptId\" IS NOT NULL AND \"VerificationExecutionId\" IS NULL AND \"AgentResponseContract\" IS NOT NULL AND \"RecipeCommandId\" IS NULL AND \"Kind\" <> 'Verification') OR (\"OperationFamily\" = 'Verification' AND \"VerificationExecutionId\" IS NOT NULL AND \"AgentAttemptId\" IS NULL AND \"AgentResponseContract\" IS NULL AND \"RecipeCommandId\" IS NOT NULL AND \"Kind\" = 'Verification')");
                    table.CheckConstraint("ck_local_execution_dispatch_witnesses_fingerprint", "typeof(\"SourceFingerprintSha256\") = 'text' AND length(\"SourceFingerprintSha256\") = 64 AND \"SourceFingerprintSha256\" NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("ck_local_execution_dispatch_witnesses_kind", "\"Kind\" IN ('Implementation', 'Verification', 'CodeReview', 'VerificationDiagnosis', 'ReviewCorrection', 'DiagnosisCorrection')");
                    table.CheckConstraint("ck_local_execution_dispatch_witnesses_ordinal", "typeof(\"Ordinal\") = 'integer' AND \"Ordinal\" BETWEEN 0 AND 101");
                    table.CheckConstraint("ck_local_execution_dispatch_witnesses_pass", "typeof(\"PassNumber\") = 'integer' AND \"PassNumber\" BETWEEN 0 AND 3");
                    table.CheckConstraint("ck_local_execution_dispatch_witnesses_recipe", "\"RecipeOrdinal\" IS NULL OR (typeof(\"RecipeOrdinal\") = 'integer' AND \"RecipeOrdinal\" BETWEEN 0 AND 31)");
                    table.CheckConstraint("ck_local_execution_dispatch_witnesses_shape", "((\"Kind\" = 'Implementation') = (\"PassNumber\" = 0)) AND ((\"Kind\" = 'Implementation') = (\"Ordinal\" = 0)) AND ((\"Kind\" = 'Verification') = (\"RecipeOrdinal\" IS NOT NULL)) AND ((\"Kind\" = 'Implementation') = (\"PredecessorStepId\" IS NULL)) AND ((\"Kind\" = 'Implementation') = (\"ReportMessageId\" IS NULL)) AND ((\"Kind\" IN ('ReviewCorrection', 'DiagnosisCorrection')) = (\"SubjectAttemptId\" IS NOT NULL))");
                    table.CheckConstraint("ck_local_execution_dispatch_witnesses_time", "typeof(\"DispatchedAtUtc\") = 'text'");
                    table.CheckConstraint("ck_local_execution_dispatch_witnesses_version", "typeof(\"Version\") = 'integer' AND \"Version\" = 1");
                    table.ForeignKey(
                        name: "FK_local_execution_dispatch_witnesses_attempts_AgentAttemptId",
                        column: x => x.AgentAttemptId,
                        principalTable: "attempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_local_execution_dispatch_witnesses_git_checkpoints_SourceCheckpointId",
                        column: x => x.SourceCheckpointId,
                        principalTable: "git_checkpoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_local_execution_dispatch_witnesses_git_workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "git_workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_local_execution_dispatch_witnesses_local_execution_admissions_AdmissionId",
                        column: x => x.AdmissionId,
                        principalTable: "local_execution_admissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_local_execution_dispatch_witnesses_local_execution_sequence_steps_StepId",
                        column: x => x.StepId,
                        principalTable: "local_execution_sequence_steps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_local_execution_dispatch_witnesses_local_execution_sequences_SequenceId",
                        column: x => x.SequenceId,
                        principalTable: "local_execution_sequences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_local_execution_dispatch_witnesses_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_local_execution_dispatch_witnesses_runs_RunId",
                        column: x => x.RunId,
                        principalTable: "runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_local_execution_dispatch_witnesses_verification_executions_VerificationExecutionId",
                        column: x => x.VerificationExecutionId,
                        principalTable: "verification_executions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "local_execution_termination_observations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    WitnessId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProcessOutcome = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    SemanticExitCode = table.Column<int>(type: "INTEGER", nullable: true),
                    ProcessDuration = table.Column<long>(type: "INTEGER", nullable: false),
                    PhysicalExitCode = table.Column<int>(type: "INTEGER", nullable: false),
                    ObservedElapsed = table.Column<long>(type: "INTEGER", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_local_execution_termination_observations", x => x.Id);
                    table.CheckConstraint("ck_local_execution_termination_observations_durations", "typeof(\"ProcessDuration\") = 'integer' AND typeof(\"ObservedElapsed\") = 'integer' AND \"ObservedElapsed\" >= 0 AND \"ObservedElapsed\" <= \"ProcessDuration\"");
                    table.CheckConstraint("ck_local_execution_termination_observations_exit_codes", "typeof(\"PhysicalExitCode\") = 'integer' AND \"PhysicalExitCode\" BETWEEN -2147483648 AND 2147483647 AND ((\"ProcessOutcome\" = 'Exited') = (\"SemanticExitCode\" IS NOT NULL)) AND (\"SemanticExitCode\" IS NULL OR (typeof(\"SemanticExitCode\") = 'integer' AND \"SemanticExitCode\" = \"PhysicalExitCode\"))");
                    table.CheckConstraint("ck_local_execution_termination_observations_outcome", "\"ProcessOutcome\" IN ('Exited', 'TimedOut', 'Cancelled')");
                    table.CheckConstraint("ck_local_execution_termination_observations_time", "typeof(\"RecordedAtUtc\") = 'text'");
                    table.CheckConstraint("ck_local_execution_termination_observations_version", "typeof(\"Version\") = 'integer' AND \"Version\" = 1");
                    table.ForeignKey(
                        name: "FK_local_execution_termination_observations_local_execution_dispatch_witnesses_WitnessId",
                        column: x => x.WitnessId,
                        principalTable: "local_execution_dispatch_witnesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "local_execution_ownership_releases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    WitnessId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ObservationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StepId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReleasedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ConfirmationVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    ConfirmationSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ConfirmationExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    IntentAdmissionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IntentAdmissionMode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    IntentAgentOutcome = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    IntentAgentResponseContract = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    IntentCompletionFingerprintSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    IntentConfirmationExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    IntentConfirmationVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    IntentDispatchedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    IntentFamily = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    IntentKind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    IntentObservationRecordedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    IntentObservedElapsed = table.Column<long>(type: "INTEGER", nullable: false),
                    IntentOperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IntentOperationStatus = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    IntentOrdinal = table.Column<int>(type: "INTEGER", nullable: false),
                    IntentPassNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    IntentPhysicalExitCode = table.Column<int>(type: "INTEGER", nullable: false),
                    IntentPredecessorStepId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IntentProcessDuration = table.Column<long>(type: "INTEGER", nullable: false),
                    IntentProcessOutcome = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    IntentProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IntentRecipeCommandId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IntentRecipeOrdinal = table.Column<int>(type: "INTEGER", nullable: true),
                    IntentReportMessageId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IntentRunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IntentSemanticExitCode = table.Column<int>(type: "INTEGER", nullable: true),
                    IntentSequenceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IntentSequenceState = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    IntentSourceCheckpointId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IntentSourceFingerprintSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    IntentSubjectAttemptId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IntentVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    IntentWorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_local_execution_ownership_releases", x => x.Id);
                    table.CheckConstraint("ck_local_execution_ownership_releases_confirmation", "typeof(\"ConfirmationVersion\") = 'integer' AND \"ConfirmationVersion\" = 1 AND typeof(\"ConfirmationSha256\") = 'text' AND length(\"ConfirmationSha256\") = 64 AND \"ConfirmationSha256\" NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("ck_local_execution_ownership_releases_intent_family", "(\"IntentFamily\" = 'Agent' AND \"IntentAgentOutcome\" IS NOT NULL AND \"IntentAgentResponseContract\" IS NOT NULL AND \"IntentCompletionFingerprintSha256\" IS NULL AND \"IntentRecipeCommandId\" IS NULL AND \"IntentKind\" <> 'Verification') OR (\"IntentFamily\" = 'Verification' AND \"IntentAgentOutcome\" IS NULL AND \"IntentAgentResponseContract\" IS NULL AND \"IntentCompletionFingerprintSha256\" IS NOT NULL AND \"IntentRecipeCommandId\" IS NOT NULL AND \"IntentKind\" = 'Verification')");
                    table.CheckConstraint("ck_local_execution_ownership_releases_intent_fingerprints", "typeof(\"IntentSourceFingerprintSha256\") = 'text' AND length(\"IntentSourceFingerprintSha256\") = 64 AND \"IntentSourceFingerprintSha256\" NOT GLOB '*[^0-9a-f]*' AND (\"IntentCompletionFingerprintSha256\" IS NULL OR (typeof(\"IntentCompletionFingerprintSha256\") = 'text' AND length(\"IntentCompletionFingerprintSha256\") = 64 AND \"IntentCompletionFingerprintSha256\" NOT GLOB '*[^0-9a-f]*'))");
                    table.CheckConstraint("ck_local_execution_ownership_releases_intent_numbers", "typeof(\"IntentOrdinal\") = 'integer' AND \"IntentOrdinal\" BETWEEN 0 AND 101 AND typeof(\"IntentPassNumber\") = 'integer' AND \"IntentPassNumber\" BETWEEN 0 AND 3 AND (\"IntentRecipeOrdinal\" IS NULL OR (typeof(\"IntentRecipeOrdinal\") = 'integer' AND \"IntentRecipeOrdinal\" BETWEEN 0 AND 31)) AND \"IntentSemanticExitCode\" IS NULL AND typeof(\"IntentPhysicalExitCode\") = 'integer' AND \"IntentPhysicalExitCode\" BETWEEN -2147483648 AND 2147483647 AND typeof(\"IntentProcessDuration\") = 'integer' AND typeof(\"IntentObservedElapsed\") = 'integer' AND \"IntentObservedElapsed\" >= 0 AND \"IntentObservedElapsed\" <= \"IntentProcessDuration\"");
                    table.CheckConstraint("ck_local_execution_ownership_releases_intent_shape", "((\"IntentKind\" = 'Implementation') = (\"IntentPassNumber\" = 0)) AND ((\"IntentKind\" = 'Implementation') = (\"IntentOrdinal\" = 0)) AND ((\"IntentKind\" = 'Verification') = (\"IntentRecipeOrdinal\" IS NOT NULL)) AND ((\"IntentKind\" = 'Implementation') = (\"IntentPredecessorStepId\" IS NULL)) AND ((\"IntentKind\" = 'Implementation') = (\"IntentReportMessageId\" IS NULL)) AND ((\"IntentKind\" IN ('ReviewCorrection', 'DiagnosisCorrection')) = (\"IntentSubjectAttemptId\" IS NOT NULL))");
                    table.CheckConstraint("ck_local_execution_ownership_releases_intent_times", "typeof(\"IntentDispatchedAtUtc\") = 'text' AND typeof(\"IntentObservationRecordedAtUtc\") = 'text' AND typeof(\"IntentConfirmationExpiresAtUtc\") = 'text'");
                    table.CheckConstraint("ck_local_execution_ownership_releases_intent_version", "typeof(\"IntentVersion\") = 'integer' AND \"IntentVersion\" = 1 AND typeof(\"IntentConfirmationVersion\") = 'integer' AND \"IntentConfirmationVersion\" = 1");
                    table.CheckConstraint("ck_local_execution_ownership_releases_intent_vocabulary", "\"IntentKind\" IN ('Implementation', 'Verification', 'CodeReview', 'VerificationDiagnosis', 'ReviewCorrection', 'DiagnosisCorrection') AND \"IntentSequenceState\" IN ('Stopped', 'NeedsAttention', 'Interrupted') AND \"IntentAdmissionMode\" IN ('Immediate', 'WaitForAdmission') AND ((\"IntentAdmissionMode\" = 'Immediate') = (\"IntentAdmissionId\" IS NULL)) AND \"IntentProcessOutcome\" IN ('TimedOut', 'Cancelled') AND (\"IntentAgentResponseContract\" IS NULL OR \"IntentAgentResponseContract\" IN ('ImplementationReport', 'ImplementationReview', 'ReviewCorrection', 'VerificationDiagnosis')) AND (\"IntentAgentOutcome\" IS NULL OR \"IntentAgentOutcome\" IN ('SourceChanged', 'ProviderInvocationFailed', 'CheckpointEvidenceUnavailable', 'VerificationEvidenceChanged')) AND typeof(\"IntentOperationStatus\") = 'text' AND length(\"IntentOperationStatus\") BETWEEN 1 AND 64");
                    table.CheckConstraint("ck_local_execution_ownership_releases_time", "typeof(\"ReleasedAtUtc\") = 'text' AND typeof(\"ConfirmationExpiresAtUtc\") = 'text' AND (CAST(strftime('%s', substr(\"ConfirmationExpiresAtUtc\", 1, 19)) AS INTEGER) * 10000000 + CASE WHEN substr(\"ConfirmationExpiresAtUtc\", 20, 1) = '.' THEN CAST(substr(substr(\"ConfirmationExpiresAtUtc\", 21, instr(substr(\"ConfirmationExpiresAtUtc\", 21), '+') - 1) || '0000000', 1, 7) AS INTEGER) ELSE 0 END) - (CAST(strftime('%s', substr(\"ReleasedAtUtc\", 1, 19)) AS INTEGER) * 10000000 + CASE WHEN substr(\"ReleasedAtUtc\", 20, 1) = '.' THEN CAST(substr(substr(\"ReleasedAtUtc\", 21, instr(substr(\"ReleasedAtUtc\", 21), '+') - 1) || '0000000', 1, 7) AS INTEGER) ELSE 0 END) <= 1200000000");
                    table.CheckConstraint("ck_local_execution_ownership_releases_version", "typeof(\"Version\") = 'integer' AND \"Version\" = 1");
                    table.ForeignKey(
                        name: "FK_local_execution_ownership_releases_local_execution_dispatch_witnesses_WitnessId",
                        column: x => x.WitnessId,
                        principalTable: "local_execution_dispatch_witnesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_local_execution_ownership_releases_local_execution_sequence_steps_StepId",
                        column: x => x.StepId,
                        principalTable: "local_execution_sequence_steps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_local_execution_ownership_releases_local_execution_termination_observations_ObservationId",
                        column: x => x.ObservationId,
                        principalTable: "local_execution_termination_observations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_local_execution_dispatch_witnesses_admission",
                table: "local_execution_dispatch_witnesses",
                column: "AdmissionId",
                unique: true,
                filter: "\"AdmissionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_local_execution_dispatch_witnesses_attempt",
                table: "local_execution_dispatch_witnesses",
                column: "AgentAttemptId",
                unique: true,
                filter: "\"AgentAttemptId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_local_execution_dispatch_witnesses_execution",
                table: "local_execution_dispatch_witnesses",
                column: "VerificationExecutionId",
                unique: true,
                filter: "\"VerificationExecutionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_local_execution_dispatch_witnesses_ProjectId",
                table: "local_execution_dispatch_witnesses",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_local_execution_dispatch_witnesses_RunId",
                table: "local_execution_dispatch_witnesses",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "ix_local_execution_dispatch_witnesses_sequence",
                table: "local_execution_dispatch_witnesses",
                column: "SequenceId");

            migrationBuilder.CreateIndex(
                name: "IX_local_execution_dispatch_witnesses_SourceCheckpointId",
                table: "local_execution_dispatch_witnesses",
                column: "SourceCheckpointId");

            migrationBuilder.CreateIndex(
                name: "ix_local_execution_dispatch_witnesses_step",
                table: "local_execution_dispatch_witnesses",
                column: "StepId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_local_execution_dispatch_witnesses_WorkspaceId",
                table: "local_execution_dispatch_witnesses",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "ix_local_execution_ownership_releases_event",
                table: "local_execution_ownership_releases",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_local_execution_ownership_releases_observation",
                table: "local_execution_ownership_releases",
                column: "ObservationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_local_execution_ownership_releases_step",
                table: "local_execution_ownership_releases",
                column: "StepId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_local_execution_ownership_releases_witness",
                table: "local_execution_ownership_releases",
                column: "WitnessId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_local_execution_termination_observations_witness",
                table: "local_execution_termination_observations",
                column: "WitnessId",
                unique: true);

            // ADR-0065: append-only guards for the three facts and the live-fence triggers recreated with the release predicate. Nothing
            // is backfilled: existing operations have no witness, observation or release and gain no release path.
            CreateGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ADR-0065: once ANY witness, observation or release exists, the previous schema cannot represent that used provenance or Human
            // decision. The downgrade is refused before any guard, row or table changes: the first statements abort the migration (and its
            // transaction) through a named CHECK on a temporary table, so the guards, the facts and the migration history are untouched.
            migrationBuilder.Sql("DROP TABLE IF EXISTS temp.local_execution_ownership_release_downgrade_check;");
            migrationBuilder.Sql(
                "CREATE TEMP TABLE local_execution_ownership_release_downgrade_check (used INTEGER NOT NULL, "
                + "CONSTRAINT used_release_feature_blocks_downgrade CHECK (used = 0));");
            migrationBuilder.Sql(
                "INSERT INTO local_execution_ownership_release_downgrade_check (used) SELECT "
                + "(SELECT COUNT(*) FROM local_execution_dispatch_witnesses) + (SELECT COUNT(*) FROM local_execution_termination_observations) "
                + "+ (SELECT COUNT(*) FROM local_execution_ownership_releases);");
            migrationBuilder.Sql("DROP TABLE temp.local_execution_ownership_release_downgrade_check;");

            DropGuards(migrationBuilder);

            migrationBuilder.DropTable(
                name: "local_execution_ownership_releases");

            migrationBuilder.DropTable(
                name: "local_execution_termination_observations");

            migrationBuilder.DropTable(
                name: "local_execution_dispatch_witnesses");
        }
    }
}
