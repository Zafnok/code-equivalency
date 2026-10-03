# P2-058 verdict: three cleanup pairs (2026-10-02)

Three public pull requests whose authors say they change no behaviour, pinned as ADR 0040's
`cleanup` kind and each run in `full` mode at equiv `46e6636`. Per-pair detail is in
`docs/runs/2026-10-02-cleanup-<slug>/SUMMARY.md`. Nothing under `src/` or `tests/` was changed.

## Cleanup pairs

**These pairs take no part in ADR 0028's rule table** (ADR 0040 decision 5). No threshold is
applied to them, no median includes them, and this file has no continue, re-scope or stop line.
`docs/runs/2026-09-30-full-verdict.md` remains the verdict.

### Outcome in one line

**No cleanup changed behaviour, and `equiv` could say so for 1 changed pair in 10.** Of 575 changed
pairs the solver proved 57 (9.9%). 52 are Divergent, and none of those is a behaviour change the
pull request made. 466 are Unknown. All three runs exit 5.

### Per pair

| Pair | What the pull request does | Detected runtimes, legacy / modern | `full` exit | Matched pairs | Changed pairs |
|---|---|---|---|---|---|
| gitextensions-11372 | IDE0028, collection expressions | net8.0 / net8.0 | 5 | 14560 | 351 |
| gitextensions-11284 | IDE0008, explicit type for `var` | net6.0 / net6.0 | 5 | 14532 | 212 |
| powershell-19687 | IDE0019, `as` plus null check becomes `is` | net8.0 / net8.0 | 5 | 33889 | 140, of which 12 edited |

Every project on both sides of a pair has the same runtime, apart from one `netstandard2.0`
source-generator project per solution that has no host. No pair crosses a runtime, so no runtime
rule applied and there is no EQ006 anywhere.

PowerShell's 140 changed pairs include 128 whose source is the same on both sides (82 `unbound`,
46 `no-body`). A whole-body opaque pair is never congruent, so the census counts it as changed
(P2-097). The rows below use the 12 pairs the pull request edited.

| Pair | Changed pairs | Proved Equivalent, by `proofMethod` | Unknown, by reason | Divergent |
|---|---|---|---|---|
| gitextensions-11372 | 351 | 7 (2.0%): bounded 7 | 319 (90.9%): opaque 261, timeout 43, unaligned-loop 7, abstraction 6, recursion 2 | 25 (7.1%) |
| gitextensions-11284 | 212 | 50 (23.6%): lockstep-induction 45, bounded 5 | 140 (66.0%): opaque 110, timeout 17, abstraction 9, unaligned-loop 4 | 22 (10.4%) |
| powershell-19687 | 12 | 0 (0%) | 7 (58.3%): unaligned-loop 2, timeout 2, recursion 1, abstraction 1, opaque 1 | 5 (41.7%) |
| **All** | 575 | 57 (9.9%) | 466 (81.0%) | 52 (9.0%) |

`unmatched-overload` Unknowns (23 and 22 on the two Git Extensions pairs) are not matched pairs and
are left out, as in `docs/runs/2026-10-01-migrations-verdict.md`.

### Every Divergent, adjudicated

P2-047's method (`docs/runs/2026-09-30-divergent-audit.md`). Replay existed only on gitextensions-11284, the one pair
whose runtime is installed here, and gave nothing to go on (all 22 `not-constructible`). Each result was
hand-traced: both files were compared and the model was read against both bodies. Identities are in
each SUMMARY.

| Pair | EQ002 | Confirmed: the cleanup changed behaviour | False positive | Real by construction, not the cleanup | Undetermined |
|---|---|---|---|---|---|
| gitextensions-11372 | 25 | 0 | 24 | 1 | 0 |
| gitextensions-11284 | 22 | 0 | 22 | 0 | 0 |
| powershell-19687 | 5 | 0 | 5 | 0 | 0 |
| **All** | 52 | 0 | 51 | 1 | 0 |

By cause:

