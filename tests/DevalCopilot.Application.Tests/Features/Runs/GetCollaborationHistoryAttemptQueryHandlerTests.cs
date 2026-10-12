using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs.Queries.GetCollaborationHistoryAttempt;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// ADR-0037 at the query boundary, over real file-backed SQLite with the production migrations: the source's own stored attempt
/// reference is resolved to at most one Agent attempt of the same run through bounded scalar projections and admitted only under the
/// unchanged identity rule, every other shape is the fixed unavailable attribution without a target, no attempt is ever substituted,
/// and the read is three indexed selects that never read message text, unrelated attempt data or write anything.
/// </summary>
public sealed class GetCollaborationHistoryAttemptQueryHandlerTests : IAsyncLifetime
{
    private const string Secret = "SECRET-SENTINEL-93d1";
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private sealed class RecordingInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Commands.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            Commands.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FaultingInterceptor : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"attempts\"", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("injected database fault");
            }

            return ValueTask.FromResult(result);
        }
    }

    private async Task<Guid> SeedRunAsync()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "Attempt", $@"C:\repos\{Guid.NewGuid():N}", Now);
        dbContext.Projects.Add(project);
        var run = Run.RecordClassifiedIntent(
            Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), RunExecutionMode.ManualAgent, "Objective", Now);
        dbContext.Runs.Add(run);
        await dbContext.SaveChangesAsync();
        return run.Id;
    }

    // The five supported role/provider/contract combinations the product launches.
    private static readonly (AgentRole Role, AgentProvider Provider)[] Variants =
    [
        (AgentRole.Planner, AgentProvider.Codex),
        (AgentRole.CriticalReviewer, AgentProvider.ClaudeCode),
        (AgentRole.Resolver, AgentProvider.Codex),
        (AgentRole.Implementer, AgentProvider.ClaudeCode),
        (AgentRole.CodeReviewer, AgentProvider.Codex),
    ];

    private static Attempt Claim(Guid runId, int variant, int number, int? maxTurns = null)
    {
        var (id, workspace, checkpoint, manifest) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var timeout = TimeSpan.FromMinutes(10);
        return variant switch
        {
            0 => Attempt.ClaimAgent(id, runId, number, workspace, checkpoint, Fingerprint, manifest, timeout, 1024, 2048, Now, number),
            1 => Attempt.ClaimAgentCriticalReview(id, runId, number, workspace, checkpoint, Fingerprint, manifest, timeout, 1024, 2048, Now, number),
            2 => Attempt.ClaimAgentChallengeResolution(id, runId, number, workspace, checkpoint, Fingerprint, manifest, timeout, 1024, 2048, Now, number),
            3 => Attempt.ClaimAgentImplementationWithAssignment(
                id, runId, number, workspace, checkpoint, Fingerprint, manifest, timeout, 1024, 2048, Now, "sonnet", "high",
                AgentPermissionProfile.WorkspaceEditOnly, ClaudeMutationAdapterContract.ImplementationV2, number, maxTurns),
            _ => Attempt.ClaimAgentCodeReview(id, runId, number, workspace, checkpoint, Fingerprint, manifest, timeout, 1024, 2048, Now, number),
        };
    }

    private async Task<Attempt> SeedAttemptAsync(Guid runId, int variant = 0, int number = 1, int? maxTurns = null)
    {
        var attempt = Claim(runId, variant, number, maxTurns);
        await using var dbContext = _fixture.CreateContext();
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync();
        return attempt;
    }

    private async Task<CollaborationMessage> SeedMessageAsync(
        Guid runId,
        Guid? attemptId,
        AgentRole role = AgentRole.Planner,
        AgentProvider provider = AgentProvider.Codex,
        CollaborationMessageProvenance provenance = CollaborationMessageProvenance.ProviderObserved,
        int index = 0)
    {
        var human = provenance == CollaborationMessageProvenance.HumanSubmitted;
        var host = provenance == CollaborationMessageProvenance.HostConstructed;
        var message = CollaborationMessage.Record(
            Guid.NewGuid(), runId, attemptId, CollaborationMessage.ProtocolVersionOne,
            human ? ParticipantIdentity.ForHuman() : host ? ParticipantIdentity.ForOrchestrator() : ParticipantIdentity.ForAgent(role, provider),
            human || host ? ParticipantIdentity.ForAgent(AgentRole.Planner, AgentProvider.Codex) : ParticipantIdentity.ForOrchestrator(),
            host ? CollaborationMessageType.ExecutionReport : CollaborationMessageType.Proposal,
            host ? Guid.NewGuid() : null,
            $"Recorded summary {index}.",
            CollaborationHistoryFixtureContent.Json(host ? CollaborationMessageType.ExecutionReport : CollaborationMessageType.Proposal, index),
            provenance, Now.AddSeconds(index));
        await using var dbContext = _fixture.CreateContext();
        dbContext.CollaborationMessages.Add(message);
        await dbContext.SaveChangesAsync();
        return message;
    }

    /// <summary>A provider-observed message of the given variant that references the given attempt.</summary>
    private Task<CollaborationMessage> ObservedAsync(Guid runId, Attempt attempt, int variant, int index = 0) =>
        SeedMessageAsync(runId, attempt.Id, Variants[variant].Role, Variants[variant].Provider, index: index);

    private async Task ExecuteAsync(FormattableString sql)
    {
        await using var dbContext = _fixture.CreateContext();
        await dbContext.Database.ExecuteSqlInterpolatedAsync(sql);
    }

    private Task SetAttemptAsync(Guid id, string column, object? value) => column switch
    {
        "Kind" => ExecuteAsync($"UPDATE attempts SET Kind = {value} WHERE Id = {id}"),
        "Status" => ExecuteAsync($"UPDATE attempts SET Status = {value} WHERE Id = {id}"),
        "AgentRole" => ExecuteAsync($"UPDATE attempts SET AgentRole = {value} WHERE Id = {id}"),
        "AgentProvider" => ExecuteAsync($"UPDATE attempts SET AgentProvider = {value} WHERE Id = {id}"),
        "AgentResponseContract" => ExecuteAsync($"UPDATE attempts SET AgentResponseContract = {value} WHERE Id = {id}"),
        "AgentPermissionProfile" => ExecuteAsync($"UPDATE attempts SET AgentPermissionProfile = {value} WHERE Id = {id}"),
        "AttemptNumber" => ExecuteAsync($"UPDATE attempts SET AttemptNumber = {value} WHERE Id = {id}"),
        "AgentRequestedModel" => ExecuteAsync($"UPDATE attempts SET AgentRequestedModel = {value} WHERE Id = {id}"),
        "AgentObservedModel" => ExecuteAsync($"UPDATE attempts SET AgentObservedModel = {value} WHERE Id = {id}"),
        "AgentRequestedEffort" => ExecuteAsync($"UPDATE attempts SET AgentRequestedEffort = {value} WHERE Id = {id}"),
        "AgentObservedEffort" => ExecuteAsync($"UPDATE attempts SET AgentObservedEffort = {value} WHERE Id = {id}"),
        "AgentAdapterContractVersion" => ExecuteAsync($"UPDATE attempts SET AgentAdapterContractVersion = {value} WHERE Id = {id}"),
        "AgentRequestedMaxTurns" => ExecuteAsync($"UPDATE attempts SET AgentRequestedMaxTurns = {value} WHERE Id = {id}"),
        _ => throw new ArgumentOutOfRangeException(nameof(column)),
    };

    private Task SetMessageAsync(Guid id, string column, object? value) => column switch
    {
        "ProtocolVersion" => ExecuteAsync($"UPDATE collaboration_messages SET ProtocolVersion = {value} WHERE Id = {id}"),
        "Type" => ExecuteAsync($"UPDATE collaboration_messages SET Type = {value} WHERE Id = {id}"),
        "Provenance" => ExecuteAsync($"UPDATE collaboration_messages SET Provenance = {value} WHERE Id = {id}"),
        "ActorKind" => ExecuteAsync($"UPDATE collaboration_messages SET ActorKind = {value} WHERE Id = {id}"),
        "ActorAgentRole" => ExecuteAsync($"UPDATE collaboration_messages SET ActorAgentRole = {value} WHERE Id = {id}"),
        "ActorAgentProvider" => ExecuteAsync($"UPDATE collaboration_messages SET ActorAgentProvider = {value} WHERE Id = {id}"),
        "AttemptId" => ExecuteAsync($"UPDATE collaboration_messages SET AttemptId = {value} WHERE Id = {id}"),
        "Sequence" => ExecuteAsync($"UPDATE collaboration_messages SET Sequence = {value} WHERE Id = {id}"),
        _ => throw new ArgumentOutOfRangeException(nameof(column)),
    };

    /// <summary>Writes a message's attempt reference that no foreign key would accept (an empty or absent attempt), simulating a damaged or
    /// out-of-band row: foreign keys are switched off on one explicitly opened connection only.</summary>
    private async Task SetAttemptReferenceWithoutForeignKeysAsync(Guid messageId, string? attemptId)
    {
        await using var dbContext = _fixture.CreateContext();
        await dbContext.Database.OpenConnectionAsync();
        await dbContext.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"UPDATE collaboration_messages SET AttemptId = {attemptId} WHERE Id = {messageId}");
    }

    private async Task<Result<GetCollaborationHistoryAttemptQueryResult>> SendAsync(Guid runId, Guid messageId)
    {
        await using var dbContext = _fixture.CreateContext();
        return await new GetCollaborationHistoryAttemptQueryHandler(dbContext).HandleAsync(
            new GetCollaborationHistoryAttemptQuery(runId, messageId), CancellationToken.None);
    }

    private async Task<GetCollaborationHistoryAttemptQueryResult> ReadAsync(Guid runId, Guid messageId)
    {
        var result = await SendAsync(runId, messageId);
        Assert.True(result.IsSuccess, string.Join(", ", result.IsSuccess ? [] : result.Errors.Select(error => error.Code)));
        return result.Value;
    }

    private static void AssertUnavailable(GetCollaborationHistoryAttemptQueryResult result)
    {
        Assert.Equal(CollaborationAttemptStatus.Unavailable, result.Status);
        Assert.Null(result.Attempt);
    }

    private async Task<Attempt> EntityAsync(Guid id)
    {
        await using var dbContext = _fixture.CreateContext();
        return await dbContext.Attempts.AsNoTracking().SingleAsync(attempt => attempt.Id == id);
    }

    private static void AssertHeaderEquals(Attempt expected, CollaborationAttemptHeader? header)
    {
        Assert.NotNull(header);
        Assert.Equal(expected.Id, header.Id);
        Assert.Equal(expected.AttemptNumber, header.AttemptNumber);
        Assert.Equal(expected.AgentRole, header.Role);
        Assert.Equal(expected.AgentProvider, header.Provider);
        Assert.Equal(expected.AgentResponseContract, header.ResponseContract);
        Assert.Equal(expected.Status, header.Status);
        Assert.Equal(expected.ClaimedAtUtc, header.ClaimedAtUtc);
        Assert.Equal(expected.AgentDispatchedAtUtc, header.DispatchedAtUtc);
        Assert.Equal(expected.CompletedAtUtc, header.CompletedAtUtc);
    }

    [Fact]
    public async Task An_unknown_run_and_a_missing_or_foreign_source_are_one_fixed_not_found()
    {
        var runId = await SeedRunAsync();
        var otherRun = await SeedRunAsync();
        var foreign = await SeedMessageAsync(otherRun, null, provenance: CollaborationMessageProvenance.HostConstructed);

        foreach (var result in new[]
        {
            await SendAsync(Guid.NewGuid(), foreign.Id),
            await SendAsync(runId, Guid.NewGuid()),
            await SendAsync(runId, foreign.Id),
            await SendAsync(runId, Guid.Empty),
        })
        {
            Assert.True(result.IsFailure);
            var error = Assert.Single(result.Errors);
            Assert.Equal(GetCollaborationHistoryAttemptErrors.NotFoundCode, error.Code);
            Assert.Equal(ErrorType.NotFound, error.Type);
        }

        Assert.Equal(CollaborationAttemptStatus.NoAgentAttempt, (await ReadAsync(otherRun, foreign.Id)).Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task A_provider_observed_message_resolves_its_own_coherent_attempt_for_every_supported_role(int variant)
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId, variant, number: 4);
        var message = await ObservedAsync(runId, attempt, variant);

        var result = await ReadAsync(runId, message.Id);

        Assert.Equal(runId, result.RunId);
        Assert.Equal(message.Id, result.MessageId);
        Assert.Equal(message.Sequence, result.SourceSequence);
        Assert.Equal(CollaborationAttemptStatus.Available, result.Status);
        AssertHeaderEquals(await EntityAsync(attempt.Id), result.Attempt);
        Assert.Equal(Variants[variant].Role, result.Attempt!.Role);
        Assert.Equal(Variants[variant].Provider, result.Attempt.Provider);
        Assert.Equal(4, result.Attempt.AttemptNumber);
    }

    [Theory]
    [InlineData("Running", false, false)]
    [InlineData("Completed", true, true)]
    [InlineData("Failed", true, true)]
    [InlineData("Interrupted", false, true)]
    public async Task The_recorded_status_and_times_are_reported_exactly_as_stored(string status, bool dispatched, bool completed)
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId);
        var dispatchedAt = "2026-10-12 09:01:02.5+00:00";
        var completedAt = "2026-10-12 09:07:08.25+00:00";
        await ExecuteAsync($"UPDATE attempts SET Status = {status} WHERE Id = {attempt.Id}");
        if (dispatched)
        {
            await ExecuteAsync($"UPDATE attempts SET AgentDispatchedAtUtc = {dispatchedAt} WHERE Id = {attempt.Id}");
        }

        if (completed)
        {
            await ExecuteAsync($"UPDATE attempts SET CompletedAtUtc = {completedAt} WHERE Id = {attempt.Id}");
        }

        var message = await ObservedAsync(runId, attempt, 0);

        var result = await ReadAsync(runId, message.Id);

        Assert.Equal(CollaborationAttemptStatus.Available, result.Status);
        var entity = await EntityAsync(attempt.Id);
        AssertHeaderEquals(entity, result.Attempt);
        Assert.Equal(Enum.Parse<AttemptStatus>(status), result.Attempt!.Status);
        Assert.Equal(dispatched, result.Attempt.DispatchedAtUtc is not null);
        Assert.Equal(completed, result.Attempt.CompletedAtUtc is not null);
    }

    [Theory]
    [InlineData(CollaborationMessageProvenance.Simulated)]
    [InlineData(CollaborationMessageProvenance.HostConstructed)]
    [InlineData(CollaborationMessageProvenance.HumanSubmitted)]
    public async Task A_recognized_message_that_is_not_provider_observed_attributes_no_agent_attempt_and_discloses_no_target(
        CollaborationMessageProvenance provenance)
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId);
        var message = await SeedMessageAsync(runId, attempt.Id, provenance: provenance);

        var result = await ReadAsync(runId, message.Id);

        Assert.Equal(CollaborationAttemptStatus.NoAgentAttempt, result.Status);
        Assert.Null(result.Attempt);
    }

    [Fact]
    public async Task A_simulated_message_with_a_simulated_attempt_is_still_no_agent_attempt()
    {
        var runId = await SeedRunAsync();
        var simulated = Attempt.Claim(Guid.NewGuid(), runId, 1, Now);
        await using (var dbContext = _fixture.CreateContext())
        {
            dbContext.Attempts.Add(simulated);
            await dbContext.SaveChangesAsync();
        }

        var message = await SeedMessageAsync(runId, simulated.Id, provenance: CollaborationMessageProvenance.Simulated);

        var result = await ReadAsync(runId, message.Id);

        Assert.Equal(CollaborationAttemptStatus.NoAgentAttempt, result.Status);
        Assert.Null(result.Attempt);
    }

    [Theory]
    [InlineData("ProtocolVersion", "2.0")]
    [InlineData("ProtocolVersion", "1.0 ")]
    [InlineData("Type", "Overlord")]
    [InlineData("Type", "proposal")]
    [InlineData("Provenance", "Forged")]
    public async Task An_unknown_version_type_or_provenance_is_unavailable_with_no_target_even_for_a_valid_attempt(string column, string stored)
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId);
        var message = await ObservedAsync(runId, attempt, 0);
        await SetMessageAsync(message.Id, column, stored + Secret);

        var result = await ReadAsync(runId, message.Id);

        AssertUnavailable(result);
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(result), StringComparison.Ordinal);
        Assert.DoesNotContain(attempt.Id.ToString(), JsonSerializer.Serialize(result), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("null-attempt")]
    [InlineData("empty-attempt")]
    [InlineData("missing-attempt")]
    [InlineData("foreign-run-attempt")]
    [InlineData("simulated-attempt")]
    [InlineData("process-attempt")]
    [InlineData("role-mismatch")]
    [InlineData("provider-mismatch")]
    [InlineData("actor-unknown-role")]
    [InlineData("actor-unknown-provider")]
    [InlineData("actor-no-role")]
    [InlineData("actor-no-provider")]
    [InlineData("actor-not-agent")]
    [InlineData("actor-unknown-kind")]
    public async Task A_provider_observed_message_with_a_broken_or_mismatched_reference_is_unavailable_without_a_target(string defect)
    {
        var runId = await SeedRunAsync();
        var otherRun = await SeedRunAsync();
        var foreign = await SeedAttemptAsync(otherRun);
        var attempt = await SeedAttemptAsync(runId);
        var message = await ObservedAsync(runId, attempt, 0);
        switch (defect)
        {
            case "null-attempt":
                await SetMessageAsync(message.Id, "AttemptId", null);
                break;
            case "empty-attempt":
                await SetAttemptReferenceWithoutForeignKeysAsync(message.Id, "00000000-0000-0000-0000-000000000000");
                break;
            case "missing-attempt":
                await SetAttemptReferenceWithoutForeignKeysAsync(message.Id, Guid.NewGuid().ToString().ToUpperInvariant());
                break;
            case "foreign-run-attempt":
                await SetMessageAsync(message.Id, "AttemptId", foreign.Id);
                break;
            case "simulated-attempt":
                await SetAttemptAsync(attempt.Id, "Kind", "Simulated");
                break;
            case "process-attempt":
                await SetAttemptAsync(attempt.Id, "Kind", "Process");
                break;
            case "role-mismatch":
                await SetMessageAsync(message.Id, "ActorAgentRole", "Resolver");
                break;
            case "provider-mismatch":
                await SetMessageAsync(message.Id, "ActorAgentProvider", "ClaudeCode");
                break;
            case "actor-unknown-role":
                await SetMessageAsync(message.Id, "ActorAgentRole", "Overlord" + Secret);
                break;
            case "actor-unknown-provider":
                await SetMessageAsync(message.Id, "ActorAgentProvider", "Gemini" + Secret);
                break;
            case "actor-no-role":
                await SetMessageAsync(message.Id, "ActorAgentRole", null);
                break;
            case "actor-no-provider":
                await SetMessageAsync(message.Id, "ActorAgentProvider", null);
                break;
            case "actor-not-agent":
                await SetMessageAsync(message.Id, "ActorKind", "Human");
                break;
            default:
                await SetMessageAsync(message.Id, "ActorKind", "Wizard" + Secret);
                break;
        }

        var result = await ReadAsync(runId, message.Id);

        AssertUnavailable(result);
        var serialized = JsonSerializer.Serialize(result);
        Assert.DoesNotContain(Secret, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(attempt.Id.ToString(), serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(foreign.Id.ToString(), serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Only_the_stored_attempt_is_resolved_never_a_latest_or_same_role_alternative()
    {
        var runId = await SeedRunAsync();
        // The store allows one Running attempt per run, so every earlier attempt is concluded before the next is claimed.
        var older = await SeedAttemptAsync(runId, 0, number: 1);
        await SetAttemptAsync(older.Id, "Status", "Completed");
        var newer = await SeedAttemptAsync(runId, 0, number: 2);
        await SetAttemptAsync(newer.Id, "Status", "Failed");
        var latest = await SeedAttemptAsync(runId, 0, number: 3);
        var message = await ObservedAsync(runId, older, 0);

        var result = await ReadAsync(runId, message.Id);

        Assert.Equal(older.Id, result.Attempt!.Id);
        Assert.Equal(1, result.Attempt.AttemptNumber);
        Assert.NotEqual(newer.Id, result.Attempt.Id);
        Assert.NotEqual(latest.Id, result.Attempt.Id);

        // The same-role attempts that remain are never a substitute when the stored reference stops resolving.
        await SetAttemptReferenceWithoutForeignKeysAsync(message.Id, Guid.NewGuid().ToString().ToUpperInvariant());
        AssertUnavailable(await ReadAsync(runId, message.Id));
        await SetMessageAsync(message.Id, "AttemptId", null);
        AssertUnavailable(await ReadAsync(runId, message.Id));
    }

    [Theory]
    [InlineData("AgentProvider", "ClaudeCode")]
    [InlineData("AgentRole", "Implementer")]
    [InlineData("AgentResponseContract", "ReviewCorrection")]
    [InlineData("Status", "Exploded")]
    [InlineData("AgentRole", "Overlord")]
    [InlineData("AgentProvider", "Gemini")]
    [InlineData("AgentResponseContract", "Nonsense")]
    [InlineData("AgentPermissionProfile", "Nonsense")]
    public async Task An_unsupported_or_unknown_identity_is_unavailable_and_a_stored_unknown_enum_is_never_exposed(string column, string stored)
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId);
        var message = await ObservedAsync(runId, attempt, 0);
        await SetAttemptAsync(attempt.Id, column, stored + Secret);

        var result = await ReadAsync(runId, message.Id);

        AssertUnavailable(result);
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("AgentRole")]
    [InlineData("AgentProvider")]
    [InlineData("AgentResponseContract")]
    public async Task A_null_role_provider_or_contract_is_unavailable(string column)
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId);
        var message = await ObservedAsync(runId, attempt, 0);
        await SetAttemptAsync(attempt.Id, column, null);

        AssertUnavailable(await ReadAsync(runId, message.Id));
    }

    [Fact]
    public async Task A_contract_that_does_not_belong_to_the_actors_role_and_a_nonpositive_attempt_number_are_unavailable()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId);
        var message = await ObservedAsync(runId, attempt, 0);
        Assert.Equal(CollaborationAttemptStatus.Available, (await ReadAsync(runId, message.Id)).Status);

        await SetAttemptAsync(attempt.Id, "AgentResponseContract", "ChallengeResolution");
        AssertUnavailable(await ReadAsync(runId, message.Id));
        await SetAttemptAsync(attempt.Id, "AgentResponseContract", "Proposal");
        Assert.Equal(CollaborationAttemptStatus.Available, (await ReadAsync(runId, message.Id)).Status);

        foreach (var number in new[] { 0, -3 })
        {
            await SetAttemptAsync(attempt.Id, "AttemptNumber", number);
            AssertUnavailable(await ReadAsync(runId, message.Id));
        }
    }

    [Theory]
    [InlineData("AgentRequestedModel", 128, true)]
    [InlineData("AgentRequestedModel", 129, false)]
    [InlineData("AgentObservedModel", 128, true)]
    [InlineData("AgentObservedModel", 129, false)]
    [InlineData("AgentRequestedEffort", 128, true)]
    [InlineData("AgentRequestedEffort", 129, false)]
    [InlineData("AgentObservedEffort", 128, true)]
    [InlineData("AgentObservedEffort", 129, false)]
    [InlineData("AgentAdapterContractVersion", 128, true)]
    [InlineData("AgentAdapterContractVersion", 129, false)]
    public async Task Every_assignment_string_is_judged_by_the_exact_existing_bound(string column, int length, bool coherent)
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId);
        var message = await ObservedAsync(runId, attempt, 0);
        await SetAttemptAsync(attempt.Id, column, new string('x', length));

        var result = await ReadAsync(runId, message.Id);

        Assert.Equal(coherent ? CollaborationAttemptStatus.Available : CollaborationAttemptStatus.Unavailable, result.Status);
        Assert.Equal(coherent, result.Attempt is not null);
    }

    [Theory]
    [InlineData("AgentRequestedModel")]
    [InlineData("AgentObservedModel")]
    [InlineData("AgentRequestedEffort")]
    [InlineData("AgentObservedEffort")]
    [InlineData("AgentAdapterContractVersion")]
    public async Task A_blank_assignment_string_is_rejected_storage_while_null_is_legitimate(string column)
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId);
        var message = await ObservedAsync(runId, attempt, 0);

        await SetAttemptAsync(attempt.Id, column, null);
        Assert.Equal(CollaborationAttemptStatus.Available, (await ReadAsync(runId, message.Id)).Status);

        foreach (var blank in new[] { string.Empty, " ", "\t  " })
        {
            await SetAttemptAsync(attempt.Id, column, blank);
            AssertUnavailable(await ReadAsync(runId, message.Id));
        }
    }

    [Theory]
    [InlineData("AgentRequestedModel")]
    [InlineData("AgentObservedModel")]
    [InlineData("AgentRequestedEffort")]
    [InlineData("AgentObservedEffort")]
    [InlineData("AgentAdapterContractVersion")]
    public async Task A_huge_or_nul_hidden_assignment_string_is_bounded_in_the_database_and_rejected_not_read_as_null(string column)
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId);
        var message = await ObservedAsync(runId, attempt, 0);

        // Within the byte guard but over the character bound, with an embedded NUL that hides it from length() of TEXT.
        await StoreTextAsync(attempt.Id, column, Encoding.UTF8.GetBytes("a\0" + new string('b', 200)));
        AssertUnavailable(await ReadAsync(runId, message.Id));

        // Far beyond the byte guard: never materialized, still rejected rather than coerced to null.
        await StoreTextAsync(attempt.Id, column, Encoding.UTF8.GetBytes("ok\0" + new string('x', 3_000_000)));
        var recorder = new RecordingInterceptor();
        await using var dbContext = _fixture.CreateContext(recorder);
        var result = await new GetCollaborationHistoryAttemptQueryHandler(dbContext).HandleAsync(
            new GetCollaborationHistoryAttemptQuery(runId, message.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        AssertUnavailable(result.Value);
        Assert.True(JsonSerializer.Serialize(result.Value).Length < 5_000);
    }

    // An ordinary string parameter can truncate at an embedded NUL, so the text is stored from bytes cast to TEXT.
    private Task StoreTextAsync(Guid id, string column, byte[] bytes) => column switch
    {
        "AgentRequestedModel" => ExecuteAsync($"UPDATE attempts SET AgentRequestedModel = CAST({bytes} AS TEXT) WHERE Id = {id}"),
        "AgentObservedModel" => ExecuteAsync($"UPDATE attempts SET AgentObservedModel = CAST({bytes} AS TEXT) WHERE Id = {id}"),
        "AgentRequestedEffort" => ExecuteAsync($"UPDATE attempts SET AgentRequestedEffort = CAST({bytes} AS TEXT) WHERE Id = {id}"),
        "AgentObservedEffort" => ExecuteAsync($"UPDATE attempts SET AgentObservedEffort = CAST({bytes} AS TEXT) WHERE Id = {id}"),
        "AgentAdapterContractVersion" => ExecuteAsync($"UPDATE attempts SET AgentAdapterContractVersion = CAST({bytes} AS TEXT) WHERE Id = {id}"),
        _ => throw new ArgumentOutOfRangeException(nameof(column)),
    };

    public static TheoryData<string, object, string, bool> MaxTurnStorage() => new()
    {
        { "integer 1", 1L, "integer", true },
        { "integer 100", 100L, "integer", true },
        { "integer 0", 0L, "integer", false },
        { "integer 101", 101L, "integer", false },
        { "integer negative", -1L, "integer", false },
        { "integer huge", long.MaxValue, "integer", false },
        // SQLite INTEGER affinity converts these to INTEGER 7 on write, so they are valid stored integers, not text or real.
        { "text 7 becomes integer", "7", "integer", true },
        { "text padded becomes integer", "07", "integer", true },
        { "real 7.0 becomes integer", 7.0d, "integer", true },
        // These keep their own storage class and are malformed storage, never read as a request or as absent.
        { "real 7.5", 7.5d, "real", false },
        { "text abc", "abc", "text", false },
        { "text 7x", "7x", "text", false },
        { "text empty", string.Empty, "text", false },
        { "blob 7", new byte[] { 0x37 }, "blob", false },
        { "blob empty", Array.Empty<byte>(), "blob", false },
        { "huge text", new string('7', 2_000_000) + "x", "text", false },
    };

    [Theory]
    [MemberData(nameof(MaxTurnStorage))]
    public async Task The_exact_max_turn_storage_kind_is_preserved_so_malformed_storage_is_never_a_valid_or_absent_request(
        string _, object stored, string storageClass, bool valid)
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId, 3, maxTurns: 5);
        var message = await ObservedAsync(runId, attempt, 3);
        Assert.Equal(CollaborationAttemptStatus.Available, (await ReadAsync(runId, message.Id)).Status);
        await SetAttemptAsync(attempt.Id, "AgentRequestedMaxTurns", stored);
        Assert.Equal(storageClass, await ClaudeMutationTurnLimitTestSupport.ReadAttemptStorageClassAsync(_fixture, attempt.Id));

        var result = await ReadAsync(runId, message.Id);

        Assert.Equal(valid ? CollaborationAttemptStatus.Available : CollaborationAttemptStatus.Unavailable, result.Status);
        Assert.Equal(valid, result.Attempt is not null);
    }

    [Fact]
    public async Task An_absent_max_turn_request_is_legitimate_and_an_oversized_blob_is_rejected_storage_not_absence()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId, 3);
        var message = await ObservedAsync(runId, attempt, 3);
        Assert.Equal(CollaborationAttemptStatus.Available, (await ReadAsync(runId, message.Id)).Status);

        await SetAttemptAsync(attempt.Id, "AgentRequestedMaxTurns", new byte[2_000_000]);
        AssertUnavailable(await ReadAsync(runId, message.Id));
        await SetAttemptAsync(attempt.Id, "AgentRequestedMaxTurns", null);
        Assert.Equal(CollaborationAttemptStatus.Available, (await ReadAsync(runId, message.Id)).Status);
    }

    [Theory]
    [InlineData("zero-sequence")]
    [InlineData("negative-sequence")]
    [InlineData("empty-identity")]
    public async Task An_unusable_stored_source_identity_or_sequence_is_one_fixed_conflict(string defect)
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId);
        var message = await ObservedAsync(runId, attempt, 0);
        var messageId = message.Id;
        switch (defect)
        {
            case "zero-sequence":
                await SetMessageAsync(message.Id, "Sequence", 0);
                break;
            case "negative-sequence":
                await SetMessageAsync(message.Id, "Sequence", -7);
                break;
            default:
                await ExecuteAsync($"UPDATE collaboration_messages SET Id = '00000000-0000-0000-0000-000000000000' WHERE Id = {message.Id}");
                messageId = Guid.Empty;
                break;
        }

        var result = await SendAsync(runId, messageId);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("collaboration_attempt.unavailable", error.Code);
        Assert.Equal(ErrorType.Conflict, error.Type);
        Assert.DoesNotContain(attempt.Id.ToString(), error.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Source_sequences_beyond_a_browser_number_are_returned_exactly()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId);
        var message = await ObservedAsync(runId, attempt, 0);

        foreach (var sequence in new[] { 9007199254740993L, 9223372036854775806L, long.MaxValue })
        {
            await SetMessageAsync(message.Id, "Sequence", sequence);

            var result = await ReadAsync(runId, message.Id);

            Assert.Equal(sequence, result.SourceSequence);
            Assert.Equal(sequence.ToString(CultureInfo.InvariantCulture), result.SourceSequence.ToString(CultureInfo.InvariantCulture));
            Assert.Equal(CollaborationAttemptStatus.Available, result.Status);
        }
    }

    [Fact]
    public async Task The_read_is_three_indexed_selects_that_never_touch_message_text_or_unrelated_attempt_data()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId, 3, maxTurns: 5);
        var message = await ObservedAsync(runId, attempt, 3);
        var recorder = new RecordingInterceptor();
        await using var dbContext = _fixture.CreateContext(recorder);

        var result = await new GetCollaborationHistoryAttemptQueryHandler(dbContext).HandleAsync(
            new GetCollaborationHistoryAttemptQuery(runId, message.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CollaborationAttemptStatus.Available, result.Value.Status);
        Assert.Equal(3, recorder.Commands.Count);
        Assert.All(recorder.Commands, command => Assert.StartsWith("SELECT", command.TrimStart(), StringComparison.OrdinalIgnoreCase));
        Assert.All(recorder.Commands, command =>
        {
            foreach (var forbidden in new[] { "OFFSET", "COUNT(", "JOIN", "UPDATE", "INSERT", "DELETE", "ORDER BY", "LIMIT 100" })
            {
                Assert.DoesNotContain(forbidden, command, StringComparison.OrdinalIgnoreCase);
            }
        });

        var sourceRead = Assert.Single(recorder.Commands, command => command.Contains("FROM \"collaboration_messages\"", StringComparison.Ordinal));
        Assert.Contains("\"RunId\" =", sourceRead, StringComparison.Ordinal);
        Assert.Contains("\"Id\" =", sourceRead, StringComparison.Ordinal);
        Assert.Contains("AttemptId", sourceRead, StringComparison.Ordinal);
        foreach (var hidden in new[] { "\"Summary\"", "\"StructuredContentJson\"", "InReplyToMessageId" })
        {
            Assert.DoesNotContain(hidden, sourceRead, StringComparison.Ordinal);
        }

        var attemptRead = Assert.Single(recorder.Commands, command => command.Contains("FROM \"attempts\"", StringComparison.Ordinal));
        Assert.Contains("\"RunId\" =", attemptRead, StringComparison.Ordinal);
        Assert.Contains("\"Id\" =", attemptRead, StringComparison.Ordinal);
        foreach (var hidden in new[]
        {
            "AgentProviderSessionId", "ProcessArguments", "ProcessExecutablePath", "ProcessWorkingDirectory", "ProcessApprovedRoot",
            "AgentContextManifestArtifactId", "AgentGitWorkspaceId", "AgentGitCheckpointId", "AgentResultGitCheckpointId",
            "AgentCheckpointFingerprintSha256", "AgentOutcome", "AgentInputTokens", "AgentOutputTokens", "AgentModelContextLimitsSnapshot",
            "AgentDirectHumanGuidance", "AgentAccountUsageDecisionSnapshot", "AgentRepairSourceAttemptId", "AgentCodexAccountUsageStopPercent",
            "AgentProcessDuration", "AgentTimeout",
        })
        {
            Assert.DoesNotContain(hidden, attemptRead, StringComparison.Ordinal);
        }

        // Every assignment-validation string is selected only under its database byte guard (three bytes per admitted character), and the
        // max-turn storage is guarded by its own small byte bound: no unbounded stored value is ever read.
        foreach (var column in new[] { "AgentRequestedModel", "AgentObservedModel", "AgentRequestedEffort", "AgentObservedEffort", "AgentAdapterContractVersion" })
        {
            Assert.Matches($@"(?i)length\(CAST\([^)]*""{column}""\s+AS\s+BLOB\)\)\s*(<=|>)\s*384(?!\d)", attemptRead);
        }

        Assert.Matches(@"(?i)length\(CAST\([^)]*""AgentRequestedMaxTurns""\s+AS\s+BLOB\)\)\s*(<=|>)\s*64(?!\d)", attemptRead);
    }

    [Fact]
    public async Task A_message_that_is_not_provider_observed_or_unresolvable_never_reads_the_attempts_table()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId);
        var hostMessage = await SeedMessageAsync(runId, attempt.Id, provenance: CollaborationMessageProvenance.HostConstructed);
        var empty = await ObservedAsync(runId, attempt, 0, index: 1);
        await SetMessageAsync(empty.Id, "AttemptId", null);

        foreach (var id in new[] { hostMessage.Id, empty.Id })
        {
            var recorder = new RecordingInterceptor();
            await using var dbContext = _fixture.CreateContext(recorder);

            await new GetCollaborationHistoryAttemptQueryHandler(dbContext).HandleAsync(
                new GetCollaborationHistoryAttemptQuery(runId, id), CancellationToken.None);

            Assert.Equal(2, recorder.Commands.Count);
            Assert.DoesNotContain(recorder.Commands, command => command.Contains("FROM \"attempts\"", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task A_database_fault_propagates_and_is_not_masked_as_an_unavailable_attribution()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId);
        var message = await ObservedAsync(runId, attempt, 0);
        await using var dbContext = _fixture.CreateContext(new FaultingInterceptor());

        await Assert.ThrowsAsync<InvalidOperationException>(() => new GetCollaborationHistoryAttemptQueryHandler(dbContext).HandleAsync(
            new GetCollaborationHistoryAttemptQuery(runId, message.Id), CancellationToken.None));
    }

    [Fact]
    public async Task Reading_changes_no_stored_row()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId, 3, maxTurns: 9);
        var message = await ObservedAsync(runId, attempt, 3);

        async Task<string> SnapshotAsync()
        {
            await using var dbContext = _fixture.CreateContext();
            var attempts = await dbContext.Attempts.AsNoTracking().OrderBy(row => row.Id).ToListAsync();
            var messages = await dbContext.CollaborationMessages.AsNoTracking().OrderBy(row => row.Sequence).ToListAsync();
            var events = await dbContext.Events.AsNoTracking().CountAsync();
            return JsonSerializer.Serialize(attempts.Select(row => new
            {
                row.Id,
                row.Status,
                row.AttemptNumber,
                row.AgentRole,
                row.AgentRequestedModel,
                row.AgentRequestedMaxTurns,
                row.CompletedAtUtc,
            })) + JsonSerializer.Serialize(messages.Select(row => new { row.Id, row.Sequence, row.AttemptId })) + events;
        }

        var before = await SnapshotAsync();
        await ReadAsync(runId, message.Id);
        await ReadAsync(runId, message.Id);

        Assert.Equal(before, await SnapshotAsync());
    }
}
