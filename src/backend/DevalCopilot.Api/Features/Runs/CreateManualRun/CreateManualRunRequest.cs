using System.Text.Json.Serialization;

namespace DevalCopilot.Api.Features.Runs.CreateManualRun;

/// <summary>An omitted or null budget member independently selects the existing default (16 claims, 120 minutes); an
/// explicit whole JSON number outside 1..16 claims or 1..120 minutes is refused, never clamped. The members are read with
/// strict number handling, so a quoted number is refused instead of being converted.</summary>
public sealed record CreateManualRunRequest(
    Guid ProjectId,
    string Objective,
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] int? MaximumAgentAttempts = null,
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] int? MaximumAgentInvocationMinutes = null);
