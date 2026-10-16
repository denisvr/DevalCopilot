# GitHub publication P9: native exit and pipe-read semantics (P9-SEM-01)

> **Status: documentation-to-source evidence for Codex review.** Not an ADR, not a contract and not P9 proof.
> [ADR-0050](../decisions/0050-define-the-first-approved-github-branch-publication-execution-contract.md) remains Proposed
> and P9 unproven. Frozen patches A, B and C and both suggested repair approaches stay unapplied and unaccepted. Nothing
> was built, launched, probed or injected; no profile, ACL, label, grant, token, credential, helper or provider operation
> occurred. Documentation is not executable evidence: it validates neither the canonical run's child exit measurements nor
> its capture completeness.

- Baseline: `main`, `HEAD` = local and live `origin/main` =
  `465e60cfc31d02961fc3970d44e65bb4cfe5ee29`. Evidence date: 2026-10-16.
- Subject: static-audit rows H37 (`drain`) and H40 (`GetExitCodeProcess`) and the matching part of unknown U2, for the
  frozen host `p9ac-host-v2.c` of the canonical run reported in the
  [feasibility note](github-publication-source-write-denial-feasibility.md). H38 and H41 appear only as context.
- Delivery checkpoint: [current work](../roadmap/current-work.md).

## 1. Conclusion

**H40.** A nonzero `GetExitCodeProcess` return establishes a termination status or `STILL_ACTIVE`; a zero return
establishes only failure. The documentation says nothing about the output variable after failure, so the static audit's
"an API failure leaves `ec = 0`" is an inference. The host discards the return and never calls `GetLastError`, so no
retained record can tell a measured `0` from a failed query. The documentation narrows the causes: the `CreateProcessW`
handle has all access, which includes both the query and the synchronization rights, and on every path where the exit
code can satisfy `runOk` the process had already been observed signalled. It lists no failure cause for a valid,
all-access, signalled handle, and it does not exclude one. H40 therefore stays an unchecked contract with no documented
trigger. It is neither a diagnosed failure nor a validated measurement.

**H37.** The documentation separates successful polling with nothing available, `PeekNamedPipe` failure, `ReadFile`
failure (including `ERROR_BROKEN_PIPE` once every write handle is closed), a successful zero-byte read and a partial read.
`drain` treats all except the partial read as "stop this pass" and records none of them. The source never observes end
of stream. The pages read do not say whether bytes buffered before the writers closed are still reported by
`PeekNamedPipe` and returned by `ReadFile` afterwards. That is exactly the case of the single final drain after the child
terminates. Capture completeness therefore remains open. The consequences differ by stream. **Stdout, conditionally:**
if loss is only an unread suffix, unread bytes fail the eight-line check unless they all lie after the eighth complete
line. That assumption fails if a failed `ReadFile` can remove bytes and a later pass resumes (K4, undocumented): a gap
inside one line could then leave the eight-line check satisfied, so general stdout-loss detection is not established.
**Stderr, under any loss mode:** emptiness reads as a pass only if **every** stderr byte went unread, which narrows the
audit's "a lost stderr byte would read as none".

**U2.** Narrowed only for the `GetExitCodeProcess` causes and the post-exit pipe reads, as listed in section 5. Its other
parts stay open.

## 2. Sources and labels

Labels: **DOC** = stated by a Microsoft page below; **CODE** = frozen host source line (`h:`); **INF** = inference from
DOC and CODE, not stated by either; **UNK** = not settled by the documentation read or the source. Prior row text comes
from the retained static-audit reports and is quoted as their claim, not re-established.

All pages were read on 2026-10-16, about 11:09 to 11:11 UTC, from `learn.microsoft.com` (en-us), with each page's
displayed "Last updated" date:

