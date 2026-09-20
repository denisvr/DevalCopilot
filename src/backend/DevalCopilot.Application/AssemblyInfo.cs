using System.Runtime.CompilerServices;

// Allows direct unit tests of internal Application-owned policy/helper types (e.g.
// AgentAuthoredMessageEligibility) without widening their public surface.
[assembly: InternalsVisibleTo("DevalCopilot.Application.Tests")]
