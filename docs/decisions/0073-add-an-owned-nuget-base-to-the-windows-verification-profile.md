# ADR-0073: Add an owned NuGet base to the Windows verification profile

Status: Accepted (current profile version extended by
[ADR-0074](0074-disable-sdk-first-run-persistent-side-effects-in-the-windows-verification-profile.md):
new executions record `windows-verification-v3`; the version-2 decision below
stays as recorded)

## Context

[ADR-0072](0072-adopt-one-windows-execution-profile.md) adopted one fixed Windows
verification environment, `windows-verification-v1`. During WIN-EXEC-PROFILE-01
(review challenge WEP-R4), every .NET SDK restore under that key set failed inside
the SDK with `NuGet.targets(782,5): error : Value cannot be null. (Parameter
'path1')`: NuGet derives the base of its Windows machine-wide settings path from
`ProgramFiles(x86)`, falling back to `ProgramFiles`, and the profile supplies
neither. NuGet's public source supports this explanation
(`NuGet.Common/PathUtil/NuGetEnvironment.cs`); it is not itself proof of every
installed binary's behavior. Pointing either key at the machine's real Program
Files tree would make NuGet read machine configuration, which ADR-0072 forbids.
The planner accepted the prerequisite and authorized this narrow extension.

## Decision

New verification executions record `windows-verification-v2`. Version 2 is
version 1 plus exactly one key for exactly one executable kind:

- When the executable is `dotnet.exe`, `ProgramFiles(x86)` points to a new,
  initially empty directory created beneath that execution's own owned scratch
  (`program-files-x86`).
- Neither ambient `ProgramFiles(x86)` nor `ProgramFiles` is read or inherited;
  no `ProgramFiles` fallback is set; nothing is copied into the directory; no
  other executable kind receives the key.
- Every other key, executable validation, preparation-failure handling, capture
  budget, scratch lifetime and authority of ADR-0072 is unchanged.

This scopes NuGet's machine-settings lookup to an empty owned directory. It is
configuration scoping, not filesystem or network isolation: it does not claim
that the SDK or NuGet performs no other machine read.

Executions that recorded `windows-verification-v1`, or no profile, keep exactly
what they recorded. Nothing relabels, backfills, upgrades, replays or
redispatches them, and no new execution records version 1. There is no
selectable profile: the provider always prepares the current version.
ADR-0072's version-1 decision stands as recorded history and is extended, not
redefined, by this decision. ADR-0050 is unaffected and remains Proposed.

## Consequences

- .NET SDK restore, build and test can run under the fixed profile with the
  workspace's own NuGet configuration and the execution's owned caches; a recipe
  still restores and builds in one execution, and SDK recipes should disable
  build servers so no persistent node inherits the captured pipes.
- The NuGet configuration an execution sees is the workspace's own and files
  inside its owned scratch; an empty machine-settings base contributes nothing.
- The real toolchain proof (acceptance F) still needs an identified outbound
  isolation boundary; this decision provides none.
