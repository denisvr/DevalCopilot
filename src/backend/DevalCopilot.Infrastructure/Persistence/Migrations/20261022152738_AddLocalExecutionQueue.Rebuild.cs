using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevalCopilot.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// ADR-0062: the explicit rebuild of the sequences table. The state vocabulary check must change, and SQLite can only do that by
    /// rebuilding the table. EF Core's own rebuild is emitted last in the migration, after every other operation, but SQLite refuses to
    /// rename the rebuilt table while any trigger still names it and the guards must be recreated after the rebuild. The statements below
    /// are the rebuild EF Core generates for exactly this model change (checked against its generated script), written out so that the
    /// guards can be dropped before it and recreated after it within one migration, in both directions.
    /// </summary>
    public partial class AddLocalExecutionQueue
    {
        private static readonly string UpCreateTemporary = """
            CREATE TABLE "ef_temp_local_execution_sequences" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_local_execution_sequences" PRIMARY KEY,
                "AdmissionMode" TEXT NOT NULL,
                "AdvancementDeadlineUtc" TEXT NOT NULL,
                "ArmedAtUtc" TEXT NOT NULL,
                "CheckpointId" TEXT NOT NULL,
                "ConfirmationSha256" TEXT NOT NULL,
                "CoordinationNumber" INTEGER NOT NULL,
                "CorrectionAllowance" INTEGER NOT NULL,
                "DetailCode" TEXT NULL,
                "EndedAtUtc" TEXT NULL,
                "FinalEscalationMessageId" TEXT NULL,
                "FinalReportMessageId" TEXT NULL,
                "FinalReviewAttemptId" TEXT NULL,
                "FingerprintSha256" TEXT NOT NULL,
                "PlanAuthority" TEXT NOT NULL,
                "PlanInputsSha256" TEXT NOT NULL,
                "PlanProposalMessageId" TEXT NOT NULL,
                "ProjectId" TEXT NOT NULL,
                "ReasonCode" TEXT NULL,
                "RecipeCount" INTEGER NOT NULL,
                "Revision" INTEGER NOT NULL,
                "RunId" TEXT NOT NULL,
                "SettingsSnapshot" TEXT NOT NULL,
                "State" TEXT NOT NULL,
                "StopRequestedAtUtc" TEXT NULL,
                "UpdatedAtUtc" TEXT NOT NULL,
                "WorkspaceId" TEXT NOT NULL,
                CONSTRAINT "ck_local_execution_sequences_allowance" CHECK ("CorrectionAllowance" BETWEEN 0 AND 2),
                CONSTRAINT "ck_local_execution_sequences_authority" CHECK ("PlanAuthority" IN ('AcceptedRoot', 'AcceptedRevision', 'HumanAuthorizedRevision')),
                CONSTRAINT "ck_local_execution_sequences_confirmation" CHECK (length("ConfirmationSha256") = 64 AND "ConfirmationSha256" NOT GLOB '*[^0-9a-f]*'),
                CONSTRAINT "ck_local_execution_sequences_coordination" CHECK (typeof("CoordinationNumber") = 'integer' AND "CoordinationNumber" >= 1),
                CONSTRAINT "ck_local_execution_sequences_final" CHECK (("State" = 'ReadyForHumanApproval') = ("FinalReviewAttemptId" IS NOT NULL) AND ("State" = 'HumanEscalation') = ("FinalEscalationMessageId" IS NOT NULL) AND ("State" IN ('ReadyForHumanApproval', 'HumanEscalation')) = ("FinalReportMessageId" IS NOT NULL)),
                CONSTRAINT "ck_local_execution_sequences_fingerprint" CHECK (length("FingerprintSha256") = 64 AND "FingerprintSha256" NOT GLOB '*[^0-9a-f]*'),
                CONSTRAINT "ck_local_execution_sequences_mode" CHECK ("AdmissionMode" IN ('Immediate', 'WaitForAdmission')),
                CONSTRAINT "ck_local_execution_sequences_plan_inputs" CHECK (length("PlanInputsSha256") = 64 AND "PlanInputsSha256" NOT GLOB '*[^0-9a-f]*'),
                CONSTRAINT "ck_local_execution_sequences_queue_state" CHECK ("AdmissionMode" = 'WaitForAdmission' OR "State" <> 'WaitingForAdmission'),
                CONSTRAINT "ck_local_execution_sequences_recipes" CHECK ("RecipeCount" BETWEEN 1 AND 32),
                CONSTRAINT "ck_local_execution_sequences_revision" CHECK ("Revision" >= 1),
                CONSTRAINT "ck_local_execution_sequences_state" CHECK ("State" IN ('Armed', 'PreparingStep', 'AwaitingStepResult', 'Stopping', 'ReadyForHumanApproval', 'HumanEscalation', 'Stopped', 'NeedsAttention', 'Interrupted', 'WaitingForAdmission')),
                CONSTRAINT "FK_local_execution_sequences_attempts_FinalReviewAttemptId" FOREIGN KEY ("FinalReviewAttemptId") REFERENCES "attempts" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_local_execution_sequences_collaboration_messages_FinalEscalationMessageId" FOREIGN KEY ("FinalEscalationMessageId") REFERENCES "collaboration_messages" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_local_execution_sequences_collaboration_messages_FinalReportMessageId" FOREIGN KEY ("FinalReportMessageId") REFERENCES "collaboration_messages" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_local_execution_sequences_collaboration_messages_PlanProposalMessageId" FOREIGN KEY ("PlanProposalMessageId") REFERENCES "collaboration_messages" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_local_execution_sequences_git_checkpoints_CheckpointId" FOREIGN KEY ("CheckpointId") REFERENCES "git_checkpoints" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_local_execution_sequences_git_workspaces_WorkspaceId" FOREIGN KEY ("WorkspaceId") REFERENCES "git_workspaces" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_local_execution_sequences_runs_RunId" FOREIGN KEY ("RunId") REFERENCES "runs" ("Id") ON DELETE RESTRICT
            );
            """;

        private static readonly string UpCopy = """
            INSERT INTO "ef_temp_local_execution_sequences" ("Id", "AdmissionMode", "AdvancementDeadlineUtc", "ArmedAtUtc", "CheckpointId", "ConfirmationSha256", "CoordinationNumber", "CorrectionAllowance", "DetailCode", "EndedAtUtc", "FinalEscalationMessageId", "FinalReportMessageId", "FinalReviewAttemptId", "FingerprintSha256", "PlanAuthority", "PlanInputsSha256", "PlanProposalMessageId", "ProjectId", "ReasonCode", "RecipeCount", "Revision", "RunId", "SettingsSnapshot", "State", "StopRequestedAtUtc", "UpdatedAtUtc", "WorkspaceId")
            SELECT "Id", "AdmissionMode", "AdvancementDeadlineUtc", "ArmedAtUtc", "CheckpointId", "ConfirmationSha256", "CoordinationNumber", "CorrectionAllowance", "DetailCode", "EndedAtUtc", "FinalEscalationMessageId", "FinalReportMessageId", "FinalReviewAttemptId", "FingerprintSha256", "PlanAuthority", "PlanInputsSha256", "PlanProposalMessageId", "ProjectId", "ReasonCode", "RecipeCount", "Revision", "RunId", "SettingsSnapshot", "State", "StopRequestedAtUtc", "UpdatedAtUtc", "WorkspaceId"
            FROM "local_execution_sequences";
            """;

        private static readonly string[] UpIndexes =
        [
            """CREATE INDEX "IX_local_execution_sequences_CheckpointId" ON "local_execution_sequences" ("CheckpointId");""",
            """CREATE UNIQUE INDEX "ix_local_execution_sequences_coordination" ON "local_execution_sequences" ("CoordinationNumber");""",
            """CREATE UNIQUE INDEX "ix_local_execution_sequences_final_escalation" ON "local_execution_sequences" ("FinalEscalationMessageId") WHERE "FinalEscalationMessageId" IS NOT NULL;""",
            """CREATE INDEX "IX_local_execution_sequences_FinalReportMessageId" ON "local_execution_sequences" ("FinalReportMessageId");""",
            """CREATE INDEX "IX_local_execution_sequences_FinalReviewAttemptId" ON "local_execution_sequences" ("FinalReviewAttemptId");""",
            """CREATE INDEX "IX_local_execution_sequences_PlanProposalMessageId" ON "local_execution_sequences" ("PlanProposalMessageId");""",
            """CREATE INDEX "ix_local_execution_sequences_project" ON "local_execution_sequences" ("ProjectId");""",
            """CREATE UNIQUE INDEX "ix_local_execution_sequences_run" ON "local_execution_sequences" ("RunId");""",
            """CREATE INDEX "ix_local_execution_sequences_workspace" ON "local_execution_sequences" ("WorkspaceId");""",
        ];

        private static readonly string DownCreateTemporary = """
            CREATE TABLE "ef_temp_local_execution_sequences" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_local_execution_sequences" PRIMARY KEY,
                "AdvancementDeadlineUtc" TEXT NOT NULL,
                "ArmedAtUtc" TEXT NOT NULL,
                "CheckpointId" TEXT NOT NULL,
                "ConfirmationSha256" TEXT NOT NULL,
                "CorrectionAllowance" INTEGER NOT NULL,
                "DetailCode" TEXT NULL,
                "EndedAtUtc" TEXT NULL,
                "FinalEscalationMessageId" TEXT NULL,
                "FinalReportMessageId" TEXT NULL,
                "FinalReviewAttemptId" TEXT NULL,
                "FingerprintSha256" TEXT NOT NULL,
                "PlanAuthority" TEXT NOT NULL,
                "PlanInputsSha256" TEXT NOT NULL,
                "PlanProposalMessageId" TEXT NOT NULL,
                "ProjectId" TEXT NOT NULL,
                "ReasonCode" TEXT NULL,
                "RecipeCount" INTEGER NOT NULL,
                "Revision" INTEGER NOT NULL,
                "RunId" TEXT NOT NULL,
                "SettingsSnapshot" TEXT NOT NULL,
                "State" TEXT NOT NULL,
                "StopRequestedAtUtc" TEXT NULL,
                "UpdatedAtUtc" TEXT NOT NULL,
                "WorkspaceId" TEXT NOT NULL,
                CONSTRAINT "ck_local_execution_sequences_allowance" CHECK ("CorrectionAllowance" BETWEEN 0 AND 2),
                CONSTRAINT "ck_local_execution_sequences_authority" CHECK ("PlanAuthority" IN ('AcceptedRoot', 'AcceptedRevision', 'HumanAuthorizedRevision')),
                CONSTRAINT "ck_local_execution_sequences_confirmation" CHECK (length("ConfirmationSha256") = 64 AND "ConfirmationSha256" NOT GLOB '*[^0-9a-f]*'),
                CONSTRAINT "ck_local_execution_sequences_final" CHECK (("State" = 'ReadyForHumanApproval') = ("FinalReviewAttemptId" IS NOT NULL) AND ("State" = 'HumanEscalation') = ("FinalEscalationMessageId" IS NOT NULL) AND ("State" IN ('ReadyForHumanApproval', 'HumanEscalation')) = ("FinalReportMessageId" IS NOT NULL)),
                CONSTRAINT "ck_local_execution_sequences_fingerprint" CHECK (length("FingerprintSha256") = 64 AND "FingerprintSha256" NOT GLOB '*[^0-9a-f]*'),
                CONSTRAINT "ck_local_execution_sequences_plan_inputs" CHECK (length("PlanInputsSha256") = 64 AND "PlanInputsSha256" NOT GLOB '*[^0-9a-f]*'),
                CONSTRAINT "ck_local_execution_sequences_recipes" CHECK ("RecipeCount" BETWEEN 1 AND 32),
                CONSTRAINT "ck_local_execution_sequences_revision" CHECK ("Revision" >= 1),
                CONSTRAINT "ck_local_execution_sequences_state" CHECK ("State" IN ('Armed', 'PreparingStep', 'AwaitingStepResult', 'Stopping', 'ReadyForHumanApproval', 'HumanEscalation', 'Stopped', 'NeedsAttention', 'Interrupted')),
                CONSTRAINT "FK_local_execution_sequences_attempts_FinalReviewAttemptId" FOREIGN KEY ("FinalReviewAttemptId") REFERENCES "attempts" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_local_execution_sequences_collaboration_messages_FinalEscalationMessageId" FOREIGN KEY ("FinalEscalationMessageId") REFERENCES "collaboration_messages" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_local_execution_sequences_collaboration_messages_FinalReportMessageId" FOREIGN KEY ("FinalReportMessageId") REFERENCES "collaboration_messages" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_local_execution_sequences_collaboration_messages_PlanProposalMessageId" FOREIGN KEY ("PlanProposalMessageId") REFERENCES "collaboration_messages" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_local_execution_sequences_git_checkpoints_CheckpointId" FOREIGN KEY ("CheckpointId") REFERENCES "git_checkpoints" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_local_execution_sequences_git_workspaces_WorkspaceId" FOREIGN KEY ("WorkspaceId") REFERENCES "git_workspaces" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_local_execution_sequences_runs_RunId" FOREIGN KEY ("RunId") REFERENCES "runs" ("Id") ON DELETE RESTRICT
            );
            """;

        private static readonly string DownCopy = """
            INSERT INTO "ef_temp_local_execution_sequences" ("Id", "AdvancementDeadlineUtc", "ArmedAtUtc", "CheckpointId", "ConfirmationSha256", "CorrectionAllowance", "DetailCode", "EndedAtUtc", "FinalEscalationMessageId", "FinalReportMessageId", "FinalReviewAttemptId", "FingerprintSha256", "PlanAuthority", "PlanInputsSha256", "PlanProposalMessageId", "ProjectId", "ReasonCode", "RecipeCount", "Revision", "RunId", "SettingsSnapshot", "State", "StopRequestedAtUtc", "UpdatedAtUtc", "WorkspaceId")
            SELECT "Id", "AdvancementDeadlineUtc", "ArmedAtUtc", "CheckpointId", "ConfirmationSha256", "CorrectionAllowance", "DetailCode", "EndedAtUtc", "FinalEscalationMessageId", "FinalReportMessageId", "FinalReviewAttemptId", "FingerprintSha256", "PlanAuthority", "PlanInputsSha256", "PlanProposalMessageId", "ProjectId", "ReasonCode", "RecipeCount", "Revision", "RunId", "SettingsSnapshot", "State", "StopRequestedAtUtc", "UpdatedAtUtc", "WorkspaceId"
            FROM "local_execution_sequences";
            """;

        private static readonly string[] DownIndexes =
        [
            """CREATE INDEX "IX_local_execution_sequences_CheckpointId" ON "local_execution_sequences" ("CheckpointId");""",
            """CREATE UNIQUE INDEX "ix_local_execution_sequences_final_escalation" ON "local_execution_sequences" ("FinalEscalationMessageId") WHERE "FinalEscalationMessageId" IS NOT NULL;""",
            """CREATE INDEX "IX_local_execution_sequences_FinalReportMessageId" ON "local_execution_sequences" ("FinalReportMessageId");""",
            """CREATE INDEX "IX_local_execution_sequences_FinalReviewAttemptId" ON "local_execution_sequences" ("FinalReviewAttemptId");""",
            """CREATE INDEX "IX_local_execution_sequences_PlanProposalMessageId" ON "local_execution_sequences" ("PlanProposalMessageId");""",
            """CREATE INDEX "ix_local_execution_sequences_project" ON "local_execution_sequences" ("ProjectId");""",
            """CREATE UNIQUE INDEX "ix_local_execution_sequences_run" ON "local_execution_sequences" ("RunId");""",
            """CREATE INDEX "ix_local_execution_sequences_workspace" ON "local_execution_sequences" ("WorkspaceId");""",
        ];

        private static void RebuildSequences(MigrationBuilder migrationBuilder, string createTemporary, string copy, string[] indexes)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS ef_temp_local_execution_sequences;");
            migrationBuilder.Sql(createTemporary);
            migrationBuilder.Sql(copy);
            migrationBuilder.Sql("PRAGMA foreign_keys = 0;", suppressTransaction: true);
            migrationBuilder.Sql("DROP TABLE local_execution_sequences;");
            migrationBuilder.Sql("ALTER TABLE ef_temp_local_execution_sequences RENAME TO local_execution_sequences;");
            migrationBuilder.Sql("PRAGMA foreign_keys = 1;", suppressTransaction: true);
            foreach (var index in indexes)
            {
                migrationBuilder.Sql(index);
            }
        }
    }
}