| Ref | Page and sections used | Last updated |
|---|---|---|
| D1 | [GetExitCodeProcess](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getexitcodeprocess): Parameters, Return value, Remarks (incl. Important) | 2022-11-01 |
| D2 | [PeekNamedPipe](https://learn.microsoft.com/en-us/windows/win32/api/namedpipeapi/nf-namedpipeapi-peeknamedpipe): Parameters, Return value, Remarks | 2023-02-02 |
| D3 | [ReadFile](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-readfile): `lpNumberOfBytesRead`, Return value, Remarks, Synchronization and File Position, Pipes | 2025-07-22 |
| D4 | [Anonymous Pipe Operations](https://learn.microsoft.com/en-us/windows/win32/ipc/anonymous-pipe-operations): whole page | 2021-01-07 |
| D5 | [CreatePipe](https://learn.microsoft.com/en-us/windows/win32/api/namedpipeapi/nf-namedpipeapi-createpipe): `nSize`, Remarks | 2021-10-13 |
| D6 | [Process Security and Access Rights](https://learn.microsoft.com/en-us/windows/win32/procthread/process-security-and-access-rights): both rights tables, CreateProcess handle paragraph, Protected Processes | 2025-07-14 |
| D7 | [WaitForSingleObject](https://learn.microsoft.com/en-us/windows/win32/api/synchapi/nf-synchapi-waitforsingleobject): Parameters, Return value, Remarks | 2024-08-22 |
| D8 | [GetLastError](https://learn.microsoft.com/en-us/windows/win32/api/errhandlingapi/nf-errhandlingapi-getlasterror): Return value, Remarks | 2024-02-06 |
| D9 | [CreateProcessW](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessw): `lpProcessInformation`, Return value | 2023-02-09 |
| D10 | [PROCESS_INFORMATION](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/ns-processthreadsapi-process_information): `hProcess`, Remarks | 2024-02-22 |
| D11 | [Terminating a Process](https://learn.microsoft.com/en-us/windows/win32/procthread/terminating-a-process): results list, exit-code and signalled-state paragraphs | 2025-07-14 |

## 3. H40: what the exit query establishes

Source (CODE): `h:929` `DWORD ec = 0; GetExitCodeProcess(pi.hProcess, &ec); R.exitCode = ec;`. The return is discarded
and `GetLastError` is not called. `h:932-935` prints `ec`; `h:1087` gates on `R.exitCode == 0`; `h:1098` uses
`R.exitCode != 0` only to choose a failure classification. `pi.hProcess` comes from `CreateProcessW` at `h:865` and is
closed only at `h:942`, after `h:931`.

1. **Return and output (DOC D1).** Nonzero means success, zero means failure, with the cause from `GetLastError`. The
   status values are described only for success: `STILL_ACTIVE` (259) if the process has not terminated; otherwise the
   `ExitProcess`/`TerminateProcess` value, the `main`/`WinMain` return or the unhandled-exception value. D11 adds that
   the status is `STILL_ACTIVE` while the process executes and changes to the exit code at termination.
2. **Output after failure (UNK).** D1 does not say whether `lpExitCode` is left unchanged, written or indeterminate on
   failure. Contrast D5, where `CreatePipe` explicitly calls its outputs indeterminate after failure. Retention of the
   initial `0` is therefore an assumption, not documented behaviour.
3. **Exit status, `STILL_ACTIVE` and failure (DOC D1, CODE).** Only the return separates a failed query from a measured
   status, and the host discards it. `STILL_ACTIVE` is a status of a successful query, and D1 warns that a process
   exiting with 259 is indistinguishable from a running one. The host never names 259 (`nt_name` prints `unrecognized`,
   `h:772-786`), but any nonzero value fails `runOk`.
4. **Query versus wait rights (DOC D6, D7, D1).** The exit query needs `PROCESS_QUERY_INFORMATION` or
   `PROCESS_QUERY_LIMITED_INFORMATION`; a wait needs `SYNCHRONIZE`. These are distinct rights, so a successful wait alone
   does not show that the query could succeed. For this handle, D6 states that the handle returned by `CreateProcess`
   has `PROCESS_ALL_ACCESS`, which includes `SYNCHRONIZE` and the low 16 specific bits containing both query rights. The
   protected-process restrictions in D6 are not expected to apply: the flags at `h:862` and the attributes at
   `h:841-845` request no protected process (INF; how a process becomes protected was not read).
5. **Ordering on the gating path (CODE, INF).** `runOk` needs `!R.timedOut && !R.capFailed`, and
   `h:916` sets `timedOut` unless `wres == WAIT_OBJECT_0` or a cap failure occurred. So whenever `R.exitCode` can satisfy
   `runOk`, `WaitForSingleObject(pi.hProcess, 100)` at `h:912` had already returned `WAIT_OBJECT_0` (DOC D7: signalled)
   before `h:929`, and `runOk` also requires `h:931` to find it signalled. D11 lists "exit code is set" and "process object is
   signalled" among the results of termination without stating their order. That a successful query after an observed
   signal returns the final status rather than 259 is an inference.
6. **What a successful wait proves (INF).** On the same handle value, it shows the handle was valid and synchronizable
   at that instant and that the process had terminated. It is not evidence that the separate query call succeeded.
7. **Remaining causes (UNK).** D1 lists no failure cause. D8 says error codes are not part of the API specification
   and cannot be listed completely. For a valid, all-access, signalled, unclosed handle in a host with no thread
   creation (CODE: no `CreateThread` or `_beginthread` token in the host), the documentation neither names nor excludes
   a failure. The audit's "rights-only cause" is not supported for this handle (DOC D6); its "same cause (invalid
   handle)" reasoning is an inference about causes the documentation does not enumerate.

## 4. H37: what `drain` retains and discards

Source (CODE), `h:754-762`: while `PeekNamedPipe(rd, NULL, 0, NULL, &avail, NULL)` succeeds with `avail > 0`, call
`ReadFile` for up to `min(avail, 4096)` bytes; stop the pass on read failure or `n == 0`; append, truncating at
`OUTPUT_CAP` with `truncated = 1`. No error is captured and the function returns nothing. Callers: `h:910` once per loop
iteration **before** each 100 ms wait, and `h:924` once after the loop or a `goto tail`. The loop does not drain again
after the wait that observes termination. The handles are synchronous (DOC D4: anonymous pipes do not support
overlapped I/O); the host creates no threads, so D2's "returns immediately" remark applies (INF).

| Outcome | Documentation | `drain` behaviour (CODE) | Bytes |
|---|---|---|---|
| Peek succeeds, `avail == 0` | DOC D2: returns immediately with no data; not an end-of-stream signal | ends the pass | none read; any later bytes wait for the next call |
| Peek fails | DOC D2: zero return, cause from `GetLastError`; **no listed causes, no closed-writer statement** (UNK) | ends the pass silently; `avail` not consulted | unread bytes stay unread for this pass; whether a later call can read them is UNK |
| Read succeeds, `0 < n < requested` | DOC D3/D4/D5: read may return after a write completes on the write end | appends and re-peeks | none lost |
| Read succeeds, `n == 0` | DOC D3: on a pipe, the writer called `WriteFile` with zero bytes | ends the pass although Peek reported `avail > 0` | remaining bytes wait for the next call |
| Read fails | DOC D3: `n` is set to zero before any work; `ERROR_BROKEN_PIPE` once the write handle is closed; `ERROR_MORE_DATA` concerns message mode, and D2 gives anonymous pipes no message remainder | ends the pass silently | unread bytes as above; whether a failed read can consume bytes is UNK |
| Cap overrun | CODE only | `truncated = 1`; excess discarded; `capFailed` fails `runOk` (`h:911`, `h:925`, `h:1087`) | discarded, flagged |

**Repeated and final drains (CODE, INF).** In-loop drains read bytes written while the child runs. Bytes written after the
last in-loop drain, for example a final line written just before exit, are left to the one pass at `h:924`. By then the child has
terminated and its handles are closed (DOC D4, D11), and the host closed its write-end copies at `h:869` (unchecked, H33).
The pipe still exists because the host holds the read handle (DOC D4). Whether `PeekNamedPipe` then reports the
buffered bytes, or fails on the closed writers before they are read, is not stated in D2, D3 or D4 (**UNK**). D3's
broken-pipe sentence does not say whether buffered data is returned first. Termination checks (H41: job query, zero
active processes, a signalled process at `h:931`) prove that the process ended (INF from D7, D11). They do not show
that the final pass read until end of stream, because `drain` never distinguishes "empty and closed" from "Peek
failed".

**Implications (INF).** Bytes are read sequentially from a FIFO byte pipe. **Suffix-only assumption:** apart from the
flagged cap, loss is an unread suffix of a stream. It holds only if a failed `ReadFile` removes no bytes. Whether it
can is undocumented (K4); because a later pass resumes after a failed one, such a removal would be a gap inside the
stream, not a suffix.

- **Stdout** (`child_complete`, `h:979-983`; `parse_child`, `h:953-976`), **under the suffix-only assumption:** an unread
  suffix that removes or cuts any of the eight required lines yields fewer lines or a missing final newline, so the check
  fails closed. An unread suffix lying entirely after the eighth complete line is invisible, so an unexpected extra line
  could go undetected. **Where K4 applies:** a gap that removes a newline still reduces the line count and fails closed,
  but a gap inside one line can keep eight lines, their framing and the step and verdict substrings, so it is not
  necessarily detected. The eight-line check is therefore not a general stdout-loss detector.
- **Stderr** (`R.err.n == 0`, `h:1087`), **under any loss mode, suffix or gap:** the predicate measures bytes the host
  read, not bytes the child wrote. A partial loss leaves `n > 0` and fails closed. Only the loss of every stderr byte
  reads as "empty".

## 5. Claim, source and limitation matrix

| ID | Prior claim (retained report) | Source | Documentation | Assessment | Limitation |
|---|---|---|---|---|---|
| H40-a | Return ignored; `ec` initialised to 0 | CODE h:929 | D1 | **Confirmed** (CODE) | none |
| H40-b | "an API failure leaves `ec = 0`" | CODE h:929 | D1 silent; D5 contrast | **Qualified**: INF/UNK, not documented | the value after failure may be 0 or another value; consequence unchanged: the gate cannot tell a failure from a measured 0 |
| H40-c | A live process gives 259 and fails | CODE h:1087 | D1, D11 | **Confirmed for a successful query** (DOC); a 259 exit is indistinguishable (D1) | gating path already observed termination (CODE h:912-916) |
| H40-d | Invalid-handle cause also fails the `h:931` wait (I) | CODE h:931, h:942 | D7 | **Qualified**: INF; causes are not enumerated (D8) | handle validity at both waits is shown only for those instants |
| H40-e | A rights-only failure would not be caught | CODE h:865 | D6 | **Narrowed**: `CreateProcess` handle has all access (DOC); no rights-only cause documented for it | assumes D6's unqualified statement holds for this launch configuration |
| H40-f | "no cause was identified"; U-gate (low likelihood) | CODE | D1, D8 | **Agreed** as a static contract gap; likelihood is not derivable from documentation | not diagnosed; canonical exit value neither validated nor refuted |
| H37-a | Any failure or zero read ends the loop silently | CODE h:756-758 | D2, D3 | **Confirmed** (CODE); zero read has a documented distinct meaning (D3) | none |
| H37-b | Read failure indistinguishable from "no more data" | CODE h:756-758, h:910, h:924 | D2, D3 | **Confirmed** (CODE); the source never observes end of stream | none |
| H37-c | Stdout loss caught indirectly by the eight-line check (I) | CODE h:953-983 | D3, D4 | **Qualified** (conditional INF): only under the suffix-only assumption does it fail closed for loss touching the eight lines; never for loss wholly after them | the assumption is undocumented (K4); with consumption on failure, a gap inside one line need not be detected |
| H37-d | Stderr emptiness only as strong as `drain`; "a lost stderr byte would read as none" (U-gate) | CODE h:1087 | D2, D3 | **Qualified**: partial loss fails closed under any loss mode; only total loss reads as empty. U-gate direction **agreed** | not diagnosed for the canonical run |
| H37-e | (new) Post-termination final drain completeness | CODE h:913, h:924 | D2, D3, D4 silent | **UNK**: buffered-byte availability after the writers close is undocumented | open; see section 6 for what could narrow it |
| U2-exit | Causes of a `GetExitCodeProcess` failure | CODE h:865, h:929 | D1, D6, D8 | **Narrowed**: rights excluded per DOC; other causes undocumented | output-after-failure UNK |
| U2-pipe | `PeekNamedPipe` and `ReadFile` after the child exits | CODE h:924 | D2, D3, D4 | **Narrowed**: `ERROR_BROKEN_PIPE` and zero-byte reads documented; Peek after close and residual bytes UNK | open |
| U2-rest | `CloseHandle` buffered-data loss; handle list versus `SetHandleInformation`; CRT invalid-parameter action | n/a | not read | **Open, unchanged** | out of this slice |

Prior text is preserved unchanged in the retained reports; this table qualifies it and does not replace it.

## 6. Unknowns and consequences

- **K1** (H40-b): content of `lpExitCode` after failure. It decides whether a failed query would read as success (0) or
  fail closed. No documentation read settles it.
- **K2** (H40-f): whether any failure cause exists for a valid, all-access, signalled handle. Documentation neither
  names nor excludes one, so a documentation argument cannot turn the recorded exit value into a checked measurement.
- **K3** (H37-e): whether `PeekNamedPipe` succeeds and reports buffered bytes once every write handle is closed, and
  whether `ReadFile` returns them before `ERROR_BROKEN_PIPE`. It decides whether the final drain can lose a tail.
- **K4** (H37): whether a failed `ReadFile` on a pipe can remove bytes. D3 sets `n` to zero but says nothing about the
  pipe contents. It conditions the suffix-only assumption and therefore the stdout eight-line argument (H37-c), not the
  stderr conclusion (H37-d).
- **K5** (H37-d): both canonical stderr captures are 0 bytes, per the retained
  [capture cross-check](github-publication-retained-capture-crosscheck.md). Documentation cannot tell whether a child
  wrote nothing or wrote bytes that were never read.

These unknowns are of three kinds; none of the following is selected, proposed as a next slice or authorized here:

- **API questions (K1, K3, K4; K2 only in part).** Further primary documentation, or a separately authorized
  investigation, might narrow them. K2 asks for the absence of any cause, which documentation can name but not
  exhaustively rule out (D8).
- **Future measurement controls (K1 to K4).** A checked query return, recorded error codes or an observed end of stream
  could make **future** measurements interpretable. They would say nothing about the canonical run's measurements.
- **Historical uncertainty (K5, and K1 to K4 as applied to the canonical run).** No new experiment, documentation or
  source change can establish that the canonical children wrote no stderr, that their original captures lost none, or
  that the recorded exit values were successful queries. That uncertainty is permanent for the retained evidence.

No probe was substituted for a missing page.

## 7. Provenance

- **Retained, hash-verified before and after use (SHA-256):** `logs/executor/p9-appcontainer-gate/src/p9ac-host-v2.c`
  (`5492ab0d...2ca9`); `manifest-20261015.sha256` (`b1f8f8e6...afc4`); static audit
  `call-to-gate-trace-20261015.md` (`ca43f753...c340`), `unknowns-and-challenges-20261015.md` (`61f00c38...082e`) and
  `manifest-static-audit-20261015.sha256` (`ea1d32b8...dfc1`); `correction1/trace-addendum-20261015.md`
  (`3d053868...f90a`), `correction1/correction-report-20261015.md` (`be034cad...45d3`) and
  `correction1/manifest-static-audit-correction1-20261015.sha256` (`7317cf5c...72b5`). The five source and report
  entries match their manifests. Manifests were treated as data: the membership and hash checks covered only these
  five entries and used no other listed member. That bound applies to those checks only; the boundary deviation below
  read three files that Codex review 1 identifies as other listed members.
- **Newly read:** D1 to D11, through the desktop app's built-in browser (page text only, no saved HTML). The read record
  is `logs/executor/p9-native-io-semantics/documentation-read-record-20261016.md` (ignored).
- **Boundary deviation (disclosed, not authorized retrospectively):** one `wc -lc` glob over the two static-audit
  directories also counted three non-allowlisted reports, members of the retained audit manifests according to Codex
  review 1 (not rechecked here, to avoid another manifest read):
  `audit-report-20261015.md`, `proposed-fault-cases-20261015.md` and `correction1/fault-cases-addendum-20261015.md`.
  To compute line and byte counts, `wc` read the full contents of those files. Only the counts were displayed; the
  contents were not viewed and not used in this analysis. The files were not reopened, re-counted or hashed afterwards.
  The first membership check failed because the shell's `grep` aborted; that record is preserved and the `awk` retry
  matched.
- **Not run:** build, harness, child or host launch, fault injection, API probe, retained capture, fixture, receipt or
  binary read, profile, ACL, label, grant or token query, qualification.
