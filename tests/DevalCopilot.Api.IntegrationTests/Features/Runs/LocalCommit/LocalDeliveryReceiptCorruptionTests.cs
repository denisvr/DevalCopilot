using System.Net;
using System.Text.Json;
using DevalCopilot.Application.Features.Runs.Policies.LocalCommit;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// A receipt is the recorded delivery or it is unavailable (ADR-0032). Each case breaks exactly one recorded fact of a real,
/// completed two-recipe delivery, proves the whole receipt becomes Unavailable with a null body (never a partial or substituted
/// one), then restores the rows and proves the receipt returns byte for byte, so no case can pass by leaving earlier damage behind.
/// </summary>
public sealed class LocalDeliveryReceiptCorruptionTests : LocalDeliveryReceiptTestBase
{
    private sealed record Case(string Name, string[] Tables, string[] Statements);

    private sealed record Facts(
        LocalCommitLineageIds Ids,
        Guid OperationId,
        Guid ImplementerAttemptId,
        Guid ApprovalMessageId,
        Guid AgentReviewId,
        Guid SecondCommandId,
        Guid FirstExecutionId,
        Guid SecondExecutionId,
        string SubsetHumanDigest,
        string ZeroNumberMemberDigest);

    private async Task<Facts> ReadFactsAsync(LocalCommitLineageIds ids, Guid operationId)
    {
        await using var db = OpenDb();
        var operation = await db.LocalCommitOperations.AsNoTracking().SingleAsync(candidate => candidate.Id == operationId);
        var report = await db.CollaborationMessages.AsNoTracking().SingleAsync(message => message.Id == ids.ExecutionReportId);
        var second = await db.VerificationExecutions.AsNoTracking().SingleAsync(execution => execution.Id == ids.AllExecutionIds![1]);
        var human = await db.CheckpointReviews.AsNoTracking().SingleAsync(review => review.Id == ids.HumanReviewId);
        var humanEvidence = await db.CheckpointReviewEvidence.AsNoTracking()
            .Where(row => row.CheckpointReviewId == ids.HumanReviewId).ToListAsync();
        var subsetDigest = LocalCommitMemberDigests.HumanDecision(human, humanEvidence.Where(row => row.VerificationExecutionId != second.Id));
        var zeroNumberDigest = LocalCommitMemberDigests.Verification(
            second.VerificationCommandId, second.Id, 0, second.CommandName, second.ExecutablePath, second.TimeoutSeconds, second.Arguments,
            second.CompletionFingerprintSha256);
        return new Facts(
            ids, operationId, report.AttemptId!.Value, operation.CodeReviewApprovalMessageId, operation.AgentCheckpointReviewId,
            second.VerificationCommandId, ids.AllExecutionIds![0], ids.AllExecutionIds[1], subsetDigest, zeroNumberDigest);
    }

