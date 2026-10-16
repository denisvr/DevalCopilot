# GitHub publication P9: retained-capture consistency cross-check (P9-EV-01)

> **Status: read-only evidence for Codex review.** Not an ADR, not a contract and not P9 proof.
> [ADR-0050](../decisions/0050-define-the-first-approved-github-branch-publication-execution-contract.md) remains Proposed
> and P9 unproven. Frozen patches A, B and C stay unapplied and unaccepted. No producer was rerun; nothing was built or
> launched; no profile, ACL, label, grant, token, credential, helper or provider operation occurred; no retained file was
> repaired, cleaned or rewritten.

- Baseline: `main`, `HEAD` = local and live `origin/main` =
  `4c9102c523e2d8bd345f390332a6af470afb9cec`. Evidence date: 2026-10-15.
- Subject: the one canonical AppContainer fixture-gate run, `run-20261015T210720Z-16872`, reported in sections 14.2 to
  14.8 of the [feasibility note](github-publication-source-write-denial-feasibility.md). The static audit left capture
  inspection open (its unknowns U4 and U10 and challenge C2); this note answers that question for the recorded bytes only.
- Delivery checkpoint: [current work](../roadmap/current-work.md).

## 1. Method

A dependency-free Node reader, `crosscheck.mjs` (SHA-256 `e8185c1a...e403`, Node v24.12.0), under ignored
`logs/executor/p9-appcontainer-evidence-crosscheck/`, opens only a fixed list of inputs, each resolved beneath its root and
refused if it leaves that root, crosses a link or is not a regular file. Manifest entries and recorded text are parsed as
data and never used to open anything. It creates outputs exclusively under unique names.

Classes used below: **Recorded** is a value as a producer wrote it. **Compared** is an independent agreement across files,
streams or the frozen source format, computed by the reader. **Unknown** cannot be decided from the retained bytes.

Each input was verified against its manifest before interpretation and re-hashed after the pass; a separate `sha256sum`
record before and after the work is byte-identical (27 entries, including the planner checkpoint). Only this declared
subset was verified: the other original manifest entries, the `ctl` and `app` fixture trees and all live state were not
opened.

## 2. Input identities

Gate root `logs/executor/p9-appcontainer-gate/`; `out/` is `runs/run-20261015T210720Z-16872/out/`. Full digests are in the
verify report.

| Manifest | SHA-256 | Entries | Status |
|---|---|---|---|
| Original `manifest-20261015.sha256` | `b1f8f8e6...afc4` | 51 | matches; 15 selected entries match |
| Prior correction `correction1/manifest-correction1.sha256` | `1a2141b0...6954` | 4 | matches; 4 entries match |
| Audit `manifest-static-audit-20261015.sha256` | `ea1d32b8...dfc1` | 9 | matches the abbreviated record; 1 selected entry matches |
| Audit correction `correction1/manifest-static-audit-correction1-20261015.sha256` | `7317cf5c...72b5` | 5 | matches; 2 selected entries match |

| Input | Bytes | SHA-256 |
|---|---|---|
| `out/host-results.jsonl` (annotated transcript) | 114907 | `e87e479a...5adf` |
| `out/child-ctl.stdout.jsonl`, `out/child-app.stdout.jsonl` | 1220, 1261 | `04edce13...65ee`, `f5d65762...697e` |
| `out/child-ctl.stderr.txt`, `out/child-app.stderr.txt` | 0, 0 | `e3b0c442...b855` |
| `out/planned-moniker.json`, `out/profile-receipt.json` | 75, 362 | `651e02b0...58c4`, `ae0b57e4...04be` |
| `cmd-run1-20261015T210720Z.stdout.txt`, `.stderr.txt` | 112425, 0 | `8137df63...1faa`, `e3b0c442...b855` |
| `cmd-run1-20261015T210720Z.exit.txt`, `.command.txt` | 77, 294 | `b0d407b0...be25`, `d0f4857d...9f32` |
| `run-p9ac.ps1`, `metadata-20261015.txt` | 1264, 4220 | `afe1ae6a...94bf`, `85f91dc7...c790` |
| `src/p9ac-host-v2.c`, `src/p9ac-child-v2.c` (formats only) | 82253, 9191 | `5492ab0d...2ca9`, `865dd7de...e40b` |
| `correction1/profile-summary-derived.json` (derived) | 4650 | `416f05ed...9738` |
| `correction1/derive-profile-summary.js`, `validate-correction1.js`, `validation-correction1.txt` | 6591, 5641, 1657 | `77a75393...7486`, `5a1c1765...38a9`, `ea7bd3b2...39ba` |
| Audit `unknowns-and-challenges-20261015.md`, `correction1/correction-report-20261015.md`, `correction1/trace-addendum-20261015.md` | 9184, 8045, 14261 | `61f00c38...082e`, `be034cad...45d3`, `3d053868...9f90` |

