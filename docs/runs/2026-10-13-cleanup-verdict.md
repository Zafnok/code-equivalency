# P2-110 verdict: the three cleanup pairs again, with no crashed pair (2026-10-13)

`gitextensions-11372`, `gitextensions-11284` and `powershell-19687`, each run in `full` mode at
equiv `78853202`, one after the other, on a box that ran nothing else. Per-pair detail is in
`docs/runs/2026-10-13-cleanup-<slug>/SUMMARY.md`. Nothing under `src/`, `tests/` or `tools/` was
changed.

Every number here stands next to the one in P2-058's verdict, `docs/runs/2026-10-02-cleanup-verdict.md`
(equiv `46e6636`), which this file replaces as the adjudication of the cleanup pairs.

## Cleanup pairs

**These pairs take no part in ADR 0028's rule table** (ADR 0040 decision 5). No threshold is
applied to them, no median includes them, and this file has no continue, re-scope or stop line.

### Outcome in one line

**No cleanup changed behaviour, no run crashed, and `equiv` can now say so for 6 changed pairs in
10, where it was 1 in 10.** Of 509 changed pairs the solver proves 297 (58.3%, from 57 of 575,
9.9%). 2 are Divergent, from 52, and both are one procedure whose build-generated commit constant
differs between any two commits. 210 are Unknown, from 466. The runs exit 1, 1 and 0, where all
three exited 5.

### How these runs differ from P2-058's

| | P2-058 (2026-10-02) | These runs |
|---|---|---|
| equiv commit | `46e6636` | `78853202` |
| Compare mode | before the modes (ADR 0049): bound 3, resourceLimit 5000000 | `quick`, the default (ADR 0052): bound 3, resourceLimit 2000000, timeoutMs 60000. A `thorough` run of each pair followed and is reported in its own section |
| Pairs verified at a time | 1 | 24 (no `--jobs`; the box has 24 processors) |
| Wall-clock | 4h43m, 44m and 5m | 259 s, 229 s and 183 s |
| Lowering crashes | 5 procedures over the three runs; every run exit 5 | none; no `run.properties.unverified`, no tool execution notification, exits 1, 1 and 0 |

- Decision: the ticket names no compare mode. `equiv-corpus-run` asks for thorough for a verdict and
  `equiv-scoreboard` takes only quick runs as a source, and criterion 6 applies the scoreboard. So
  both were run, as P2-124 did: the tables below are the quick runs and the thorough runs'
  differences have their own section, adjudicated the same way.
- Decision: no `--jobs`. No earlier run of these pairs is in a compare mode, so there is no time to
  compare with and the default is what a user gets. Every timeout of the three quick runs ends on
  the resource limit and none on the wall clock (`run.properties.queryEndings`), so the number of
  pairs at a time does not decide a result.

So a count can differ from P2-058's because of a fix or because of the smaller resource limit.

### Precision tickets done at the commit that ran

All eight the ticket names are done at `78853202`: P2-071 (PR 362), P2-098 (PR 367), P2-101
(PR 430), P2-103 (PR 470), P2-104 (PR 472), P2-106 (PR 424), P2-107 (PR 425) and P2-120 (PR 384).
So are the crash fix P2-105 (PR 379) and the rest of the ticket's dependencies: P2-093, P2-127,
P2-077 and P1-032.

### Per pair

| Pair | What the pull request does | Detected runtimes, legacy / modern | | `full` exit | Matched pairs | Congruent pairs | Changed pairs |
|---|---|---|---|---|---|---|---|
| gitextensions-11372 | IDE0028, collection expressions | net8.0 / net8.0 | before | 5 | 14560 | 14208 (97.6%) | 351 |
| | | | now | 1 | 14614 | 14295 (97.8%) | 319 |
| gitextensions-11284 | IDE0008, explicit type for `var` | net6.0 / net6.0 | before | 5 | 14532 | 14317 (98.5%) | 212 |
| | | | now | 1 | 14586 | 14408 (98.8%) | 178 |
| powershell-19687 | IDE0019, `as` plus null check becomes `is` | net8.0 / net8.0 | before | 5 | 33889 | 33748 (99.6%) | 140, of which 12 edited |
| | | | now | 0 | 34183 | 34146 (99.9%) | 37, of which 12 edited |

Every project on both sides of a pair has the same runtime, apart from one `netstandard2.0`
source-generator project per solution that has no host. No pair crosses a runtime, so no runtime
rule applied and there is no EQ006 anywhere. Every project loads on both sides of all three pairs.