    private static Case[] Cases(Facts f)
    {
        var ids = f.Ids;
        var operation = Upper(f.OperationId);
        var first = Upper(f.FirstExecutionId);
        var second = Upper(f.SecondExecutionId);
        var someone = Upper(Guid.NewGuid());
        const string Operations = "local_commit_operations";
        const string Members = "local_commit_authority_members";
        const string Messages = "collaboration_messages";
        const string Attempts = "attempts";
        const string Evidence = "attempt_verification_evidence";
        const string Inputs = "attempt_input_messages";
        const string Reviews = "checkpoint_reviews";
        const string ReviewEvidence = "checkpoint_review_evidence";
        const string Executions = "verification_executions";
        var verificationMembers = $"\"OperationId\" = '{operation}' AND \"Kind\" = 'Verification'";
        var humanMember = $"\"OperationId\" = '{operation}' AND \"Kind\" = 'HumanReview'";
        var review = $"\"AttemptId\" = '{Upper(ids.ReviewAttemptId)}'";

        return
        [
            new("operation commit sha is malformed", [Operations], [$"UPDATE {Operations} SET \"CommitSha\" = 'not-a-sha' WHERE \"Id\" = '{operation}'"]),
            new("completed operation has no completion time", [Operations], [$"UPDATE {Operations} SET \"CompletedAtUtc\" = NULL WHERE \"Id\" = '{operation}'"]),
            new("operation fingerprint is not the checkpoint's", [Operations], [$"UPDATE {Operations} SET \"CheckpointFingerprintSha256\" = '{Zeros64}' WHERE \"Id\" = '{operation}'"]),
            new("operation project is not the run's", [Operations], [$"UPDATE {Operations} SET \"ProjectId\" = '{someone}' WHERE \"Id\" = '{operation}'"]),
            new("operation checkpoint is another checkpoint", [Operations], [$"UPDATE {Operations} SET \"GitCheckpointId\" = '{someone}' WHERE \"Id\" = '{operation}'"]),
            new("checkpoint number differs", ["git_checkpoints"], [$"UPDATE git_checkpoints SET \"CheckpointNumber\" = 77 WHERE \"Id\" = '{Upper(ids.CheckpointId)}'"]),
            new("checkpoint head is not the recorded parent", ["git_checkpoints"], [$"UPDATE git_checkpoints SET \"HeadCommitSha\" = '{Nines40}' WHERE \"Id\" = '{Upper(ids.CheckpointId)}'"]),
            new("checkpoint belongs to another workspace", ["git_checkpoints"], [$"UPDATE git_checkpoints SET \"WorkspaceId\" = '{someone}' WHERE \"Id\" = '{Upper(ids.CheckpointId)}'"]),
            new("workspace belongs to another project", ["git_workspaces"], [$"UPDATE git_workspaces SET \"ProjectId\" = '{someone}' WHERE \"Id\" = '{Upper(ids.WorkspaceId)}'"]),
            new("workspace branch is not the recorded branch", ["git_workspaces"], [$"UPDATE git_workspaces SET \"BranchName\" = 'elsewhere' WHERE \"Id\" = '{Upper(ids.WorkspaceId)}'"]),
            new("execution report is missing", [Messages], [$"DELETE FROM {Messages} WHERE \"Id\" = '{Upper(ids.ExecutionReportId)}'"]),
            new("execution report belongs to another run", [Messages], [$"UPDATE {Messages} SET \"RunId\" = '{someone}' WHERE \"Id\" = '{Upper(ids.ExecutionReportId)}'"]),
            new("execution report is not an execution report", [Messages], [$"UPDATE {Messages} SET \"Type\" = 'Proposal' WHERE \"Id\" = '{Upper(ids.ExecutionReportId)}'"]),
            new("execution report is human submitted", [Messages], [$"UPDATE {Messages} SET \"Provenance\" = 'HumanSubmitted' WHERE \"Id\" = '{Upper(ids.ExecutionReportId)}'"]),
            new("implementer attempt did not complete", [Attempts], [$"UPDATE {Attempts} SET \"Status\" = 'Failed' WHERE \"Id\" = '{Upper(f.ImplementerAttemptId)}'"]),
            new("implementer result checkpoint is another checkpoint", [Attempts], [$"UPDATE {Attempts} SET \"AgentResultGitCheckpointId\" = '{someone}' WHERE \"Id\" = '{Upper(f.ImplementerAttemptId)}'"]),
            new("review was launched against another message", [Inputs], [$"UPDATE {Inputs} SET \"CollaborationMessageId\" = '{Upper(f.ApprovalMessageId)}' WHERE {review}"]),
            new("approval message is missing", [Messages], [$"DELETE FROM {Messages} WHERE \"Id\" = '{Upper(f.ApprovalMessageId)}'"]),
            new("approval does not reply to the report", [Messages], [$"UPDATE {Messages} SET \"InReplyToMessageId\" = '{someone}' WHERE \"Id\" = '{Upper(f.ApprovalMessageId)}'"]),
            new("approval is not from the code reviewer", [Messages], [$"UPDATE {Messages} SET \"ActorAgentRole\" = 'Planner' WHERE \"Id\" = '{Upper(f.ApprovalMessageId)}'"]),
            new("approval belongs to another attempt", [Messages], [$"UPDATE {Messages} SET \"AttemptId\" = '{Upper(f.ImplementerAttemptId)}' WHERE \"Id\" = '{Upper(f.ApprovalMessageId)}'"]),
            new("review attempt did not approve", [Attempts], [$"UPDATE {Attempts} SET \"AgentOutcome\" = 'ReviewChangesRequested' WHERE \"Id\" = '{Upper(ids.ReviewAttemptId)}'"]),
            new("review attempt judged another checkpoint", [Attempts], [$"UPDATE {Attempts} SET \"AgentGitCheckpointId\" = '{someone}' WHERE \"Id\" = '{Upper(ids.ReviewAttemptId)}'"]),
            new("review attempt judged another fingerprint", [Attempts], [$"UPDATE {Attempts} SET \"AgentCheckpointFingerprintSha256\" = '{Zeros64}' WHERE \"Id\" = '{Upper(ids.ReviewAttemptId)}'"]),
            new("review evidence lost a member", [Evidence], [$"DELETE FROM {Evidence} WHERE {review} AND \"Sequence\" = 1"]),
            new("review evidence is reordered", [Evidence],
            [
                $"UPDATE {Evidence} SET \"Sequence\" = 9 WHERE {review} AND \"Sequence\" = 0",
                $"UPDATE {Evidence} SET \"Sequence\" = 0 WHERE {review} AND \"Sequence\" = 1",
                $"UPDATE {Evidence} SET \"Sequence\" = 1 WHERE {review} AND \"Sequence\" = 9",
            ]),
            new("review evidence names another execution", [Evidence], [$"UPDATE {Evidence} SET \"VerificationExecutionId\" = '{someone}' WHERE {review} AND \"Sequence\" = 1"]),
            new("review evidence has a gap", [Evidence], [$"UPDATE {Evidence} SET \"Sequence\" = 5 WHERE {review} AND \"Sequence\" = 1"]),
            new("human decision is not approved", [Reviews], [$"UPDATE {Reviews} SET \"Decision\" = 'ChangesRequested' WHERE \"Id\" = '{Upper(ids.HumanReviewId)}'"]),
            new("selected review is not a human", [Reviews], [$"UPDATE {Reviews} SET \"ActorKind\" = 'FutureAgent' WHERE \"Id\" = '{Upper(ids.HumanReviewId)}'"]),
            new("human review judged another fingerprint", [Reviews], [$"UPDATE {Reviews} SET \"CheckpointFingerprintSha256\" = '{Zeros64}' WHERE \"Id\" = '{Upper(ids.HumanReviewId)}'"]),
            new("human review judged another checkpoint number", [Reviews], [$"UPDATE {Reviews} SET \"CheckpointNumber\" = 41 WHERE \"Id\" = '{Upper(ids.HumanReviewId)}'"]),
            new("human evidence lost a member", [ReviewEvidence], [$"DELETE FROM {ReviewEvidence} WHERE \"CheckpointReviewId\" = '{Upper(ids.HumanReviewId)}' AND \"VerificationExecutionId\" = '{second}'"]),
            new("human evidence is no longer passed", [ReviewEvidence], [$"UPDATE {ReviewEvidence} SET \"VerificationExecutionStatus\" = 'Failed' WHERE \"CheckpointReviewId\" = '{Upper(ids.HumanReviewId)}' AND \"VerificationExecutionId\" = '{second}'"]),
            new("human evidence names another execution", [ReviewEvidence], [$"UPDATE {ReviewEvidence} SET \"VerificationExecutionId\" = '{someone}' WHERE \"CheckpointReviewId\" = '{Upper(ids.HumanReviewId)}' AND \"VerificationExecutionId\" = '{second}'"]),
            new("human review covers a subset under a consistent recorded digest", [ReviewEvidence, Members],
            [
                $"DELETE FROM {ReviewEvidence} WHERE \"CheckpointReviewId\" = '{Upper(ids.HumanReviewId)}' AND \"VerificationExecutionId\" = '{second}'",
                $"UPDATE {Members} SET \"Digest\" = '{f.SubsetHumanDigest}' WHERE {humanMember}",
            ]),
            new("human member digest differs", [Members], [$"UPDATE {Members} SET \"Digest\" = '{Zeros64}' WHERE {humanMember}"]),
            new("human member is not the selected review", [Members], [$"UPDATE {Members} SET \"SubjectId\" = '{someone}' WHERE {humanMember}"]),
            new("human member is missing", [Members], [$"DELETE FROM {Members} WHERE {humanMember}"]),
            new("agent review is not approved", [Reviews], [$"UPDATE {Reviews} SET \"Decision\" = 'ChangesRequested' WHERE \"Id\" = '{Upper(f.AgentReviewId)}'"]),
            new("agent review evidence differs", [ReviewEvidence], [$"DELETE FROM {ReviewEvidence} WHERE \"CheckpointReviewId\" = '{Upper(f.AgentReviewId)}' AND \"VerificationExecutionId\" = '{second}'"]),
            new("verification member digest differs", [Members], [$"UPDATE {Members} SET \"Digest\" = '{Zeros64}' WHERE {verificationMembers} AND \"Sequence\" = 1"]),
            new("verification members are not contiguous", [Members], [$"UPDATE {Members} SET \"Sequence\" = 5 WHERE {verificationMembers} AND \"Sequence\" = 1"]),
            new("verification members are reordered", [Members],
            [
                $"UPDATE {Members} SET \"Sequence\" = 9 WHERE {verificationMembers} AND \"Sequence\" = 0",
                $"UPDATE {Members} SET \"Sequence\" = 0 WHERE {verificationMembers} AND \"Sequence\" = 1",
                $"UPDATE {Members} SET \"Sequence\" = 1 WHERE {verificationMembers} AND \"Sequence\" = 9",
            ]),
            new("verification member is extra", [Members],
            [
                $"INSERT INTO {Members} (\"Id\", \"OperationId\", \"Kind\", \"Sequence\", \"SubjectId\", \"CommandId\", \"Digest\") "
                    + $"VALUES ('{someone}', '{operation}', 'Verification', 2, '{Upper(Guid.NewGuid())}', '{Upper(Guid.NewGuid())}', '{Zeros64}')",
            ]),
            new("verification member is missing", [Members], [$"DELETE FROM {Members} WHERE {verificationMembers} AND \"Sequence\" = 1"]),
            new("verification member repeats an execution", [Members],
                [$"UPDATE {Members} SET \"SubjectId\" = '{first}', \"CommandId\" = '{Upper(ids.CommandId)}' WHERE {verificationMembers} AND \"Sequence\" = 1"]),
            new("verification member has no command", [Members], [$"UPDATE {Members} SET \"CommandId\" = NULL WHERE {verificationMembers} AND \"Sequence\" = 1"]),
            new("execution did not pass", [Executions], [$"UPDATE {Executions} SET \"Status\" = 'Failed' WHERE \"Id\" = '{second}'"]),
            new("execution exit was not clean", [Executions], [$"UPDATE {Executions} SET \"ExitCode\" = 1 WHERE \"Id\" = '{second}'"]),
            new("execution outcome was not an exit", [Executions], [$"UPDATE {Executions} SET \"Outcome\" = 'TimedOut' WHERE \"Id\" = '{second}'"]),
            new("execution snapshot name differs", [Executions], [$"UPDATE {Executions} SET \"CommandName\" = 'Tampered' WHERE \"Id\" = '{second}'"]),
            new("execution snapshot executable differs", [Executions], [$"UPDATE {Executions} SET \"ExecutablePath\" = 'C:\\tampered.exe' WHERE \"Id\" = '{second}'"]),
            new("execution snapshot arguments differ", [Executions], [$"UPDATE {Executions} SET \"Arguments\" = '[\"tampered\"]' WHERE \"Id\" = '{second}'"]),
            new("execution snapshot timeout differs", [Executions], [$"UPDATE {Executions} SET \"TimeoutSeconds\" = 7 WHERE \"Id\" = '{second}'"]),
            new("execution number differs", [Executions], [$"UPDATE {Executions} SET \"ExecutionNumber\" = 999 WHERE \"Id\" = '{second}'"]),
            new("execution completion fingerprint differs", [Executions], [$"UPDATE {Executions} SET \"CompletionFingerprintSha256\" = '{Zeros64}' WHERE \"Id\" = '{second}'"]),
            new("execution belongs to another checkpoint", [Executions], [$"UPDATE {Executions} SET \"GitCheckpointId\" = '{someone}' WHERE \"Id\" = '{second}'"]),
            new("execution belongs to another project", [Executions], [$"UPDATE {Executions} SET \"ProjectId\" = '{someone}' WHERE \"Id\" = '{second}'"]),
            new("execution belongs to another workspace", [Executions], [$"UPDATE {Executions} SET \"GitWorkspaceId\" = '{someone}' WHERE \"Id\" = '{second}'"]),
            new("execution is another command's", [Executions], [$"UPDATE {Executions} SET \"VerificationCommandId\" = '{someone}' WHERE \"Id\" = '{second}'"]),
            new("execution was never dispatched", [Executions], [$"UPDATE {Executions} SET \"DispatchedAtUtc\" = NULL WHERE \"Id\" = '{second}'"]),
            new("execution has no completion time", [Executions], [$"UPDATE {Executions} SET \"CompletedAtUtc\" = NULL WHERE \"Id\" = '{second}'"]),
            new("execution ran in another workspace path", [Executions], [$"UPDATE {Executions} SET \"WorkspacePath\" = 'C:\\elsewhere' WHERE \"Id\" = '{second}'"]),
            new("run is not recorded as completed", ["runs"], [$"UPDATE runs SET \"Lifecycle\" = 'Running' WHERE \"Id\" = '{Upper(ids.RunId)}'"]),
            new("run lifecycle is unrecognized", ["runs"], [$"UPDATE runs SET \"Lifecycle\" = 'Bogus' WHERE \"Id\" = '{Upper(ids.RunId)}'"]),
            new("run stage is not completed", ["runs"], [$"UPDATE runs SET \"Stage\" = 'Execute' WHERE \"Id\" = '{Upper(ids.RunId)}'"]),
            new("run stage is unrecognized", ["runs"], [$"UPDATE runs SET \"Stage\" = 'Bogus' WHERE \"Id\" = '{Upper(ids.RunId)}'"]),
            new("run completion time is not the operation's", ["runs"], [$"UPDATE runs SET \"LastAdvancedAtUtc\" = \"CreatedAtUtc\" WHERE \"Id\" = '{Upper(ids.RunId)}'"]),
            new("changed path count is negative", [Operations], [$"UPDATE {Operations} SET \"ChangedPathCount\" = -1 WHERE \"Id\" = '{operation}'"]),
            new("changed path count is zero", [Operations], [$"UPDATE {Operations} SET \"ChangedPathCount\" = 0 WHERE \"Id\" = '{operation}'"]),
            new("changed path count is over the maximum", [Operations], [$"UPDATE {Operations} SET \"ChangedPathCount\" = 129 WHERE \"Id\" = '{operation}'"]),
            new("checkpoint number is not positive everywhere it is recorded", [Operations, "git_checkpoints", Reviews],
            [
                $"UPDATE {Operations} SET \"CheckpointNumber\" = 0 WHERE \"Id\" = '{operation}'",
                $"UPDATE git_checkpoints SET \"CheckpointNumber\" = 0 WHERE \"Id\" = '{Upper(ids.CheckpointId)}'",
                $"UPDATE {Reviews} SET \"CheckpointNumber\" = 0 WHERE \"GitCheckpointId\" = '{Upper(ids.CheckpointId)}'",
            ]),
            new("review attempt number is not positive", [Attempts], [$"UPDATE {Attempts} SET \"AttemptNumber\" = 0 WHERE \"Id\" = '{Upper(ids.ReviewAttemptId)}'"]),
            new("execution number is not positive everywhere it is recorded", [Executions, ReviewEvidence, Members],
            [
                $"UPDATE {Executions} SET \"ExecutionNumber\" = 0 WHERE \"Id\" = '{second}'",
                $"UPDATE {ReviewEvidence} SET \"VerificationExecutionNumber\" = 0 WHERE \"VerificationExecutionId\" = '{second}'",
                $"UPDATE {Members} SET \"Digest\" = '{f.ZeroNumberMemberDigest}' WHERE {verificationMembers} AND \"Sequence\" = 1",
            ]),
            new("approval message provider is not the review attempt's", [Messages], [$"UPDATE {Messages} SET \"ActorAgentProvider\" = 'ClaudeCode' WHERE \"Id\" = '{Upper(f.ApprovalMessageId)}'"]),
            new("approval message has no provider", [Messages], [$"UPDATE {Messages} SET \"ActorAgentProvider\" = NULL WHERE \"Id\" = '{Upper(f.ApprovalMessageId)}'"]),
            new("human evidence exit code differs from its execution", [ReviewEvidence], [$"UPDATE {ReviewEvidence} SET \"VerificationExecutionExitCode\" = 17 WHERE \"CheckpointReviewId\" = '{Upper(ids.HumanReviewId)}' AND \"VerificationExecutionId\" = '{second}'"]),
            new("human evidence exit code is missing", [ReviewEvidence], [$"UPDATE {ReviewEvidence} SET \"VerificationExecutionExitCode\" = NULL WHERE \"CheckpointReviewId\" = '{Upper(ids.HumanReviewId)}' AND \"VerificationExecutionId\" = '{second}'"]),
            new("human evidence outcome differs from its execution", [ReviewEvidence], [$"UPDATE {ReviewEvidence} SET \"VerificationExecutionOutcome\" = 'TimedOut' WHERE \"CheckpointReviewId\" = '{Upper(ids.HumanReviewId)}' AND \"VerificationExecutionId\" = '{second}'"]),
            new("human evidence outcome is missing", [ReviewEvidence], [$"UPDATE {ReviewEvidence} SET \"VerificationExecutionOutcome\" = NULL WHERE \"CheckpointReviewId\" = '{Upper(ids.HumanReviewId)}' AND \"VerificationExecutionId\" = '{second}'"]),
            new("human evidence execution number differs from its execution", [ReviewEvidence], [$"UPDATE {ReviewEvidence} SET \"VerificationExecutionNumber\" = 999 WHERE \"CheckpointReviewId\" = '{Upper(ids.HumanReviewId)}' AND \"VerificationExecutionId\" = '{second}'"]),
            new("human evidence fingerprint differs from its execution", [ReviewEvidence], [$"UPDATE {ReviewEvidence} SET \"VerificationExecutionCheckpointFingerprintSha256\" = '{Zeros64}' WHERE \"CheckpointReviewId\" = '{Upper(ids.HumanReviewId)}' AND \"VerificationExecutionId\" = '{second}'"]),
            new("human evidence status differs from its execution", [ReviewEvidence], [$"UPDATE {ReviewEvidence} SET \"VerificationExecutionStatus\" = 'Failed' WHERE \"CheckpointReviewId\" = '{Upper(ids.HumanReviewId)}' AND \"VerificationExecutionId\" = '{second}'"]),
            new("agent evidence exit code differs from its execution", [ReviewEvidence], [$"UPDATE {ReviewEvidence} SET \"VerificationExecutionExitCode\" = 17 WHERE \"CheckpointReviewId\" = '{Upper(f.AgentReviewId)}' AND \"VerificationExecutionId\" = '{second}'"]),
            new("agent evidence exit code is missing", [ReviewEvidence], [$"UPDATE {ReviewEvidence} SET \"VerificationExecutionExitCode\" = NULL WHERE \"CheckpointReviewId\" = '{Upper(f.AgentReviewId)}' AND \"VerificationExecutionId\" = '{second}'"]),
            new("agent evidence outcome differs from its execution", [ReviewEvidence], [$"UPDATE {ReviewEvidence} SET \"VerificationExecutionOutcome\" = 'TimedOut' WHERE \"CheckpointReviewId\" = '{Upper(f.AgentReviewId)}' AND \"VerificationExecutionId\" = '{second}'"]),
            new("agent evidence outcome is missing", [ReviewEvidence], [$"UPDATE {ReviewEvidence} SET \"VerificationExecutionOutcome\" = NULL WHERE \"CheckpointReviewId\" = '{Upper(f.AgentReviewId)}' AND \"VerificationExecutionId\" = '{second}'"]),
            new("agent evidence execution number differs from its execution", [ReviewEvidence], [$"UPDATE {ReviewEvidence} SET \"VerificationExecutionNumber\" = 999 WHERE \"CheckpointReviewId\" = '{Upper(f.AgentReviewId)}' AND \"VerificationExecutionId\" = '{second}'"]),
            new("agent evidence fingerprint differs from its execution", [ReviewEvidence], [$"UPDATE {ReviewEvidence} SET \"VerificationExecutionCheckpointFingerprintSha256\" = '{Zeros64}' WHERE \"CheckpointReviewId\" = '{Upper(f.AgentReviewId)}' AND \"VerificationExecutionId\" = '{second}'"]),
            new("agent evidence status differs from its execution", [ReviewEvidence], [$"UPDATE {ReviewEvidence} SET \"VerificationExecutionStatus\" = 'Failed' WHERE \"CheckpointReviewId\" = '{Upper(f.AgentReviewId)}' AND \"VerificationExecutionId\" = '{second}'"]),
            new("execution is missing", [Executions], [$"DELETE FROM {Executions} WHERE \"Id\" = '{second}'"]),
        ];
    }

