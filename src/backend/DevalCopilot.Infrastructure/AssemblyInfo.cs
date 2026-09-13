using System.Runtime.CompilerServices;

// Lets the integration test project deterministically exercise a handful of small internal
// helpers (process-tree termination, output capture) directly against real processes, rather
// than only indirectly through the public adapter surface.
[assembly: InternalsVisibleTo("DevalCopilot.Infrastructure.IntegrationTests")]
