# ADR-0074: Disable SDK first-run persistent side effects in the Windows verification profile

Status: Accepted

## Context

[ADR-0073](0073-add-an-owned-nuget-base-to-the-windows-verification-profile.md)
extended the fixed Windows verification environment to `windows-verification-v2`
so real .NET SDK restore, build and test could run. Every verification
execution receives a fresh owned `DOTNET_CLI_HOME`, so each real SDK
execution is that SDK's first run, and the SDK's first-run experience performs
two actions that outlive the owned scratch:

- **Global tools on the persistent user `PATH`.** The SDK appends
  `%DOTNET_CLI_HOME%\.dotnet\tools` to the persistent user `PATH`
  (`DOTNET_ADD_GLOBAL_TOOLS_TO_PATH`, documented default `true`). During
  WIN-EXEC-PROFILE-01 (finding WEP-F6) the registry user `PATH` was found to hold
  dozens of entries for released scratch directories, one per real SDK
  execution, including the executor's own. A paired experiment on a trivial
  offline project showed the profile keys alone grew the user `PATH`, and
  adding `DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=false` left it unchanged. The growth
  is unbounded and eventually stops `cmd.exe` from resolving `dotnet`, which
  broke the shared NSwag build step on the host.
- **ASP.NET development certificate.** The SDK's first-run step generates an
  ASP.NET Core HTTPS development certificate in the current user's certificate
  store (`DOTNET_GENERATE_ASPNET_CERTIFICATE`, documented default `true`). It is
  gated on the first-run check and a sentinel under the CLI home, so a fresh
  owned CLI home repeats it every execution. A verification execution has no
  need for that certificate, and a certificate in a user store is a persistent
  change to the user's account outside the owned scratch. Source support: the
  original SDK implementation (`dotnet/cli` commit `ad8f3da`,
  `DotnetFirstTimeUseConfigurer.ShouldGenerateAspNetCertificate`) reads the
  variable as a boolean defaulting to `true` and gates generation on the
  first-run check and a sentinel file; Microsoft's .NET environment-variable
  reference documents the default and that `0`, `false` or `no` disables it. That
  is supporting evidence for the mechanism, not proof of every installed
  build. No certificate store was enumerated, no certificate generated and no
  trust changed to obtain it.

Both effects are written by the SDK's own process, not by the profile or the
supervisor, so cleanup of the owned scratch cannot undo them. The planner
accepted the prerequisite and authorized this bounded contract change.

## Decision

New verification executions record `windows-verification-v3`. Version 3 is
version 2 plus exactly two keys for exactly one executable kind:

- When the executable is `dotnet.exe`, `DOTNET_ADD_GLOBAL_TOOLS_TO_PATH` is the
  literal `false`.
- When the executable is `dotnet.exe`, `DOTNET_GENERATE_ASPNET_CERTIFICATE` is
  the literal `false`.
- Both are constants of the profile. Neither reflects an ambient value, and no
  other executable kind (`node.exe`, npm or any other tool) receives them.
- Every other key and value, including the trailing directory separator of the
  owned `NUGET_PACKAGES` (WEP-F4, representational only), executable
  validation, owned scratch, preparation-failure handling, capture budget,
  scratch lifetime and authority of ADR-0072 and ADR-0073 is unchanged.

This disables two named SDK side effects. It is not filesystem, registry,
certificate-store or network isolation, and it does not claim that the SDK or
any tool performs no other persistent write; a recipe that itself writes the
user environment or a store is outside this decision.

Executions that recorded `windows-verification-v1`, `windows-verification-v2`
or no profile keep exactly what they recorded: status, outcome, completion time
and profile label. Nothing relabels, backfills, upgrades, replays or redispatches
them, there is no migration, and no new execution records version 1 or 2.
There is still no selectable profile: the provider always prepares the current
version. ADR-0072 and ADR-0073 stand as recorded history and are extended, not
redefined. ADR-0050 is unaffected and remains Proposed.

## Verification and testing boundary

The decision is protected without running a deliberately unsafe SDK child:

- Provider tests pin the exact key set and literal values per executable kind
  and the absence of both keys for every other kind.
- Tests that run the real SDK pass every request through a pre-launch guard that
  inspects the exact environment the adapter is about to give a `dotnet.exe`
  child and refuses, without starting a process, when either flag is not the
  literal `false`. Mutating the provider to omit a flag or change its value
  therefore fails before any child launches. That is a pre-launch control, not a
  physical negative SDK experiment, which is deliberately never repeated on a
  real host.
- Positive evidence is a guarded real execution: the same recipes pass through
  the production supervisor under version 3 with complete I/O, source binding
  and proved scratch release, and a read-only identity of the persistent user
  `PATH` (length, entry count and hash; never the value) is unchanged across
  repeated fresh executions.
- The outer runner that invokes the SDK to build and test the repository also
  sets both flags to `false` and uses a short process-only `PATH`. Nothing
  repairs or restores a user's `PATH` or certificate store automatically.

## Consequences

- Real SDK verification no longer adds a per-execution entry to the persistent
  user `PATH` or a development certificate to the user's certificate store.
- Entries or certificates already written by earlier version-2 executions are
  not removed by this decision; removing them is the user's own action.
- Recorded history shows three labels (`windows-verification-v1`,
  `windows-verification-v2` and `windows-verification-v3`) plus unrecorded rows;
  each means exactly the key set it was recorded under.
- The real toolchain proof (acceptance F) still needs an identified outbound
  isolation boundary; this decision provides none.