PowerShell's row below uses the 12 pairs the pull request edited, as P2-058's verdict did, so the
two totals are the same count. Its other 25 changed pairs are in byte-identical files: 17
`unbound` (82 before), 3 the solver proves, 3 `opaque` and 2 `timeout`. The 46 identical `no-body`
pairs that P2-058 counted as changed no longer are (P2-107).

| Pair | | Changed pairs | Proved Equivalent, by `proofMethod` | Unknown, by reason | Divergent |
|---|---|---|---|---|---|
| gitextensions-11372 | before | 351 | 7 (2.0%): bounded 7 | 319 (90.9%): opaque 261, timeout 43, unaligned-loop 7, abstraction 6, recursion 2 | 25 (7.1%) |
| | now | 319 | 217 (68.0%): bounded 153, lockstep-induction 64 | 101 (31.7%): opaque 69, timeout 26, abstraction 4, unaligned-loop 1, recursion 1 | 1 (0.3%) |
| gitextensions-11284 | before | 212 | 50 (23.6%): lockstep-induction 45, bounded 5 | 140 (66.0%): opaque 110, timeout 17, abstraction 9, unaligned-loop 4 | 22 (10.4%) |
| | now | 178 | 73 (41.0%): lockstep-induction 52, bounded 21 | 104 (58.4%): opaque 82, timeout 17, abstraction 5 | 1 (0.6%) |
| powershell-19687 | before | 12 | 0 (0%) | 7 (58.3%): unaligned-loop 2, timeout 2, recursion 1, abstraction 1, opaque 1 | 5 (41.7%) |
| | now | 12 | 7 (58.3%): bounded 6, lockstep-induction 1 | 5 (41.7%): unaligned-loop 3, recursion 1, timeout 1 | 0 (0%) |
| **All** | **before** | 575 | 57 (9.9%) | 466 (81.0%) | 52 (9.0%) |
| | **now** | 509 | 297 (58.3%): bounded 180, lockstep-induction 117 | 210 (41.3%): opaque 151, timeout 44, abstraction 9, unaligned-loop 4, recursion 2 | 2 (0.4%) |

`unmatched-overload` Unknowns (23 and 22 on the two Git Extensions pairs, as before) are not
matched pairs and are left out. Counting all 37 of PowerShell's changed pairs, the totals are 534
changed pairs: 300 proved (56.2%), 232 Unknown (43.4%) and 2 Divergent (0.4%).

`gitextensions-11372` was also run on two branches on 2026-10-03, before the compare modes: P2-099's
proved 160 of 351 and P2-120's 240 of 319 (75.2%). This run proves 217 of the same 319. Two things
changed in between and both point that way: quick mode's resource limit is 2000000 where those
runs had 5000000 (26 timeouts, from 19), and a call to a local function is now an opaque with
reason `LocalFunction`, which P2-127 added to close a false-Equivalent path (69 `opaque`, from 54).

### Every Divergent, adjudicated

P2-047's method (`docs/runs/2026-09-30-divergent-audit.md`). No replay exists (`--execute` is out
of the ticket's scope), so each result was hand-traced: both files were compared and the first call
at which the model's two traces part was read against the body.

| Pair | | EQ002 | Confirmed: the cleanup changed behaviour | False positive | Real by construction, not the cleanup | Undetermined |
|---|---|---|---|---|---|---|
| gitextensions-11372 | before | 25 | 0 | 24 | 1 | 0 |
| | now | 1 | 0 | 0 | 1 | 0 |
| gitextensions-11284 | before | 22 | 0 | 22 | 0 | 0 |
| | now | 1 | 0 | 0 | 1 | 0 |
| powershell-19687 | before | 5 | 0 | 5 | 0 | 0 |
| | now | 0 | 0 | 0 | 0 | 0 |
| **All** | **before** | 52 | 0 | 51 | 1 | 0 |
| | **now** | 2 | 0 | 0 | 2 | 0 |

Both results are `BugReporter.Program::Main()`, in a file that is byte-identical on both sides of
both pairs. The two traces make the same calls in the same order, and the first argument that
differs is the string passed to `BugReporter.UserEnvironmentInformation::Initialise(string,bool)`,
which the body reads from a generated `ThisAssembly` constant holding the commit. The two sides are
two commits, so the values do differ. As in P2-058's verdict it is not counted as confirmed,
because criterion 4 defines confirmed as "the cleanup changed behaviour", and it is not a false
positive either. ADR 0046 (P2-098) keeps a generated commit constant an ordinary constant on purpose, and says this procedure stays Divergent on any pair of two commits; no ticket.

Where P2-058's causes went:

