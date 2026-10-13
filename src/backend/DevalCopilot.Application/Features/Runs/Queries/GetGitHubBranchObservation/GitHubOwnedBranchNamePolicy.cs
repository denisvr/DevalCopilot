namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubBranchObservation;

/// <summary>
/// The admission rule for the one branch form this observation supports: the deterministic, tool-owned workspace branch
/// <c>devalcopilot/workspace/&lt;32 lowercase hexadecimal project digits&gt;/&lt;workspace number&gt;</c> (ADR-0008), with a decimal
/// workspace number without sign or leading zero that fits a 32-bit integer. Any other branch is never sent to a process. Pure.
/// </summary>
public static class GitHubOwnedBranchNamePolicy
{
    private const string Prefix = "devalcopilot/workspace/";
    private const int ProjectDigits = 32;
    private const int MaximumNumberDigits = 10;

    public static bool IsOwnedBranch(string? branchName)
    {
        if (branchName is null || !branchName.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = branchName.AsSpan(Prefix.Length);
        if (rest.Length < ProjectDigits + 2 || rest[ProjectDigits] != '/')
        {
            return false;
        }

        foreach (var character in rest[..ProjectDigits])
        {
            if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
            {
                return false;
            }
        }

        var number = rest[(ProjectDigits + 1)..];

        return number.Length <= MaximumNumberDigits
            && number[0] is >= '1' and <= '9'
            && number.IndexOfAnyExceptInRange('0', '9') < 0
            && long.Parse(number, System.Globalization.CultureInfo.InvariantCulture) <= int.MaxValue;
    }
}