## 3. Comparison results

Locators are transcript line and host `seq` (`L146/s146`) unless a file is named. Every row met its expectation; the
original receipt row is the expected, named parse failure. Nothing was waived.

| Check | Class | Locator | Result |
|---|---|---|---|
| Raw child stream lengths against host `RESULT` | Compared | ctl `L146/s146`, app `L259/s247`; both capture files | `stdout_bytes` 1220 and 1261 equal the file lengths and the embedded segment lengths; `stderr_bytes` 0 equals both empty files and both empty segments; `truncated=0/0`, `cap=65536` recorded |
| Strict JSONL of the separate captures | Compared | both stdout captures | 8 LF-terminated object lines each; no CR, empty, malformed or unterminated line |
| Transcript classes | Compared | transcript | 311 LF lines: 287 host records (exact key order `role` to `detail`), 16 child lines only inside the two stdout segments, 8 markers in four balanced pairs (ctl stdout `L147-156`, ctl stderr `L157-158`, app stdout `L260-269`, app stderr `L270-271`), 0 malformed; ASCII only; final newline. Not strict JSONL as a whole |
| Embedded segments against separate captures | Compared | the four segments and four capture files | byte-identical; parsed records equal |
| Eight named steps per child | Compared with the child source format | capture lines 1-8 | each step once, `seq` 0-7 in source order, mode `control` or `appcontainer` throughout, key sets per line type, every verdict `expected`; control steps 1-6 success with win32 0; app steps 1-2 success, steps 3-6 `expect` `access_denied_5`, observed failure, win32 5 (child self-reports) |
| Host `CAPTURE` summaries | Compared | ctl `L159/s147`, app `L272/s248` | `lines=8 well_formed=1`, all eight steps `expected`: equal to the independent parse |
| Host `CHILD-RESULT` records | Compared | ctl `L160-167/s148-155`, app `L273-280/s249-256` | one per step in order; `ok`, verdict and win32 equal the child lines; `observed=failure` for `CHILD_START` and `DONE` only because those lines have no `observed` field |
| Host validation records | Recorded, agreement compared | `CONTROL-VALIDATION` `L187/s175`, `GATE-RESULT` `L300/s276` | ok; `child_complete_and_all_expected=1`, `host_effects_match=1`; exit and stderr values equal `RESULT` |
| Wrapper host-record stream | Compared | wrapper stdout; transcript host records | 287 CRLF-terminated host records, no marker or child line (by design, `write_capture` writes only the transcript); each record byte-identical in order to its transcript record apart from the terminator; 112,425 bytes = 112,138 bytes of LF-terminated transcript host lines + 287 bytes from replacing LF with CRLF. Wrapper stderr 0 bytes |
| Sequence and time | Compared | transcript; `exit.txt` | host `seq` contiguous 1-287 in both streams; `utc` nondecreasing from `21:07:20.295Z` to `.445Z`, inside the wrapper window `.2601926Z` to `.4515726Z` |
| Completion ordering | Compared | `RUN complete` `L301/s277`; `RUN exit` `L311/s287` | completion is followed by 9 post-run `GRANT-INVENTORY` records (`s278-286`) and then `RUN exit`, the last host record (`ok`, `exit 0`); wrapper `exit=0`. No `ok=false` record after completion or anywhere in the run; no `STOP` record |
| Emitted size against host buffers | Compared | transcript | longest host line 2909 bytes with LF, longest escaped detail 2759 bytes, below the 20000-byte emit and 16000-byte detail buffers |
| Original profile receipt | Recorded named failure | `out/profile-receipt.json` | strict parse fails: `Bad escaped character in JSON at position 248 (line 1 column 249)`. Kept unrepaired |
| Receipt against its host record | Compared | `PROFILE receipt-written-before-launch` `L193/s181` | the decoded record detail equals the 362 receipt bytes: the record carries the same text correctly escaped, the defect is the file serialization |
| Planned moniker | Compared | `L190/s178`, `L191/s179`; `out/planned-moniker.json` | strict parse succeeds; bytes equal the planned record; moniker equals the `CreateAppContainerProfile` record (`HRESULT 0x00000000`) |
| Prior derived summary | Compared | `correction1/profile-summary-derived.json`; `s178-181`, `s222`, `s245` | parses; labelled derived, no fresh query or reconstruction; both cited source digests are current; its six cited host records match by seq, utc, op, label and step; moniker, HRESULTs, package SID (`TokenAppContainerSid` `s222`) and folder text equal the host records; distinct from the original. Still a derivative; nothing repaired |
| Command capture redaction | Compared | `cmd-run1-20261015T210720Z.command.txt` | one `<user-profile>` placeholder, no drive-letter user path (a redacted derivative) |