| Cause, in P2-058's verdict | Results then | Now | Ticket |
|---|---|---|---|
| A `[CallerFilePath]` argument holds the checkout directory | 44 | 0 Divergent | P2-098, done |
| `BugReporter.Program::Main()` passes a commit hash the build generates | 1 | 2 (it is now decided on `gitextensions-11284` too) | none: real by construction |
| `x as T` plus a null check, against `x is T t` | 5 | 0: all five are proved Equivalent | P2-103, done |
| A collection expression against the `new()` and initializer it replaces | 2 | 0 Divergent | P2-099 and P2-120, done |

Divergent precision on these pairs has nothing left to measure: 0 confirmed and 0 false positives,
from 0 of 51. P2-047's audit measured 3.8% (2 of 52) on the migration and P2-124 40.0% (6 of 15) on
the upgrades. None sets a threshold.

### What this says about cleanup commits

- **The unchanged part is handled.** 97.8%, 98.8% and 99.9% of matched pairs are congruent.
- **The changed part now mostly is.** The collection-expression pull request goes from 2.0% to
  68.0%, the `as`-to-`is` pull request from 0 of 12 to 7 of 12, and the `var` rewrite from 23.6% to
  41.0%. Each pull request applies one rewrite, and `equiv` now models all three.
- **A Divergent on a cleanup is rare and none is noise from the checkout layout.** 2 of 509, from 52
  of 575, and the 45 that came from the two sides being two checkout directories are gone.
- **What is left is Unknown, and most of it is an opaque construct.** 151 of the 210. On the `var`
  pull request it is one construct: `DeconstructionAssignment` is in the reason set of 46 of its
  178 changed pairs. On the collection-expression pull request the largest is `LocalFunction`, in
  43 of 319.

### Skipped and replaced pairs

None. All three pairs loaded and none was replaced. `.corpus/` was made new for this ticket
(`-Prepare`, `-Fetch`, restore). `powershell-19687` needed the three manual steps
`tools/corpus/README.md` records; both Git Extensions pairs were restored with
`dotnet restore --force`.

### Execution

Not run: `--execute` is out of the ticket's scope. The box has Microsoft.NETCore.App 6.0.36, 10.0.9
and 10.0.12, as on 2026-10-02.

### Thorough runs

Each pair was run again in compare mode thorough, on the same build and checkouts, with no
`--jobs`. The first pass is the quick run; a budget pass and an IL pass then take the pairs still
Unknown, at bound 3, resourceLimit 30000000 and timeoutMs 600000 (ADR 0049). No run crashed: no
`run.properties.unverified` and no notification, exits 1, 1 and 0. No Equivalent or Divergent of the
quick runs changes in the thorough runs.

| Pair | | Wall-clock | Changed pairs | Proved Equivalent | Unknown | Divergent |
|---|---|---|---|---|---|---|
| gitextensions-11372 | quick | 259 s | 319 | 217 (68.0%) | 101 (31.7%) | 1 (0.3%) |
| | thorough | 3846 s | 319 | 222 (69.6%): bounded 157, lockstep-induction 65 | 93 (29.2%): opaque 70, timeout 18, abstraction 4, unaligned-loop 1 | 4 (1.3%) |
| gitextensions-11284 | quick | 229 s | 178 | 73 (41.0%) | 104 (58.4%) | 1 (0.6%) |
| | thorough | 3357 s | 178 | 95 (53.4%): lockstep-induction 60, bounded 35 | 82 (46.1%): opaque 72, abstraction 5, timeout 5 | 1 (0.6%) |
| powershell-19687, the 12 edited | quick | 183 s | 12 | 7 (58.3%) | 5 (41.7%) | 0 |
| | thorough | 470 s | 12 | 8 (66.7%): bounded 7, lockstep-induction 1 | 4 (33.3%): unaligned-loop 3, recursion 1 | 0 |
| **All** | **quick** | | 509 | 297 (58.3%) | 210 (41.3%) | 2 (0.4%) |
| | **thorough** | | 509 | 325 (63.9%) | 179 (35.2%) | 5 (1.0%) |

| Pair | Pairs the budget pass took | Pairs the IL pass took | Results the budget pass decided or changed | The IL pass |
|---|---|---|---|---|
| gitextensions-11372 | 58 (3266 s) | 18 (294 s) | 8, all `timeout` to `opaque` | 8: 5 Equivalent, 3 Divergent |
| gitextensions-11284 | 50 (2952 s) | 27 (132 s) | 12, all `timeout` to `opaque` | 22, all Equivalent |
| powershell-19687 | 9 (243 s) | 0 | 2: 1 Equivalent, 1 `timeout` to `opaque` | none |

