namespace DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetCodexAccountAllowance;

/// <summary>
/// The explicit observation-state discriminator for a Codex account-allowance snapshot.
/// <see cref="Unknown"/> covers every unavailable case uniformly (no vetted launch target,
/// missing account authentication, an unsupported protocol method, a malformed or absent
/// response, a timeout, or a process failure) — never a guessed or zero-valued allowance.
/// </summary>
public enum CodexAccountAllowanceStatus
{
    Unknown = 0,
    Observed = 1,
}
