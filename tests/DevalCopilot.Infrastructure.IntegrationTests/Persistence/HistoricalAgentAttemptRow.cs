using System.Diagnostics.CodeAnalysis;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// Writes one Attempt into a database that is deliberately stopped at an older migration. Entity Framework would also write every
/// column of the current model, including columns a later migration adds and this older table does not have yet, so a migration
/// test about an earlier schema cannot save the entity the ordinary way. This writes the attempt's own persisted facts instead:
/// every column the current model maps for it, converted by the model's own type mappings exactly as an ordinary save would
/// convert it, and omitting only the columns the table does not have at that migration. Nothing is invented, defaulted or
/// dropped: a fact a column exists for is written as the attempt holds it.
/// </summary>
internal static class HistoricalAgentAttemptRow
{
    internal static Task InsertAsync(DevalCopilotDbContext context, Attempt attempt) => HistoricalEntityRow.InsertAsync(context, attempt);

    /// <summary>Every mapped fact of the original Attempt (identity, workspace, checkpoint, fingerprint, manifest, protocol, contract,
    /// permission profile, adapter version, capture bounds, budget slot, timeouts and the rest) must be the fact that was read back.
    /// A fact the older schema never had is null on both sides, so nothing here is excused and nothing is invented.</summary>
    internal static void AssertEveryFactSurvived(DevalCopilotDbContext context, Attempt expected, Attempt actual)
    {
        var entityType = context.Model.FindEntityType(typeof(Attempt))!;
        var expectedEntry = context.Entry(expected);
        var actualEntry = context.Entry(actual);
        var suppliedFacts = 0;
        foreach (var property in entityType.GetProperties())
        {
            var want = expectedEntry.Property(property.Name).CurrentValue;
            var got = actualEntry.Property(property.Name).CurrentValue;
            suppliedFacts += want is null ? 0 : 1;
            var same = want is System.Collections.IEnumerable and not string && got is System.Collections.IEnumerable and not string
                ? ((System.Collections.IEnumerable)want).Cast<object>().SequenceEqual(((System.Collections.IEnumerable)got).Cast<object>())
                : Equals(want, got);
            Assert.True(same, $"Attempt.{property.Name} was {Describe(want)} when seeded and {Describe(got)} after the round trip.");
        }

        // Not vacuous: the claimed attempt really supplied these, so a NULL on the way back is a lost fact.
        Assert.NotNull(expected.AgentGitWorkspaceId);
        Assert.NotNull(expected.AgentGitCheckpointId);
        Assert.NotNull(expected.AgentCheckpointFingerprintSha256);
        Assert.NotNull(expected.AgentContextManifestArtifactId);
        Assert.NotNull(expected.AgentProtocolVersion);
        Assert.NotNull(expected.AgentPermissionProfile);
        Assert.NotNull(expected.AgentAdapterContractVersion);
        Assert.NotNull(expected.AgentMaxBytesPerStream);
        Assert.NotNull(expected.AgentMaxTotalCapturedBytes);
        Assert.NotNull(expected.AgentBudgetSlot);
        Assert.True(suppliedFacts >= 15, $"Only {suppliedFacts} facts were supplied; the comparison would prove too little.");
    }

    private static string Describe(object? value) => value is null ? "NULL" : value.ToString() ?? "(unprintable)";
}