So thorough costs 15 times quick on the two Git Extensions pairs and under 3 times on PowerShell,
proves 28 more pairs, 22 of them on the `var` pull request through the IL pass, and adds 3
Divergent, none of which is a behaviour change. With 30000000 in place of 2000000 the budget pass
proves one pair and moves 21 timeouts to `opaque`: the larger budget gets far enough to reach an
opaque node, and no further.

The 3 Divergent the thorough runs add, adjudicated as above:

| Pair | Results | Classification | Cause | Ticket |
|---|---|---|---|---|
| gitextensions-11372 | 3 EQ002, `decidedBy: il-pass`: `GitExtUtilsTests.ArgumentBuilderTests::Add(string[],int,string)`, `GitExtUtilsTests.LazyStringSplitTests::None(string,char,string[])` and `::RemoveEmptyEntries(string,char,string[])` | false positive | Each replaces an empty collection and a `foreach` that calls `Add` with a collection expression holding one spread. The two forms make the same calls. In the counterexample the modern side throws a `NullReferenceException` where the legacy side goes on, at a point where neither source form dereferences anything the other does not. All three are Unknown (`opaque`, `CollectionExpression`) in the quick run | P2-169 (new) |

The fourth Divergent of `gitextensions-11372` and the one of `gitextensions-11284` are
`BugReporter.Program::Main()`, as in the quick runs.

Thorough precision over the three pairs: 0 confirmed and 3 false positives of 5, with 2 real by
construction, which is 0% (0 of 3). The README's scoreboard rests on the quick runs
(`equiv-scoreboard`), where there is nothing left to decide.

### Findings and their tickets

| Finding | Ticket |
|---|---|
| No lowering crash on any pair: the five procedures P2-058's runs threw on all have results | P2-105, done |
| `BugReporter.Program::Main()` is Divergent on both Git Extensions pairs for its commit constant | none: real by construction (ADR 0046) |
| `DeconstructionAssignment` is in the reason set of 46 of `gitextensions-11284`'s 178 changed pairs (25.8%) and is the first reason of 34 of its Unknowns | P2-170 (M, new). P2-025 lowers a tuple literal only; the pull request's 38 rewritten deconstructions take apart a call's result (26), an array element (9), an awaited value (2) or a new object (1) |
| `LocalFunction` is in the reason set of 43 of `gitextensions-11372`'s 319 changed pairs (13.5%) | P2-171 (L, new). P2-127 made the call opaque on purpose and left proving it to a later ticket |
| `CollectionExpression` is left in 11 changed pairs of `gitextensions-11372` | P2-128, open |
| 44 timeouts over the edited pairs (26, 17 and 1), all on the resource limit. Thorough mode's budget pass, at 15 times the limit, proves 1 of them and finds an opaque node behind 20 | none: the roadmap's "Not on the milestone" paragraph says what is known about the solver-budget Unknowns |
| Thorough's IL pass reports 3 false EQ002 on a collection expression with a spread; thorough mode only | P2-169 (M, new) |
| 17 `unbound` pairs on PowerShell, from 82, in files the pull request did not touch | P2-172 (M, new) |
| 8 pairs in byte-identical PowerShell files are neither congruent nor `unbound` (3 proved, 3 `opaque`, 2 `timeout`), and 3 on the two Git Extensions pairs that are not `no-body` or the commit constant (2 proved, 1 `abstraction`). The SARIF does not say what keeps an unedited body out of congruence on a same-runtime pair | P2-173 (M, new) |

### Scoreboard (criterion 6)

`equiv-scoreboard` was applied. No cleanup pair is one of the large pairs (those are `human`
pairs), so the rows over the large pairs do not move. One scoreboard row changed, Divergent
precision, in its cleanup part only: from 0% (0 of 51) to none left to decide. Under the
scoreboard, the sentences and table cells that quote a cleanup number were rewritten: the two
cleanup rows of "Other pairs, for range", the two cleanup rows of the run-time table, and the
cleanup figures in the "How close to usable" table. No other line of `README.md` changed.

### Method notes

- **Runs.** Six, one after the other, from one build of `78853202`: the three pairs in quick, then
  the three in thorough.
- **P2-058's numbers** are quoted from its verdict and summaries; its SARIF files are not on this
  checkout, so no result was matched to an earlier one by identity except through those files.
- **Tests.** No pair has a `verify_command`; no upstream test was run.
- Source text and model values stay under `.corpus/` (`docs/runs/README.md`).
