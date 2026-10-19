using System.Security.Cryptography;
using System.Text;

namespace DevalCopilot.Application.Features.Runs.Policies.PlanningSequences;

/// <summary>The lowercase SHA-256 binding of a run's recorded objective text to its planning sequence (ADR-0055).</summary>
internal static class PlanningSequenceObjective
{
    public static string Hash(string objective) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(objective)));
}
