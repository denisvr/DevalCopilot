using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;

/// <summary>
/// The complete candidate a Human confirms to arm a local execution sequence (ADR-0060): the exact plan and its authority identities,
/// the starting workspace, checkpoint and fingerprint, the whole enabled verification set by value, the pinned run settings and the
/// automatic correction allowance. <see cref="ConfirmationSha256"/> binds the confirmation to one availability read: it is neither a
/// credential nor the authority or membership store.
/// </summary>
internal sealed record LocalExecutionCandidate(
    Guid RunId,
    Guid ProjectId,
    Guid PlanProposalMessageId,
    LocalExecutionPlanAuthority PlanAuthority,
    IReadOnlyList<Guid> PlanInputMessageIds,
    Guid? PlanGrantId,
    Guid WorkspaceId,
    Guid CheckpointId,
    string FingerprintSha256,
    string SettingsSnapshot,
    int CorrectionAllowance,
    IReadOnlyList<LocalExecutionRecipePin> Recipes)
{
    public const string ConfirmationVersion = "devalcopilot.local-execution-sequence.confirmation.v1";
    public const string PlanInputsVersion = "devalcopilot.local-execution-sequence.plan-inputs.v1";

    /// <summary>The versioned SHA-256 of the canonical encoding: a version line, then one <c>name=value</c> line per member where a value
    /// is the UTF-8 byte length, a colon and the text (an absent value is <c>-</c>), strings compared ordinally, identifiers in
    /// lowercase <c>D</c> form, integers in invariant form and instants as UTC ticks. Recipes are listed in pinned order with their
    /// count first, so the encoding is injective.</summary>
    public string ConfirmationSha256
    {
        get
        {
            var builder = new Canonical(ConfirmationVersion);
            builder.Add("run", RunId);
            builder.Add("project", ProjectId);
            builder.Add("plan", PlanProposalMessageId);
            builder.Add("authority", PlanAuthority.ToString());
            builder.Add("planInputs", PlanInputsSha256);
            builder.Add("workspace", WorkspaceId);
            builder.Add("checkpoint", CheckpointId);
            builder.Add("fingerprint", FingerprintSha256);
            builder.Add("settings", SettingsSnapshot);
            builder.Add("allowance", CorrectionAllowance);
            builder.Add("recipeCount", Recipes.Count);
            foreach (var recipe in Recipes)
            {
                builder.Add("recipe.ordinal", recipe.Ordinal);
                builder.Add("recipe.id", recipe.CommandId);
                builder.Add("recipe.number", recipe.CommandNumber);
                builder.Add("recipe.name", recipe.Name);
                builder.Add("recipe.path", recipe.ExecutablePath);
                builder.Add("recipe.arguments", recipe.ArgumentsJson);
                builder.Add("recipe.timeout", recipe.TimeoutSeconds);
                builder.Add("recipe.enabled", recipe.IsEnabled ? "1" : "0");
                builder.Add("recipe.configuredTicks", recipe.ConfiguredAtUtc.UtcTicks);
                builder.Add("recipe.updatedTicks", recipe.UpdatedAtUtc.UtcTicks);
            }

            return builder.Hash();
        }
    }

    /// <summary>The SHA-256 of the plan's ordered input message identities and, when a Human grant authorized it, that grant.</summary>
    public string PlanInputsSha256 => HashPlanInputs(PlanInputMessageIds, PlanGrantId);

    public static string HashPlanInputs(IReadOnlyList<Guid> orderedInputMessageIds, Guid? grantId)
    {
        var builder = new Canonical(PlanInputsVersion);
        builder.Add("count", orderedInputMessageIds.Count);
        foreach (var id in orderedInputMessageIds)
        {
            builder.Add("input", id);
        }

        builder.Add("grant", grantId);
        return builder.Hash();
    }

    private sealed class Canonical
    {
        private readonly StringBuilder text = new();

        public Canonical(string version) => text.Append(version).Append('\n');

        public void Add(string name, string? value) =>
            text.Append(name).Append('=')
                .Append(value is null ? "-" : Encoding.UTF8.GetByteCount(value).ToString(CultureInfo.InvariantCulture) + ":" + value)
                .Append('\n');

        public void Add(string name, Guid value) => Add(name, value.ToString("D", CultureInfo.InvariantCulture));

        public void Add(string name, Guid? value) => Add(name, value?.ToString("D", CultureInfo.InvariantCulture));

        public void Add(string name, int value) => Add(name, value.ToString(CultureInfo.InvariantCulture));

        public void Add(string name, long value) => Add(name, value.ToString(CultureInfo.InvariantCulture));

        public string Hash() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))).ToLowerInvariant();
    }
}