## 4. Claim assessment

| Recorded claim | Source | Verdict |
|---|---|---|
| Captures exist, parse and have the recorded sizes 1220 and 1261 bytes | feasibility 14.8 R2 | Supported |
| Transcript: 311 lines = 287 host + 16 child + 8 markers; non-comment lines parse | 14.8 R3; metadata | Supported |
| Wrapper stdout is valid JSONL (287 lines), stderr 0 bytes | 14.8 R3; metadata; prior validation log | Supported with a refinement: CRLF terminators (CRT text-mode stdout) versus the LF (`wb`) transcript; the earlier check split on LF and `JSON.parse` accepts a trailing CR, so it did not distinguish them |
| Eight well-formed expected lines per child; app source steps returned 5 | 14.4 | Supported as consistent child self-reports; host source-state records were not re-derived here |
| Each child emitted `DONE`; stderr 0 bytes | 14.4; 14.8 R2 | Supported; the stderr count depends on `drain`, which swallows read errors (audit H37) |
| `observed=failure` for `CHILD_START` and `DONE` is representational | 14.6 | Supported |
| Host exit 0; `RUN complete` and `RUN exit` agree | 14.4 | Supported |
| `RUN complete` precedes the post-run inventory | audit C8(a) | Supported |
| No `ok=false` record after `RUN complete` | audit C2 | Supported; the host discards the inventory result (audit H53), so `ok=true` there is not a gate |
| Receipt invalid at position 248; planned moniker and `PROFILE` records valid; derived summary labelled and sourced | 14.8 R1 | Supported |
| Child timings 27 and 26 ms, exit `0x0`, job total 1 and active 0 | 14.4; metadata | Recorded values only; no independent measurement exists |

**Contradictions: none found.** Refinements: the CRLF/LF stream distinction above; completion is not the last check (ten
records follow it); and the 32- and 34-object inventory totals (`s197`, `s286`) are recorded values not compared here.

## 5. Unknowns and limits

- **Child exit codes.** `RESULT` records `exit=0x0`, but the host ignores the `GetExitCodeProcess` result with the variable
  initialized to 0 (`h:929`). The unchecked measurement stays unproven.
- **Lost output.** Agreeing captures, equal lengths, contiguous `seq` and no partial line show that no numbered host record is
  missing from either retained stream and that no retained line is cut. They cannot show the absence of bytes a child
  never wrote, bytes never drained or a write the host ignored (`emit`, `write_capture`).
- **Buffer truncation before emission** (audit U5): only emitted lengths are visible; descriptor and snapshot texts built in
  fixed buffers could be cut before `rec` without a marker.
- **Live state.** Current ACLs, labels, grants, fixtures and the profile were not inspected; file-hash manifests do not seal
  them, and inventory records are historical.
- **Redaction.** The command capture is the only copy (later in-place redaction); the wrapper's console line
  `stamp=... exit=...` was never retained. Neither is reconstructed.
- **Scope.** Capture consistency only: which check refused each source operation, source-write impossibility, escape
  resistance, Git/GCM compatibility and P9 are outside it. The run is one observation by one build.

## 6. Reproduction and artifacts

From the repository root, after a self-test with synthetic controls for a missing step, truncated JSONL, a duplicated step,
a byte-count mismatch, a post-completion failure, wrapper divergence and an unescaped receipt, plus a positive control:

```bash
node logs/executor/p9-appcontainer-evidence-crosscheck/crosscheck.mjs --self-test
node logs/executor/p9-appcontainer-evidence-crosscheck/crosscheck.mjs --verify
```

The final self-test met all 8 controls (`self-test-20261015T225831Z-10420`, about 10 ms). The single evidence pass
(`verify-20261015T225838Z-10692`, about 21 ms) met every expectation and wrote its own manifest. An earlier self-test
(`self-test-20261015T225750Z-18916`) failed its positive control because the key-value parser rejected keys containing
digits (`win32`); that report is retained and the reader was corrected before the verify pass. The root also holds the
before/after protection hashes and a final output manifest, `manifest-outputs-20261015T225954Z.sha256` (`2ca80368...bc46`).
