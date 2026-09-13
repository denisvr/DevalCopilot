using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.EnvironmentReadiness.Commands.EnsureHostCapabilityCatalogSeeded;

/// <summary>
/// Idempotent startup step: inserts a <c>NeverProbed</c> snapshot for any fixed catalog
/// capability that does not yet have one. Never re-seeds or resets an existing row, and never
/// runs per project — the catalog is host-scoped and this seeds it exactly once regardless of
/// how many projects are registered.
/// </summary>
public sealed record EnsureHostCapabilityCatalogSeededCommand : ICommand<Result<int>>;
