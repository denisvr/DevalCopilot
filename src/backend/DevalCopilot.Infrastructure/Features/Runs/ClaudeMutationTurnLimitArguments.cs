using System.Globalization;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Infrastructure.Features.Runs;

/// <summary>
/// The one place the two mutating Claude adapters translate an attempt's immutable agentic-turn-limit
/// request into the documented <c>--max-turns</c> CLI argument, so they cannot drift apart. A null
/// request appends nothing, preserving the exact argument list of a version 1 attempt and of a version 2
/// attempt that requested no limit. A non-null request must be inside
/// <see cref="ClaudeMutationTurnLimit"/> and paired with the exact version 2 mutation contract of the
/// path (<paramref name="expectedContractVersion"/>); anything else (an out-of-range value, a version 1
/// or unknown contract) fails the invocation closed before any process starts, so a limit is never
/// silently dropped and the invocation never retried unrestricted. The number is one discrete,
/// invariant-culture argument, never passed through a shell. It is a request for a provider-loop
/// guardrail only: reaching it is reported by the provider as an ordinary failed invocation.
/// </summary>
internal static class ClaudeMutationTurnLimitArguments
{
    public static bool TryAppend(
        List<string> arguments, int? requestedMaxTurns, string? adapterContractVersion, string expectedContractVersion)
    {
        if (requestedMaxTurns is not { } maxTurns)
        {
            return true;
        }

        if (!ClaudeMutationTurnLimit.IsValid(maxTurns)
            || !string.Equals(adapterContractVersion, expectedContractVersion, StringComparison.Ordinal)
            || !ClaudeMutationAdapterContract.CarriesTurnLimit(adapterContractVersion))
        {
            return false;
        }

        arguments.Add("--max-turns");
        arguments.Add(maxTurns.ToString(CultureInfo.InvariantCulture));
        return true;
    }
}