| Cause | Results | Ticket |
|---|---|---|
| A `[CallerFilePath]` argument holds the absolute path of the source file, and the two sides are two checkout directories. 26 of these 44 are in files that are byte-identical on both sides. | 44 | P2-098 |
| `BugReporter.Program::Main()` passes a commit hash that the build generates. The two sides are two commits, so the values do differ. The pull request did not touch the file. | 1 | P2-098 |
| `x as T` plus a null check, against `x is T t`: the cast's null flag and the type test are unrelated in the encoding. | 5 | P2-093 |
| A collection expression against the `new()` and initializer it replaces: the two call traces start with different calls. | 2 | P2-099 |

The one "real by construction" result is not counted as confirmed, because criterion 4 defines
confirmed as "the cleanup changed behaviour", and it is not a false positive either, because the
two builds do pass different strings.

Divergent precision on these pairs is 0 of 51. P2-047's audit measured 3.8% (2 of 53) on
migrations. Neither sets a threshold.

### What this says about cleanup commits

- **The unchanged part is handled.** 97.6%, 98.5% and 99.6% of matched pairs are congruent, with
  no runtime rule in the way. That is what ADR 0040 was for.
- **The changed part mostly is not.** The best pair is the `var` rewrite, where a body often lowers
  to the same IR and the solver proved 23.6%. The collection-expression pull request is 2.0%,
  because the one construct it introduces is opaque. The `as`-to-`is` pull request is 0 of 12,
  because the two forms are modelled apart.
- **Each pull request applies one rewrite, and for two of the three `equiv` has no model of that
  rewrite.** P2-099 and P2-093 are those two.
- **None of the 52 Divergent was a behaviour change.** 45 of them come from the two sides being
  two checkout directories, which is how every corpus pair is laid out.

### Skipped and replaced pairs

None. All three pairs named in P2-058 loaded, and none was replaced. Two needed work first:
- Both Git Extensions pairs failed in one second on the first attempt, because their `global.json`
  pins an SDK major the box does not have. `corpus.ps1 -Fetch` now patches that policy.
- `powershell-19687` needed three manual steps (an environment variable, a local tag, and the
  repository's own resource generator), recorded in `tools/corpus/README.md`. Its first run skipped
  `System.Management.Automation` on the legacy side and exited 4; that run is void.

### Execution

`--execute` runs each side on its detected runtime and never substitutes another (ADR 0040
decision 3). This box has Microsoft.NETCore.App 6.0.36, 10.0.9 and 10.0.12.
- gitextensions-11284 (net6.0): run, 3034s, exit 5, same verdicts as `full`. Replay: 22 of 22 EQ002
  `not-constructible`. Differential testing: 49 of 140 Unknown pairs ran, 91 not constructible, no
  difference found.
- gitextensions-11372 and powershell-19687 (net8.0): execution unavailable, net8.0 not installed.
  Installing it changes the machine, so it was not done.

There is no `not-reproduced` replay anywhere, and no Unknown became Divergent.

### Findings and their tickets

| Finding | Ticket |
|---|---|
| Build-location constants: 45 Divergent, and unedited bodies that are not congruent | P2-098 |
| Collection expressions are an opaque `Conversion`: 308 of 351 changed pairs on gitextensions-11372 | P2-099 |
| `as` plus null check against an `is` pattern: 5 Divergent, 0 of 12 proved | P2-093 |
| `is not T t` is opaque: 3 of PowerShell's 12 edited pairs | P2-094 |
| Five lowering crashes (null references) over the three runs, so every run exits 5 | P2-095 |
| 82 `unbound` pairs on PowerShell, where the diagnostics a hand build shows are warnings promoted to errors | P2-096 |
| Identical whole-body opaque pairs counted as changed on a same-runtime pair: 128 of PowerShell's 140 | P2-097 |
| 62 timeouts over the three runs, and two pairs that ran 91 and 48 minutes on gitextensions-11372 | P2-076, P2-083 (timeouts), both open |