    [Fact]
    public async Task Every_single_broken_recorded_fact_makes_the_whole_receipt_unavailable_and_restoring_it_restores_the_receipt()
    {
        using var host = StartHost(runSupervisor: true);
        var (ids, operationId) = await DeliverAsync(host, recipes: 2, completeSet: true);
        var facts = await ReadFactsAsync(ids, operationId);
        var intact = await ReceiptAsync(host, ids.RunId);
        Assert.Equal("Available", intact.State);
        var cases = Cases(facts);
        Assert.True(cases.Length >= 60);
        Assert.Equal(cases.Length, cases.Select(candidate => candidate.Name).Distinct().Count());

        var failures = new List<string>();
        foreach (var broken in cases)
        {
            await CorruptAsync(broken.Tables, broken.Statements, async () =>
            {
                var reading = await ReceiptAsync(host, ids.RunId);
                if (reading.Status != HttpStatusCode.OK || reading.State != "Unavailable"
                    || reading.Root.GetProperty("receipt").ValueKind != JsonValueKind.Null)
                {
                    failures.Add($"{broken.Name}: {reading.Status} {reading.Body.Length switch { > 300 => reading.Body[..300], _ => reading.Body }}");
                }
            });

            var restored = await ReceiptAsync(host, ids.RunId);
            if (restored.Body != intact.Body)
            {
                failures.Add($"{broken.Name}: not restored byte for byte");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public async Task The_legacy_single_approval_delivery_also_rejects_run_scalar_and_review_snapshot_contradictions()
    {
        using var host = StartHost(runSupervisor: true);
        var (ids, operationId) = await DeliverAsync(host, recipes: 1);
        var intact = await ReceiptAsync(host, ids.RunId);
        Assert.Equal("Available", intact.State);
        var execution = Upper(ids.ExecutionId);
        await using var db = OpenDb();
        var agentReviewId = Upper((await db.LocalCommitOperations.AsNoTracking().SingleAsync(row => row.Id == operationId)).AgentCheckpointReviewId);
        var human = Upper(ids.HumanReviewId);
        var cases = new (string Name, string[] Tables, string Sql)[]
        {
            ("run not completed", ["runs"], $"UPDATE runs SET \"Lifecycle\" = 'Running' WHERE \"Id\" = '{Upper(ids.RunId)}'"),
            ("changed path count -1", ["local_commit_operations"], $"UPDATE local_commit_operations SET \"ChangedPathCount\" = -1 WHERE \"Id\" = '{Upper(operationId)}'"),
            ("human exit code", ["checkpoint_review_evidence"], $"UPDATE checkpoint_review_evidence SET \"VerificationExecutionExitCode\" = 17 WHERE \"CheckpointReviewId\" = '{human}' AND \"VerificationExecutionId\" = '{execution}'"),
            ("human execution number", ["checkpoint_review_evidence"], $"UPDATE checkpoint_review_evidence SET \"VerificationExecutionNumber\" = 999 WHERE \"CheckpointReviewId\" = '{human}' AND \"VerificationExecutionId\" = '{execution}'"),
            ("agent status", ["checkpoint_review_evidence"], $"UPDATE checkpoint_review_evidence SET \"VerificationExecutionStatus\" = 'Failed' WHERE \"CheckpointReviewId\" = '{agentReviewId}'"),
            ("agent fingerprint", ["checkpoint_review_evidence"], $"UPDATE checkpoint_review_evidence SET \"VerificationExecutionCheckpointFingerprintSha256\" = '{Zeros64}' WHERE \"CheckpointReviewId\" = '{agentReviewId}'"),
        };

        foreach (var (name, tables, sql) in cases)
        {
            await CorruptAsync(tables, [sql], async () =>
            {
                var reading = await ReceiptAsync(host, ids.RunId);
                Assert.True(
                    reading.Status == HttpStatusCode.OK && reading.State == "Unavailable"
                        && reading.Root.GetProperty("receipt").ValueKind == JsonValueKind.Null,
                    $"{name}: {reading.Status} {reading.State}");
            });
            Assert.Equal(intact.Body, (await ReceiptAsync(host, ids.RunId)).Body);
        }
    }

    [Fact]
    public async Task An_unrecognized_stored_status_or_member_kind_is_never_read_as_a_completion()
    {
        using var host = StartHost(runSupervisor: true);
        var (ids, operationId) = await DeliverAsync(host, recipes: 1);
        var intact = await ReceiptAsync(host, ids.RunId);

        await CorruptAsync(
            ["local_commit_operations"],
            [$"UPDATE local_commit_operations SET \"Status\" = 'Bogus' WHERE \"Id\" = '{Upper(operationId)}'"],
            async () =>
            {
                var reading = await ReceiptAsync(host, ids.RunId);
                Assert.True(reading.Status == HttpStatusCode.OK, host.ServerErrors.Describe());
                Assert.NotEqual("Available", reading.State);
                Assert.Equal(JsonValueKind.Null, reading.Root.GetProperty("receipt").ValueKind);
            });
        await CorruptAsync(
            ["local_commit_authority_members"],
            [$"UPDATE local_commit_authority_members SET \"Kind\" = 'Bogus' WHERE \"OperationId\" = '{Upper(operationId)}' AND \"Kind\" = 'Verification'"],
            async () =>
            {
                var reading = await ReceiptAsync(host, ids.RunId);
                Assert.Equal(HttpStatusCode.OK, reading.Status);
                Assert.Equal("Unavailable", reading.State);
                Assert.Equal(JsonValueKind.Null, reading.Root.GetProperty("receipt").ValueKind);
            });

        Assert.Equal(intact.Body, (await ReceiptAsync(host, ids.RunId)).Body);
    }

    [Fact]
    public async Task A_foreign_run_verification_execution_cannot_stand_in_for_a_pinned_member()
    {
        using var host = StartHost(runSupervisor: true);
        var (ids, operationId) = await DeliverAsync(host, recipes: 1);
        var other = await LocalCommitLineage.SeedAsync(host, Scene, continueFrom: ids);
        var intact = await ReceiptAsync(host, ids.RunId);
        Assert.Equal("Available", intact.State);
        var foreignExecution = Upper(other.ExecutionId);

        await CorruptAsync(
            ["local_commit_authority_members", "attempt_verification_evidence", "checkpoint_review_evidence"],
            [
                $"UPDATE local_commit_authority_members SET \"SubjectId\" = '{foreignExecution}' WHERE \"OperationId\" = '{Upper(operationId)}' AND \"Kind\" = 'Verification'",
                $"UPDATE attempt_verification_evidence SET \"VerificationExecutionId\" = '{foreignExecution}' WHERE \"AttemptId\" = '{Upper(ids.ReviewAttemptId)}'",
                $"UPDATE checkpoint_review_evidence SET \"VerificationExecutionId\" = '{foreignExecution}' WHERE \"CheckpointReviewId\" = '{Upper(ids.HumanReviewId)}'",
            ],
            async () =>
            {
                var reading = await ReceiptAsync(host, ids.RunId);
                Assert.Equal("Unavailable", reading.State);
                Assert.Equal(JsonValueKind.Null, reading.Root.GetProperty("receipt").ValueKind);
            });

        Assert.Equal(intact.Body, (await ReceiptAsync(host, ids.RunId)).Body);
    }

    [Fact]
    public async Task Thirty_three_recorded_members_are_never_truncated_to_a_receipt()
    {
        using var host = StartHost(runSupervisor: true);
        var (ids, operationId) = await DeliverAsync(host, recipes: 1);

        var statements = new List<string>();
        for (var sequence = 1; sequence <= 32; sequence++)
        {
            var execution = Upper(Guid.NewGuid());
            var command = Upper(Guid.NewGuid());
            statements.Add(
                "INSERT INTO local_commit_authority_members (\"Id\", \"OperationId\", \"Kind\", \"Sequence\", \"SubjectId\", \"CommandId\", \"Digest\") "
                + $"VALUES ('{Upper(Guid.NewGuid())}', '{Upper(operationId)}', 'Verification', {sequence}, '{execution}', '{command}', '{Zeros64}')");
        }

        await CorruptAsync(["local_commit_authority_members"], [.. statements], async () =>
        {
            var reading = await ReceiptAsync(host, ids.RunId);
            Assert.Equal("Unavailable", reading.State);
            Assert.Equal(JsonValueKind.Null, reading.Root.GetProperty("receipt").ValueKind);
        });

        Assert.Equal("Available", (await ReceiptAsync(host, ids.RunId)).State);
    }
}
