# P2-130 verdict: the three migration pairs on one commit, and the scoreboard over all five large pairs (2026-10-14)

`gitextensions-8522` (.NET Framework 4.8 to .NET 5), `duplicati-3124` (.NET Framework to .NET 5, first
step) and `openra-17989` (Mono/net472 plus .NET 5), each run in `full` mode, compare mode quick (the
default, ADR 0052), at equiv `e98fd1b9`, one after the other from one build, on a box that ran nothing
else. Per-pair detail is in `docs/runs/2026-10-13-full-gitextensions-8522/SUMMARY.md`,
`docs/runs/2026-10-13-full-duplicati-3124/SUMMARY.md` and `docs/runs/2026-10-14-full-openra-17989/SUMMARY.md`.
Nothing under `src/`, `tests/` or `tools/` was changed.

**These pairs take no part in ADR 0028's rule table here.** This file measures and adjudicates; it has
no continue, re-scope or stop line. `docs/runs/2026-09-30-full-verdict.md` remains the ADR 0028 verdict.
It replaces `docs/runs/2026-09-30-divergent-audit.md` as the adjudication of migration pairs.

## Outcome in one line

**All three migrations now run to a result with no crashed pair, and Duplicati and OpenRA have
verdicts for the first time. Divergent precision on migrations is 1.4% (1 of 69 decided): the fixes the
milestone named removed most of the false positives P2-047 counted, and the samples drawn today find 68
more with eleven causes, each now a ticket.** Half the migrations' changed pairs (1,004 of 1,966) are
bodies nobody edited that a runtime rule keeps out of congruence; the solver proves 5 of them. That is
the largest cause of both the Unknown and the Divergent rows, and it had no ticket (P2-181, new).

## How these runs differ from the last summaries

| | Last summary | These runs |
|---|---|---|
| gitextensions-8522 | 2026-10-08, equiv `18b751f7`, quick, `--jobs 4`, 364 s, exit 1 | `e98fd1b9`, quick, `--jobs 4`, 413 s, exit 1 |
| duplicati-3124 | 2026-10-01, equiv `ef79ff6`: `full` crashed before the first verdict (P2-082); a `--lower-only` census only | 547 s, exit 1, first run to a result |
| openra-17989 | 2026-10-01, equiv `ef79ff6`: as Duplicati | 292 s, exit 1, first run to a result |

- Commit: criterion 1 asks for the commit P2-124 and P2-110 ran at when nothing under `src/` has merged
  since, otherwise `main`'s head. They ran at two commits, `1d767f54` (P2-124) and `78853202` (P2-110),
  and four pull requests that change `src/` merged after both (P2-170 to P2-173). So these runs are at
  `main`'s head, `e98fd1b9`. The scoreboard's five large pairs rest on three commits: `e98fd1b9` (the
  three migrations) and `1d767f54` (the two upgrades).
- Decision: compare mode quick only. Criterion 1 names `compare`'s default mode, which is quick (ADR
  0052), and the scoreboard takes only quick runs. No thorough run was made.
- Decision: `--jobs 4`, the value `gitextensions-8522`'s 2026-10-08 run and both upgrade runs used, so
  the times compare and the scoreboard's run-time row keeps one meaning. Every query that ran out of
  budget ended on the resource limit and none on the wall clock (`run.properties.queryEndings`:
  176, 255, 232), so the number of pairs at a time decides no result.
- `.corpus/` was made new for this ticket (`-Prepare`, `-Fetch`, restore of both sides of each pair as
  the skill says); every restore succeeded.

## Crashes (criterion 2)

None. All three runs exit 1 (Divergent results exist), with no `run.properties.unverified` entry and
no tool execution notification. Every project loads on both sides of all three pairs (`Duplicati.Tools`,
P2-084's placeholder, no longer counts against the load rate).

## Tickets done at the commit that ran (criterion 3)

Every ticket on P2-130's `Depends on` line is done at `e98fd1b9`: P2-073 (PR 456), P2-074 (PR 464),
P2-075 (PR 457), P2-103 (PR 470), P2-117 (PR 459), P2-118 (PR 465), P2-122 (PR 444), P2-087 (PR 445),
P2-095 (PR 450), P2-093 (PR 458), P2-104 (PR 472), P1-028 (PR 418), P2-127 (PR 402), P2-077 (PR 403),
P1-032 (PR 415), P2-124 (PR 483) and P2-110 (PR 487). So are the seven the criterion also names: P2-068
(PR 355), P2-069 (PR 326), P2-070 (PR 359), P2-071 (PR 362), P2-098 (PR 367), P2-105 (PR 379) and P2-113
(PR 380). Also done since `gitextensions-8522`'s last run and relevant to it: P2-136 to P2-141 (P1-028's
tickets) and P2-170 to P2-173.

## Per pair (criterion 3)

Shares are over ADR 0034's changed pairs. An Unknown that is `unmatched-overload` is not a matched pair
and is left out. Duplicati's and OpenRA's last summaries are censuses, so they have no verdicts.

| Pair | | Matched pairs | Congruent pairs | Changed pairs | Proved Equivalent by the solver, by `proofMethod` | Unknown, by reason | EQ002 | EQ006 |
|---|---|---|---|---|---|---|---|---|
| gitextensions-8522 | 2026-10-08 | 13592 | 12682 (93.3%) | 910 | 45 (4.9%): bounded 40, lockstep-induction 5 | 596 (65.5%): opaque 256, abstraction 218, timeout 109, unaligned-loop 12, recursion 1 | 30 | 239 |
| | now | 13592 | 12654 (93.1%) | 938 | 140 (14.9%): bounded 126, lockstep-induction 14 | 603 (64.3%): abstraction 252, opaque 233, timeout 104, unaligned-loop 13, recursion 1 | 29 | 166 |
| duplicati-3124 | 2026-10-01 census | 6275 | 5331 (85.0%) | 936 | n/a: crashed | n/a | n/a | n/a |
| | now | 6294 | 5587 (88.8%) | 707 | 189 (26.7%): bounded 162, lockstep-induction 27 | 352 (49.8%): opaque 125, timeout 124, abstraction 92, unaligned-loop 8, recursion 3 | 24 | 142 |
| openra-17989 | 2026-10-01 census | 10114 | 9688 (95.8%) | 418 | n/a: crashed | n/a | n/a | n/a |
| | now | 10120 | 9799 (96.8%) | 321 | 4 (1.2%): bounded 3, lockstep-induction 1 | 267 (83.2%): timeout 114, abstraction 99, opaque 42, unaligned-loop 10, recursion 2 | 2 | 48 |
| **All three, now** | | 30006 | 28040 (93.4%) | 1966 | 333 (16.9%) | 1222 (62.2%) | 55 | 356 |

- `gitextensions-8522`: Divergent falls from 269 to 195 (EQ006 from 239 to 166) and the solver proves
  140 changed pairs where it proved 45. 28 fewer pairs are congruent: the same 13,592 matched pairs,
  and P2-173's run on the cleanups showed fingerprint changes moving bodies both ways; the 28 were not
  traced one by one.
- `duplicati-3124` and `openra-17989`: OpenRA's solver proves 4 of 321. 242 of its 321 changed pairs are
  unedited bodies a runtime rule keeps out of congruence (next section), and its Unknowns are mostly
  `timeout` (114) and `abstraction` (99, float arithmetic among the largest kinds: `conv.f32.i32` 19,
  `f32.mul` 17).

## Why a changed pair is not congruent

`properties.notCongruent` (P2-173) names the first part where the two texts differ. On every pair the
largest component is `runtime-rule`: the two texts are equal, and the body is kept out of congruence
because it calls a member whose behaviour changed between the pair's runtimes (ADR 0040).

| Pair | Changed pairs | `runtime-rule` | proved | EQ006 | Unknown (abstraction, timeout, opaque, other) |
|---|---|---|---|---|---|
| gitextensions-8522 | 938 | 493 (52.6%) | 4 | 150 | 339 (201, 76, 54, 8) |
| duplicati-3124 | 707 | 269 (38.0%) | 1 | 122 | 146 (66, 62, 13, 5) |
| openra-17989 | 321 | 242 (75.4%) | 0 | 47 | 195 (81, 85, 22, 7) |
| All three | 1,966 | 1,004 (51.1%) | 5 | 319 | 680 (348, 223, 89, 20) |

These 1,004 unedited bodies are 680 of the 1,222 Unknown results (55.6%) and 319 of the 411 Divergent
(77.6%). P2-073 to P2-075 narrowed when a row fires EQ006; nothing decides the bodies whose rows
cannot fire. Filed as P2-181. Its EQ006 half is also where most of the adjudicated false positives
below sit (P2-188 to P2-190).

## Adjudication (criterion 4)

P2-047's method (`docs/runs/2026-09-30-divergent-audit.md`), with its two decisions and the two of
`docs/runs/2026-10-12-upgrade-verdict.md`: **confirmed** needs an executed oracle (replay or a test);
**false positive** is a hand trace showing the claimed difference cannot happen on the pair's
runtimes; anything else is **undetermined**, with its obstacle. No replay exists for these runs and
none of the pairs' modern runtime (net5.0) is installed, so nothing was executed, and a difference real
by hand trace is undetermined. Bodies were diffed between the two checkouts; for EQ006 the catalogue's
row, `changedIn` and precondition were read. Every result was found in the SARIF.

- Decision: a fresh sample of the same size is drawn on `gitextensions-8522` too, beside the
  re-adjudication of P2-047's sample the criterion asks for. 48 of P2-047's 60 results are no longer
  Divergent, so the 12 that are describe what survived the fixes, not what the run reports today, and
  a precision taken from them alone would not be the pair's. The fresh sample is P2-047's draw (per
  rule, sorted by `resultFingerprint/v1`, `random.Random(47).shuffle`, first 30), on all three pairs.
  A result in both is adjudicated once.
- Decision: Duplicati's four `SystemIOWindows` path results whose argument always carries a device
  prefix (`\\?\`) are undetermined, not false positives. That .NET Framework 4.6.2 skips its
  invalid-character check for such a path rests on reading, not on a run.
- The tracing was done by three sub-agents, one per pair, under one written brief; their tables were
  read in full, and the two causes with the most results (P2-184 and the `runtime-rule` count) were
  checked against the SARIF and the lowering's source.

### P2-047's sample, again

Of P2-047's 60 Git Extensions results, by its classification and by what the same procedure is now:

| P2-047 class | Congruent | Proved by the solver | Unknown | Still Divergent |
|---|---|---|---|---|
| false positive (40) | 21 | 1 | 11 (opaque 8, abstraction 3) | 7 |
| undetermined (19) | 8 | 0 | 7 (abstraction 6, opaque 1) | 4 |
| confirmed (1) | 0 | 0 | 0 | 1 |

By the ticket P2-047 gave each false positive: P2-068's 11 are 10 congruent and 1 Unknown; P2-069's 9
are 8 Unknown (`opaque`, `rebound-call`, ADR 0042) and 1 still EQ002; P2-070's 2 are 1 proved and 1
still EQ002; P2-071's 3 are 1 Unknown and 2 still EQ002; P2-073's 8 are 7 congruent and 1 Unknown;
P2-074's 5 are 2 congruent and 3 still EQ006; P2-075's 2 are congruent.

The 12 still Divergent: `EasyHook.LocalHook::Release()` stays confirmed (replay `reproduced` in
P2-047; the modern side compiles the native call out); four stay undetermined with the same cause
(`BranchComparer::Compare`, `FontContainer::ParseFont`, `ConfigFileSettings::CreateGlobal`,
`Git.hub.Organization::GetHashCode`); seven are false positives. Five of them keep P2-047's cause, now a
case its done ticket left (rows 40 and 43, P2-071's catalogue: P2-186; rows 52, 61 and 62, a valid path
P2-074 does not follow: P2-188). Two have a new cause: row 26's rebound `Returns` call is now paired and
what differs is a retyped return value (P2-185); row 30's `TrimEnd` entry now applies and what differs
is the rewritten call's heap effect (P2-184). **1 confirmed, 7 false positive, 4 undetermined.**

### Today's samples

| Pair | Rule | In run | Sampled | Confirmed | False positive | Undetermined | Precision |
|---|---|---|---|---|---|---|---|
| gitextensions-8522 | EQ002 | 29 | 29 | 1 | 12 | 16 | 7.7% (1 of 13) |
| | EQ006 | 166 | 30 | 0 | 16 | 14 | 0% (0 of 16) |
| | **all** | 195 | 59 | 1 | 28 | 30 | **3.4% (1 of 29)** |
| duplicati-3124 | EQ002 | 24 | 24 | 0 | 16 | 8 | 0% (0 of 16) |
| | EQ006 | 142 | 30 | 0 | 11 | 19 | 0% (0 of 11) |
| | **all** | 166 | 54 | 0 | 27 | 27 | **0% (0 of 27)** |
| openra-17989 | EQ002 | 2 | 2 | 0 | 2 | 0 | 0% (0 of 2) |
| | EQ006 | 48 | 30 | 0 | 11 | 19 | 0% (0 of 11) |
| | **all** | 50 | 32 | 0 | 13 | 19 | **0% (0 of 13)** |
| **Migrations** | **all** | 411 | 145 | 1 | 68 | 76 | **1.4% (1 of 69)** |

Divergent precision on migrations is **1.4% (1 of 69)**, from 3.8% (2 of 52) in P2-047's audit, which
also held one agent pair. If every undetermined result were real it would be at most 53.1% (77 of 145).
The one confirmed result is P2-047's.

Undetermined, by obstacle (76): 46 real difference by hand trace, not executed; 10 depend on where the
code runs (process environment, host OS, hardware, or `SystemIOLinux` wrappers Duplicati selects only on
POSIX hosts); 4 the device-path decision above; 4 depend on third-party code that is not in the
checkout (RestSharp, ExCSS, FasterHashing); 5 no differing ICU/NLS input found; 4 real by construction
(2 `GetHashCode`, 1 a getter whose return type the pull request changed, 1 the runtime's own version);
1 the build-generated commit constant in `BugReporter.Program::Main()`; 1 a documented case
unreachable with an empty key unchecked; 1 kept from P2-047 with its obstacle.

Real behaviour changes the traces found, each reported Divergent and none executed, so undetermined:
- `duplicati-3124`, `Duplicati.Library.Backend.S3IAM::CreateUnprivilegedUser(string,string,string)`:
  the pull request makes a policy call asynchronous and drops its task, so a failure to attach the policy
  goes unnoticed and the access key can be created first. A bug the migration introduced.
- `gitextensions-8522`, `GitExtensions.Plugins.Gource.GourceStart::linkLabel1_LinkClicked(...)`, in a
  byte-identical file: starting a URL without the shell throws on .NET 5 where .NET Framework opened the
  browser (EQ006, the `UseShellExecute` row). A regression the migration did not fix.
- `gitextensions-8522`: seven EasyHook and theming bodies whose native or reflection calls the modern
  side compiles out behind a symbol nothing defines (the pattern P2-072 found).

### False-positive causes

| Ticket | Cause | Rule | Git Extensions | Duplicati | OpenRA | All |
|---|---|---|---|---|---|---|
| P2-188 (new) | A path row fires on a valid path that reaches the call through a solution member, a field, a caller or a validating member; P2-074 left these routes | EQ006 | 5 (+3 of P2-047's) | 7 | 1 | 13 (+3) |
| P2-190 (new) | Six rows match calls their change does not affect (ICU on `IndexOf(char)`, `OSVersion` thresholds and `.Platform`, `Ceiling` to `int`, `SetValue` on instance fields or behind a Mono guard, UTF-8 rows on `Encoding.ASCII`, `Process.Start` with `UseShellExecute` set); P2-075 left them | EQ006 | 5 | 2 | 6 | 13 |
| P2-189 (new) | The `Split` TrimEntries, `GetFolderPath` and `DataFormats.GetFormat` rows have no precondition and fire on constant arguments that cannot reach the change; P2-073 left them | EQ006 | 6 | 2 | 3 | 11 |
| P2-192 (new) | A callee replaced one for one by another member that does the same, which no catalogue entry can say: a solution-local `CallContext` shim for the remoting one, a third-party hashing factory for `HashAlgorithm.Create`, a synchronous SDK call for its `Async` twin and `GetResult` | EQ002 | 0 | 11 | 0 | 11 |
| P2-184 (new) | A call an API-equivalence entry rewrites is never closed, so it writes the heap on the legacy side only (`IrLowerer`'s rewritten callee has `Closed: false`); P2-070 left it | EQ002 | 6 | 1 | 2 | 9 |
| P2-193 (new) | The static ACL members against the `FileSystemAclExtensions` call on a new `FileInfo` that replaces them | EQ002 | 0 | 4 | 0 | 4 |
| P2-185 (new) | An unchanged call whose return type a package upgrade retyped gives two values; P2-069 left it | EQ002 | 2 | 0 | 0 | 2 |
| P2-186 (new) | An allocation-only constructor outside P2-071's catalogue (`SynchronizedCollection<T>`, a third-party parser) | EQ002 | 2 | 0 | 0 | 2 |
| P2-187 (new) | A field renamed with the same initializer is two unrelated heap inputs | EQ002 | 1 | 0 | 0 | 1 |
| P2-097 (open) | A call to a renamed private helper | EQ002 | 1 | 0 | 0 | 1 |
| P2-191 (new) | An unhosted library a host loads at run time is judged against every runtime (OpenRA's four mod projects, "between net40 and net10.0"), so a net8.0 row fires on a net5.0 pair | EQ006 | 0 | 0 | 1 | 1 |
| **All** | | | **28** | **27** | **13** | **68** |

The three largest causes (37 of 68) are EQ006 rows firing where their change cannot be reached: the
work P2-073 to P2-075 started, with the routes and rows they left. The EQ002 causes are, as in P2-047's
audit, one call given two meanings: by a rewrite's heap effect, a retyped return, a replacement member.

## The milestone against the runs (criterion 5)

What each ticket on the `Depends on` line claimed, and what these runs and P2-124's and P2-110's show.
Claims about false positives are P2-047's, P2-066's and P2-058's adjudications; claims about changed
pairs are shares of a pair's changed pairs.

| Ticket | Its Goal claimed | What the runs show |
|---|---|---|
| P2-073 | 15 of P2-047's false EQ006 (8 on Git Extensions): constant arguments that cannot reach a row's change | The 8 Git Extensions procedures: 7 congruent, 1 Unknown, none Divergent. Rows it did not cover: 11 new false positives (P2-189) |
| P2-074 | 7 false EQ006 (5 on Git Extensions): paths from a source that yields only valid paths | 2 congruent, 3 still EQ006 through a field or a solution member it does not follow; 13 new ones on the same routes (P2-188) |
| P2-075 | 2 false EQ006: rows matching members their change does not touch | Both congruent; 13 new ones on six other rows (P2-190) |
| P2-103 | All 5 of PowerShell's false EQ002 (cleanup) | P2-110: PowerShell has 0 Divergent, and 7 of its 12 edited pairs are proved |
| P2-117 | 2 of the upgrades' false EQ002 | P2-124: both proved Equivalent |
| P2-118 | 4 of the upgrades' false EQ002, in generated regex code | P2-124: no longer procedures |
| P2-122 | `switch-pattern` alone in 6.9% of Git Extensions' changed pairs and 6.8% of `gitextensions-9860`'s | Alone in 4 of 938 (0.4%) here, in 7 Unknowns; under 1% on `gitextensions-9860` (P2-124) |
| P2-087 | `Binary` alone in 6.2% of OpenRA's changed pairs, 4.0% of Duplicati's, 3.4% of Git Extensions' | 3.1% (10 of 321), 1.4% (10 of 707), 0.9% (8 of 938) |
| P2-095 | 13 of `gitextensions-9860`'s 16 `Conversion` pairs | P2-124: `Conversion` alone in 5 of 605. Here alone in 9 of 938 and 9 of 707 |
| P2-093, P2-104 | Pattern forms behind cleanup samples | P2-110: the cleanup pairs prove 297 of 509 changed pairs, from 57 |
| P1-028 | Count the unowned opaque tail; filed P2-136 to P2-141 | All six done. Still at or above 5% of a pair's changed pairs: `rebound-call` on Git Extensions (12.9% alone; P2-179), `CaughtException` on Duplicati (5.2%; P2-180), `DelegateCreation` (9.7%) and `ArrayElementReference` (5.9%) on OpenRA (P2-182, P2-183) |
| P2-127 | The one known false-Equivalent path (a local function's body) | Closed; these runs adjudicate no Equivalent, so no measure. No false Equivalent was met in the traces |
| P2-077 | The run-time row: verify pairs in parallel | 413 s, 547 s and 292 s at `--jobs 4`, where the last recorded one-at-a-time runs took 2h05m, 3h26m and 1h10m |
| P1-032 | The scoreboard states what a default run gives | Every run records `run.properties.mode` quick |
| P2-124 | The upgrades' 126 false positives, 120 of them runtime rows with no change point; 353 unedited Jellyfin bodies | P2-124: Divergent 173 to 15 (6 confirmed, 9 false), EQ006 161 to 4; 334 of the 353 bodies congruent |
| P2-110 | The cleanups' 52 Divergent, 45 of them build-location constants; every run exit 5 | P2-110: 2 Divergent, both the commit constant; no crash |
| P2-068 to P2-071 (criterion 3) | 26 of P2-047's 50 false EQ002: forwarders, rebound calls, overloads, effect-free calls | Of the 25 on Git Extensions: 10 congruent, 1 proved, 10 Unknown, 4 still EQ002 with a cause the fix left (P2-184 to P2-186) |

Against the two weak rows: on the migrations the milestone moved Divergent from 269 to 195 on Git
Extensions and gave Duplicati and OpenRA verdicts, but precision did not rise, because the samples now
reach causes the first audit never drew. The Unknown share of changed pairs over the five large pairs
moves from 57.8% to 58.4%: the two pairs that join have 49.8% and 83.2%.

## Findings (criterion 6)

No run crashed. 5% of the changed pairs is 47 on `gitextensions-8522`, 35 on `duplicati-3124` and 16
on `openra-17989`. "Alone" is ADR 0028's measure: the changed pairs whose reason set is that one reason.

| Ticket | Finding | Pairs |
|---|---|---|
| P2-181 (L, new) | Unedited bodies a runtime rule keeps out of congruence: 1,004 of 1,966 changed pairs, 5 proved, 680 Unknown, 319 EQ006 | all three |
| P2-179 (M, new) | `rebound-call` alone in 121 of 938 changed pairs (12.9%), in 108 Unknowns: calls rebound between two versions of FluentAssertions, System.IO.Abstractions and NSubstitute; no open owner since P2-137 | gitextensions-8522 |
| P2-180 (M, new) | `CaughtException` alone in 37 of 707 (5.2%), in 45 Unknowns; no open owner since P2-140 | duplicati-3124 |
| P2-182 (M, new) | `DelegateCreation` alone in 31 of 321 (9.7%); no open owner since P2-136 | openra-17989 |
| P2-183 (M, new) | `ArrayElementReference` alone in 19 of 321 (5.9%); no owner | openra-17989 |
| P2-165, existing and open | `no-body` alone in 51 of 938 (5.4%): unedited `extern` methods on a pair that crosses a runtime | gitextensions-8522 |
| P2-184 to P2-193, new | The eleven false-positive causes above (P2-097 owns the twelfth) | all three |

Below 5% and not filed: on `gitextensions-8522` `LocalFunction` alone in 37 (3.9%; P2-178 owns one form)
and `DelegateCreation` in 27 (2.9%); on `duplicati-3124` `iterator` 21 (3.0%) and `no-body` 19 (2.7%;
P2-165); on `openra-17989` `CaughtException` 14 (4.4%). `timeout` is 104, 124 and 114 Unknowns (11.1%,
17.5% and 35.5% of the changed pairs), and `abstraction` 252, 92 and 99; neither is an opaque reason or a
false-positive cause, and the `runtime-rule` count above is most of both (P2-181).

## Scoreboard (criterion 7)

`equiv-scoreboard` was applied over the five large pairs with a summary on `main`: these three runs and
P2-124's two (`docs/runs/2026-10-12-full-gitextensions-9860/SUMMARY.md`,
`docs/runs/2026-10-12-full-jellyfin-13023/SUMMARY.md`). Rows rewritten: the header (5 pairs, 58,477
matched), Proved Equivalent, Equivalent by congruence, Changed pairs the solver proves, Unknown,
Divergent (its changed-pair share), Divergent precision (migration), Left for a reviewer, Run time.
Rows left alone: Soundness, Large pairs that run to a result (5 of 5 already), Repeatability. The
sentences and tables under the scoreboard changed only where one of those numbers is in them.

| Row | Was | Now |
|---|---|---|
| Header | 3 large real pairs, 42,063 matched procedure pairs | 5 large real pairs, 58,477 matched procedure pairs |
| Proved Equivalent | 97.1% (40,833 of 42,063) | 96.6% (56,479 of 58,477) |
| Equivalent by congruence | 96.1% (40,426 of 42,063) | 95.4% (55,784 of 58,477) |
| Changed pairs the solver proves | 24.9% (407 of 1,637) | 25.8% (695 of 2,693) |
| Unknown | 2.2% of pairs (946), 57.8% of changed pairs | 2.7% of pairs (1,572), 58.4% of changed pairs |
| Divergent | 0.7% of pairs (284), 17.3% of changed pairs | 0.7% of pairs (426), 15.8% of changed pairs |
| Divergent precision, migration | 3.8% (2 of 52) | 1.4% (1 of 69) |
| Left for a reviewer | 2.9% of pairs (1,230) | 3.4% of pairs (1,998) |
| Run time | mean 4 min (3 min to 6 min) | mean 6 min (3 min to 9 min) |

Worse: proved Equivalent, congruence, the Unknown share, Left for a reviewer, run time and migration
precision. Each is the two pairs that join (Duplicati's congruent share is 88.8%, OpenRA's solver
proves 1.2%, and Duplicati's run takes 9 minutes) or the wider sample. On `gitextensions-8522` alone,
proved Equivalent (93.6% to 94.1%), the Unknown share of changed pairs (65.5% to 64.3%), Divergent (269
to 195) and Left for a reviewer (865 to 798) improved; 28 fewer pairs are congruent and the run took
413 s against 364 s. The tickets that own the causes are above.

## Next milestone (criterion 8)

Ranked by what each moves on the Unknown and Divergent precision rows; `docs/ROADMAP.md` holds the
list:
1. P2-181: 680 of the 1,222 migration Unknowns and 319 of their 411 Divergent.
2. P2-188, P2-190, P2-189: 37 of the 68 false positives, all EQ006, the rows' reach.
3. P2-184 (S): 9 false EQ002 on all three pairs, one line of the lowering.
4. P2-192 and P2-193: 15 of Duplicati's 27 false positives.
5. P2-179: Git Extensions' largest opaque reason (108 Unknowns).
6. P2-180, P2-182, P2-183, P2-165: the other opaque reasons at or above 5%.
7. P2-185, P2-186, P2-187, P2-191, P2-097: one or two false positives each.

## Method notes

- Runs: `./tools/corpus/corpus.ps1 -Compare <slug> -Mode quick -CompareArgs '--jobs', '4'`, three in a
  row from one script and one build of `e98fd1b9`, 2026-10-13 23:44 to 2026-10-14 00:05 local time.
- Counts come from the SARIF logs (`run.properties.loweringCensus`, results by rule, `proofMethod`,
  `unknownReason`, `notCongruent`, `reviewList`), read with a throwaway script under the session's
  scratch directory, and agree with `corpus.ps1 -Metrics`.
- Package changes and the "unchanged files" proxy are in each SUMMARY; neither pair has a
  `verify_command`, so no upstream test was run. `--execute` is out of scope.
- The adjudication tables with each result's one-line cause stay under `.corpus/audit/P2-130/`; the
  appendix below holds identities and classes only.

## Appendix: every adjudicated result

Today's samples, in sample order per pair. P2-047's 12 that are still Divergent are in the section
above; seven of them are also here.

| Pair | # | Rule | Procedure | Fingerprint | Classification | Ticket (false positive), basis (confirmed) or obstacle (undetermined) |
|---|---|---|---|---|---|---|
| Git Extensions | 0 | EQ002 | `` GitUI.UserControls.RevisionGrid.Graph.RevisionGraphRevision::get_StartSegments() `` | `18ddeb0fa18e` | undetermined | real by construction (the getter's return type changed), not executed |
| Git Extensions | 1 | EQ002 | `` EasyHook.LocalHook::GetProcAddress(string,string) `` | `382e6ddf0d74` | undetermined | real difference by hand trace, not executed |
| Git Extensions | 2 | EQ002 | `` CommonTestUtils.MockExecutable.MockProcess::Verify() `` | `c7220fc7467b` | undetermined | real difference by hand trace, not executed |
| Git Extensions | 3 | EQ002 | `` GitUI.CommandsDialogs.RevisionFileTreeController::SelectFileOrFolder(GitUI.UserControls.NativeTreeView,string) `` | `f3b3a94e134a` | false positive | P2-184 |
| Git Extensions | 4 | EQ002 | `` GitCommandsTests.Settings.FileSettingsCacheTests::ctor_FileWatcher_Filter_should_be_default_if_invalid_dir(string) `` | `c8de28bb25c6` | undetermined | real difference by hand trace, not executed |
| Git Extensions | 5 | EQ002 | `` EasyHook.LocalHook::Release() `` | `f98ef866387d` | confirmed | kept from P2-047 (row 18: replay `reproduced`) |
| Git Extensions | 6 | EQ002 | `` EasyHook.LocalHook::get_HookBypassAddress() `` | `d09c0d65c7bb` | undetermined | real difference by hand trace, not executed |
| Git Extensions | 7 | EQ002 | `` GitUITests.Editor.Diff.LinePrefixHelperFixture::GetSegmentsForDiffText(string) `` | `29f8924ab2fc` | false positive | P2-184 |
| Git Extensions | 8 | EQ002 | `` GitUI.OsShellUtil::PickFolder(System.Windows.Forms.IWin32Window,string) `` | `29f7a853882f` | undetermined | real difference by hand trace, not executed |
| Git Extensions | 9 | EQ002 | `` Git.hub.Client::setOAuth2Token(string) `` | `b02b0d38a8f0` | undetermined | depends on library code outside the checkout (RestSharp 106.11.4 on both sides), not executed |
| Git Extensions | 10 | EQ002 | `` EasyHook.HookAccessControl::SetInclusiveACL(int[]) `` | `b8563157e808` | undetermined | real difference by hand trace, not executed |
| Git Extensions | 11 | EQ002 | `` GitCommandsTests.SshPathLocatorTest::File_system_access_throwing_should_return_empty_string() `` | `2af4a371a664` | false positive | P2-185 |
| Git Extensions | 12 | EQ002 | `` GitCommands.EnvironmentPathsProvider::IsValidPath(string) `` | `1c2650b415c5` | undetermined | real difference by hand trace, not executed |
| Git Extensions | 13 | EQ002 | `` GitUI.Theming.ThemeLoader::.ctor(GitUI.Theming.IThemeCssUrlResolver,GitUI.Theming.IThemeFileReader) `` | `9500d96f3927` | false positive | P2-186 |
| Git Extensions | 14 | EQ002 | `` EasyHook.HookAccessControl::SetExclusiveACL(int[]) `` | `e82d05c7ff8d` | undetermined | real difference by hand trace, not executed |
| Git Extensions | 15 | EQ002 | `` GitUI.Theming.ThemeLoader::StyleRuleThemeException(ExCSS.StyleRule,string) `` | `007c0477d601` | undetermined | depends on library code outside the checkout (ExCSS 2.0.6 against 4.1.0), not executed |
| Git Extensions | 16 | EQ002 | `` GitUI.CommandsDialogs.WorktreeDialog.FormManageWorktree::IsCurrentlyOpenedWorktree(GitUI.CommandsDialogs.WorktreeDialog.FormManageWorktree.WorkTree) `` | `eda42310a48b` | false positive | P2-184 |
| Git Extensions | 17 | EQ002 | `` GitUI.Theming.ThemeModule::ResetGdiCaches() `` | `01889bee21ab` | undetermined | real difference by hand trace, not executed |
| Git Extensions | 18 | EQ002 | `` GitUIPluginInterfaces.ManagedExtensibility::TryLoadAssembly(System.IO.FileInfo) `` | `ac56e0e65b8f` | undetermined | real difference by hand trace, not executed |
| Git Extensions | 19 | EQ002 | `` GitUI.UserControls.RevisionGrid.Graph.RevisionGraphRevision::.ctor(GitUIPluginInterfaces.ObjectId,int) `` | `4bc2b0def908` | false positive | P2-186 |
| Git Extensions | 20 | EQ002 | `` GitUI.CommitInfo.CommitInfo.BranchComparer::Compare(string,string) `` | `a3a728141a9f` | undetermined | kept from P2-047 (row 21: real difference by hand trace, not executed: non-public) |
| Git Extensions | 21 | EQ002 | `` GitExtensions.UITests.Script.ScriptRunnerTests::Setup() `` | `2c47f64c4e81` | false positive | P2-187 |
| Git Extensions | 22 | EQ002 | `` GitUI.Avatars.GithubAvatarProvider::GetAvatarAsync(string,string,int) `` | `4162ceb4d1b9` | false positive | P2-097 |
| Git Extensions | 23 | EQ002 | `` BugReporter.Program::Main() `` | `aa53103de73e` | undetermined | real by construction, not the migration (build-generated commit constant) |
| Git Extensions | 24 | EQ002 | `` ICSharpCode.TextEditor.Document.FoldingManager::DeserializeFromString(string) `` | `89c45add3fdc` | false positive | P2-184 |
| Git Extensions | 25 | EQ002 | `` EasyHook.LocalHook::IsThreadIntercepted(int) `` | `a3a8958bc604` | undetermined | real difference by hand trace, not executed |
| Git Extensions | 26 | EQ002 | `` GitUITests.CommandsDialogs.RevisionFileTreeControllerTests::PopulateTreeView(GitUI.UserControls.NativeTreeView,string) `` | `794fec94ab50` | false positive | P2-184 |
| Git Extensions | 27 | EQ002 | `` GitUI.FileStatusList::IsFilterMatch(GitCommands.GitItemStatus) `` | `163b56fec8bb` | false positive | P2-184 |
| Git Extensions | 28 | EQ002 | `` GitCommandsTests.SshPathLocatorTest::Find_on_gitBinDir_parent_throwing_should_return_empty_string() `` | `4685d00903ca` | false positive | P2-185 |
| Git Extensions | 29 | EQ006 | `` TranslationApp.Program::Main() `` | `715f54069585` | false positive | P2-188 |
| Git Extensions | 30 | EQ006 | `` Git.hub.User::GetHashCode() `` | `403cfd967c2b` | undetermined | real by construction (hash differs per runtime), not executed |
| Git Extensions | 31 | EQ006 | `` GitCommands.EncodingHelper::GetString(byte[],byte[],System.Text.Encoding) `` | `f581e8b0f7b1` | undetermined | real difference by hand trace, not executed |
| Git Extensions | 32 | EQ006 | `` GitCommands.Utils.EnvUtils::IsWindowsVistaOrGreater() `` | `4f48b369846a` | false positive | P2-190 |
| Git Extensions | 33 | EQ006 | `` ICSharpCode.TextEditor.TextView::GetFontHeight(System.Drawing.Font) `` | `7641b0722a90` | false positive | P2-190 |
| Git Extensions | 34 | EQ006 | `` GitCommands.Utils.EnvUtils::IsWindows7OrGreater() `` | `5ff2f6a9663d` | false positive | P2-190 |
| Git Extensions | 35 | EQ006 | `` GitUI.CommandsDialogs.GitIgnoreDialog.GitLocalExcludeModel::get_ExcludeFile() `` | `79a9e320002e` | undetermined | real difference by hand trace, not executed |
| Git Extensions | 36 | EQ006 | `` GitExtUtilsTests.LazyStringSplitTests::None(string,char,string[]) `` | `6e8fcb676173` | false positive | P2-189 |
| Git Extensions | 37 | EQ006 | `` NetSpell.SpellChecker.Dictionary.WordDictionary::VerifyAffixKey(string,char) `` | `0532c111bc10` | false positive | P2-190 |
| Git Extensions | 38 | EQ006 | `` GitCommands.FileAssociatedIconProvider::CreateTempFile(string) `` | `3981846aafb5` | false positive | P2-188 |
| Git Extensions | 39 | EQ006 | `` GitCommands.Settings.ConfigFileSettings::CreateSystemWide(bool) `` | `e0b0b1464bb4` | false positive | P2-189 |
| Git Extensions | 40 | EQ006 | `` GitCommands.ExternalLinks.ExternalLink::GetHashCode() `` | `b49a928f213e` | undetermined | real by construction (hash differs per runtime), not executed |
| Git Extensions | 41 | EQ006 | `` GitUI.Editor.Diff.DiffLineNumAnalyzer::IsMinusLineInCombinedDiff(string) `` | `a0842741b145` | undetermined | no differing ICU/NLS input found |
| Git Extensions | 42 | EQ006 | `` GitUI.SpellChecker.EditNetSpell::PasteTextFromClipboard() `` | `7496266de932` | false positive | P2-189 |
| Git Extensions | 43 | EQ006 | `` CommonTestUtils.GitModuleTestHelper::Dispose() `` | `4beef9df5ccb` | false positive | P2-188 |
| Git Extensions | 44 | EQ006 | `` GitCommands.Utils.EnvUtils::IsWindows8Point1OrGreater() `` | `554e7dab9afb` | undetermined | depends on process environment (whether the host executable's manifest declares Windows 8.1) |
| Git Extensions | 45 | EQ006 | `` GitUI.AutoCompletion.CommitAutoCompleteProvider::ReadOrInitializeAutoCompleteRegexes() `` | `f52ccbfc10a2` | false positive | P2-189 |
| Git Extensions | 46 | EQ006 | `` GitCommands.AppSettings::GetDictionaryDir() `` | `467ae51d6815` | undetermined | depends on process environment (the `InstallDir` registry value) |
| Git Extensions | 47 | EQ006 | `` GitExtensions.Plugins.Gource.GourceStart::linkLabel1_LinkClicked(object,System.Windows.Forms.LinkLabelLinkClickedEventArgs) `` | `172ab08d40f6` | undetermined | real difference by hand trace, not executed |
| Git Extensions | 48 | EQ006 | `` ICSharpCode.TextEditor.Document.DocumentFactory::CreateFromFile(string) `` | `b93c4926a47f` | undetermined | real difference by hand trace, not executed |
| Git Extensions | 49 | EQ006 | `` GitUI.NBugReports.BugReportInvoker::Report(System.Exception,bool) `` | `230e24cf7a05` | undetermined | depends on process current directory |
| Git Extensions | 50 | EQ006 | `` GitUI.CommandsDialogs.SettingsDialog.Pages.GitConfigSettingsPageController::GetInitialDirectory(string,string) `` | `7e35c6fefc5c` | false positive | P2-189 |
| Git Extensions | 51 | EQ006 | `` GitUITests.CommandsDialogs.FormFileHistoryControllerTests::TryGetExactPathName_should_check_if_path_matches_case(string,bool,bool) `` | `3cf4b8f809df` | false positive | P2-188 |
| Git Extensions | 52 | EQ006 | `` GitUI.SpellChecker.EditNetSpell::GetCursorPosition() `` | `49971b32dce2` | false positive | P2-190 |
| Git Extensions | 53 | EQ006 | `` GitUI.Theming.ThemeFileReader::ReadThemeFile(string) `` | `f7941fa87a91` | undetermined | real difference by hand trace, not executed |
| Git Extensions | 54 | EQ006 | `` ICSharpCode.TextEditor.Document.FontContainer::ParseFont(string) `` | `30b84d1aab93` | undetermined | kept from P2-047 (row 50: real difference by hand trace, not executed: no legacy build) |
| Git Extensions | 55 | EQ006 | `` GitUI.CommandsDialogs.FormResolveConflicts::Mergetool_Click(object,System.EventArgs) `` | `7faa6205a4e0` | false positive | P2-188 |
| Git Extensions | 56 | EQ006 | `` GitCommands.Plink::GetPlinkCompatibleUrl(string) `` | `788e60d3450e` | undetermined | no differing ICU/NLS input found |
| Git Extensions | 57 | EQ006 | `` GitCommands.PathUtil::GetDisplayPath(string) `` | `65b818acad93` | false positive | P2-189 |
| Git Extensions | 58 | EQ006 | `` TeamCityIntegration.Settings.TeamCityBuildChooser::ReselectPreviouslySelectedBuild() `` | `41a126cfc902` | undetermined | documented case unreachable; an empty key not checked |
| Duplicati | 0 | EQ002 | `` Duplicati.Library.Localization.LocalizationContext::Dispose() `` | `21a0359b0fd3` | false positive | P2-192 |
| Duplicati | 1 | EQ002 | `` Duplicati.Library.Common.IO.SystemIOWindows::SetAccessControlDir(string,System.Security.AccessControl.DirectorySecurity) `` | `f49c7b66807b` | false positive | P2-193 |
| Duplicati | 2 | EQ002 | `` Duplicati.Library.Utility.Utility::CalculateHash(System.IO.Stream) `` | `ad97a796d61d` | false positive | P2-192 |
| Duplicati | 3 | EQ002 | `` Duplicati.Library.Localization.LocalizationContext::.ctor(System.Globalization.CultureInfo) `` | `441031e80442` | false positive | P2-192 |
| Duplicati | 4 | EQ002 | `` Duplicati.Library.Common.IO.SystemIOWindows::GetAccessControlFile(string) `` | `214d7b9a4ecc` | false positive | P2-193 |
| Duplicati | 5 | EQ002 | `` Duplicati.Library.AutoUpdater.UpdaterManager::RunFromMostRecent(System.Reflection.MethodInfo,string[],Duplicati.Library.AutoUpdater.AutoUpdateStrategy) `` | `1f66711aa158` | undetermined | real difference by hand trace, not executed; depends on process environment |
| Duplicati | 6 | EQ002 | `` Duplicati.Library.Common.IO.SystemIOWindows::GetAccessControlDir(string) `` | `e4cbfee396b9` | false positive | P2-193 |
| Duplicati | 7 | EQ002 | `` Duplicati.Library.Main.BackendManager::CalculateFileHash(System.IO.Stream) `` | `dd3ceb27877a` | false positive | P2-192 |
| Duplicati | 8 | EQ002 | `` Duplicati.Library.Utility.CallContextSettings`1.ContextGuard::Dispose() `` | `09e4350d4bbe` | false positive | P2-192 |
| Duplicati | 9 | EQ002 | `` Duplicati.Library.Backend.AzureBlob.AzureBlobWrapper::AddFileStream(string,System.IO.Stream,System.Threading.CancellationToken) `` | `ab1d17bf7233` | undetermined | real difference by hand trace, not executed |
| Duplicati | 10 | EQ002 | `` Duplicati.Library.Common.IO.SystemIOWindows::SetAccessControlFile(string,System.Security.AccessControl.FileSecurity) `` | `d072f53919bd` | false positive | P2-193 |
| Duplicati | 11 | EQ002 | `` Duplicati.Library.Utility.CallContextSettings`1::get_ContextID() `` | `c258e2f1548b` | false positive | P2-192 |
| Duplicati | 12 | EQ002 | `` Duplicati.Library.Utility.HashCalculatingStream::.ctor(System.IO.Stream,string) `` | `04d82279cac7` | undetermined | depends on library code outside the checkout (FasterHashing 1.3.0, binary only) for a hash name it does not support |
| Duplicati | 13 | EQ002 | `` Duplicati.Library.Utility.CallContextSettings`1.ContextGuard::.ctor() `` | `4e6e4b769560` | false positive | P2-192 |
| Duplicati | 14 | EQ002 | `` Duplicati.Library.Localization.LocalizationService::get_Current() `` | `2887e830b600` | false positive | P2-192 |
| Duplicati | 15 | EQ002 | `` Duplicati.Library.Utility.Power.WindowsPowerSupplyState::GetSource() `` | `66d2f65fb32f` | undetermined | real difference by hand trace, not executed; depends on the machine's power hardware |
| Duplicati | 16 | EQ002 | `` Duplicati.Library.Main.BackendManager::GetFileHasherStream(System.IO.Stream,System.Security.Cryptography.CryptoStreamMode,out System.Func<string>) `` | `4a3cd6feac45` | false positive | P2-192 |
| Duplicati | 17 | EQ002 | `` Duplicati.Library.Utility.UrlPath::.ctor(string) `` | `5fe54034f91a` | false positive | P2-184 |
| Duplicati | 18 | EQ002 | `` Duplicati.GUI.TrayIcon.Program::GetWinformsInstance() `` | `b9882e379cf8` | undetermined | real difference by hand trace, not executed: UI |
| Duplicati | 19 | EQ002 | `` Duplicati.Library.Backend.S3IAM::CreateUnprivilegedUser(string,string,string) `` | `6d8d81324e0e` | undetermined | real difference by hand trace, not executed |
| Duplicati | 20 | EQ002 | `` Duplicati.Library.Backend.S3IAM::CanCreateUser(string,string) `` | `b691923f95ef` | false positive | P2-192 |
| Duplicati | 21 | EQ002 | `` Duplicati.GUI.TrayIcon.Program::TryGetWinforms() `` | `6b406f3a2ae3` | undetermined | real difference by hand trace on a POSIX host (legacy under Mono), not executed; depends on host OS |
| Duplicati | 22 | EQ002 | `` Duplicati.Library.Main.Utility.Metahash::.ctor(System.Collections.Generic.Dictionary<string, string>,Duplicati.Library.Main.Options) `` | `1485309eb88d` | undetermined | depends on library code outside the checkout (FasterHashing 1.3.0, binary only) for a hash name it does not support |
| Duplicati | 23 | EQ002 | `` Duplicati.Library.Logging.Log::set_CurrentScope(Duplicati.Library.Logging.LogScope) `` | `610683f9ca1a` | false positive | P2-192 |
| Duplicati | 24 | EQ006 | `` Duplicati.Library.Backend.Backblaze.B2::PutAsync(string,string,System.Threading.CancellationToken) `` | `96e026a2a716` | undetermined | real difference by hand trace, not executed |
| Duplicati | 25 | EQ006 | `` Duplicati.Library.Common.IO.SystemIOLinux::PathChangeExtension(string,string) `` | `7c1ceab6d9b8` | undetermined | real difference by hand trace on a .NET Framework host, not executed; depends on host OS (Duplicati selects `SystemIOLinux` on POSIX hosts only) |
| Duplicati | 26 | EQ006 | `` Duplicati.Library.Backend.OpenStack.OpenStackStorage::PutAsync(string,string,System.Threading.CancellationToken) `` | `7d57ee69312e` | undetermined | real difference by hand trace, not executed |
| Duplicati | 27 | EQ006 | `` Duplicati.Library.Common.IO.SystemIOWindows::DirectoryDelete(string,bool) `` | `15458f8c5b8b` | undetermined | depends on .NET Framework device-path handling, not checked |
| Duplicati | 28 | EQ006 | `` Duplicati.Library.Main.Operation.Common.BackendHandler.FileEntryItem::CalculateFileHash(string) `` | `d626feb47703` | undetermined | real difference by hand trace, not executed |
| Duplicati | 29 | EQ006 | `` Duplicati.Library.Backend.Jottacloud::Get(string,string) `` | `d00d06277f90` | undetermined | real difference by hand trace, not executed |
| Duplicati | 30 | EQ006 | `` Duplicati.Server.WebServer.IndexHtmlHandler::Process(HttpServer.IHttpRequest,HttpServer.IHttpResponse,HttpServer.Sessions.IHttpSession) `` | `b7f6ced8642e` | false positive | P2-188 |
| Duplicati | 31 | EQ006 | `` Duplicati.Library.Common.IO.SystemIOLinux::GetPathRoot(string) `` | `b7e5659e1f33` | undetermined | real difference by hand trace on a .NET Framework host, not executed; depends on host OS (Duplicati selects `SystemIOLinux` on POSIX hosts only) |
| Duplicati | 32 | EQ006 | `` Duplicati.Library.Backend.Rclone::Delete(string) `` | `2830cc7336f9` | undetermined | real difference by hand trace, not executed |
| Duplicati | 33 | EQ006 | `` Duplicati.Library.Backend.GoogleDrive.GoogleDrive::GetFolderId(string,bool) `` | `bca89907f1fb` | false positive | P2-189 |
| Duplicati | 34 | EQ006 | `` Duplicati.Library.Common.IO.SystemIOWindows::FileGetLastWriteTimeUtc(string) `` | `8ad8fba2a989` | undetermined | depends on .NET Framework device-path handling, not checked |
| Duplicati | 35 | EQ006 | `` Duplicati.Library.Common.IO.SystemIOLinux::DirectoryDelete(string,bool) `` | `72bddd618976` | false positive | P2-188 |
| Duplicati | 36 | EQ006 | `` Duplicati.Library.Snapshots.LinuxSnapshot::GetCreationTimeUtc(string) `` | `746c6dab6b10` | false positive | P2-188 |
| Duplicati | 37 | EQ006 | `` Duplicati.Library.Common.IO.SystemIOLinux::FileCreate(string) `` | `10d4f9d86673` | undetermined | real difference by hand trace on a .NET Framework host, not executed; depends on host OS (Duplicati selects `SystemIOLinux` on POSIX hosts only) |
| Duplicati | 38 | EQ006 | `` Duplicati.Library.Backend.Box.BoxBackend::GetCurrentFolder(bool) `` | `9438437f7cb1` | false positive | P2-189 |
| Duplicati | 39 | EQ006 | `` Duplicati.Library.Common.IO.SystemIOLinux::FileOpenRead(string) `` | `295849fe4794` | undetermined | real difference by hand trace on a .NET Framework host, not executed; depends on host OS (Duplicati selects `SystemIOLinux` on POSIX hosts only) |
| Duplicati | 40 | EQ006 | `` Duplicati.Library.Backend.SharePointBackend::uploadFileSlicePerSlice(Microsoft.SharePoint.Client.ClientContext,Microsoft.SharePoint.Client.Folder,System.IO.Stream,string,System.Threading.CancellationToken) `` | `9b56c032efc5` | undetermined | real difference by hand trace, not executed: non-public |
| Duplicati | 41 | EQ006 | `` Duplicati.Library.Common.IO.SystemIOLinux::DirectoryCreate(string) `` | `487691a23b74` | false positive | P2-188 |
| Duplicati | 42 | EQ006 | `` Duplicati.Library.Snapshots.SnapshotBase::GetLastWriteTimeUtc(string) `` | `d0b3898e3093` | undetermined | real difference by hand trace, not executed |
| Duplicati | 43 | EQ006 | `` Duplicati.CommandLine.RecoveryTool.Index::WriteIndex(ref System.Collections.Generic.SortedSet<string>,string) `` | `252ab5476f09` | false positive | P2-188 |
| Duplicati | 44 | EQ006 | `` Duplicati.Library.Snapshots.LinuxSnapshot::GetLastWriteTimeUtc(string) `` | `83b40cf8c763` | false positive | P2-188 |
| Duplicati | 45 | EQ006 | `` Duplicati.Library.Backend.GoogleDrive.GoogleDrive::PutAsync(string,string,System.Threading.CancellationToken) `` | `0c4d32f89262` | undetermined | real difference by hand trace, not executed |
| Duplicati | 46 | EQ006 | `` Duplicati.Library.Backend.Box.BoxBackend::Get(string,string) `` | `a5aece306f11` | undetermined | real difference by hand trace, not executed |
| Duplicati | 47 | EQ006 | `` Duplicati.Library.Common.IO.SystemIOWindows::GetPathRoot(string) `` | `eb5f1347c6f3` | undetermined | depends on .NET Framework device-path handling, not checked |
| Duplicati | 48 | EQ006 | `` Duplicati.Library.UsageReporter.OSInfoHelper::RunProgramAndReadOutput(string,string) `` | `5d0e9faf8c40` | false positive | P2-190 |
| Duplicati | 49 | EQ006 | `` Duplicati.Library.Main.BackendManager::Dispose() `` | `e7e0a67994aa` | undetermined | real difference by hand trace, not executed |
| Duplicati | 50 | EQ006 | `` Duplicati.Library.Compression.FileArchiveDirectory::OpenRead(string) `` | `db85392854c2` | undetermined | real difference by hand trace, not executed |
| Duplicati | 51 | EQ006 | `` Duplicati.Library.OAuthHttpClient::.cctor() `` | `29e67cd447b2` | false positive | P2-190 |
| Duplicati | 52 | EQ006 | `` Duplicati.Library.Common.IO.SystemIOLinux::GetFileAttributes(string) `` | `f92ac3094f06` | false positive | P2-188 |
| Duplicati | 53 | EQ006 | `` Duplicati.Library.Common.IO.SystemIOWindows::PathChangeExtension(string,string) `` | `7be87aa9263b` | undetermined | depends on .NET Framework device-path handling, not checked |
| OpenRA | 0 | EQ002 | `` OpenRA.Mods.Common.Server.LobbyCommands::InterpretCommand(OpenRA.Server.Server,OpenRA.Server.Connection,OpenRA.Network.Session.Client,string) `` | `5894c09082b2` | false positive | P2-184 |
| OpenRA | 1 | EQ002 | `` OpenRA.Mods.Common.LoadScreens.LogoStripeLoadScreen::Init(OpenRA.ModData,System.Collections.Generic.Dictionary<string, string>) `` | `f5e5ed6c9a1f` | false positive | P2-184 |
| OpenRA | 2 | EQ006 | `` OpenRA.Mods.D2k.UtilityCommands.D2kMapImporter::.ctor(string,string,OpenRA.Ruleset) `` | `e96d149c45e7` | undetermined | real difference by hand trace, not executed; non-public |
| OpenRA | 3 | EQ006 | `` OpenRA.MiniYaml::FromString(string,string,bool,System.Collections.Generic.Dictionary<string, string>) `` | `ff3438aad40a` | false positive | P2-189 |
| OpenRA | 4 | EQ006 | `` OpenRA.FileSystem.Folder::OpenPackage(string,OpenRA.FileSystem.FileSystem) `` | `9711a14506e4` | undetermined | real difference by hand trace, not executed |
| OpenRA | 5 | EQ006 | `` OpenRA.ValueActorInit`1::Initialize(T) `` | `ce5650380240` | false positive | P2-190 |
| OpenRA | 6 | EQ006 | `` OpenRA.float2::FromAngle(float) `` | `f397fec71b1b` | undetermined | real difference by hand trace, not executed |
| OpenRA | 7 | EQ006 | `` OpenRA.Mods.Common.Widgets.ScrollPanelWidget::Scroll(int,bool) `` | `ff9f320d3d1d` | undetermined | real difference by hand trace, not executed; non-public |
| OpenRA | 8 | EQ006 | `` OpenRA.FileFormats.ReplayMetadata::RenameFile(string) `` | `cfb3dceb53a3` | false positive | P2-188 |
| OpenRA | 9 | EQ006 | `` OpenRA.Mods.Common.Traits.Harvester::get_Fullness() `` | `0e2cd54ec09f` | false positive | P2-191 |
| OpenRA | 10 | EQ006 | `` OpenRA.StreamExts::ReadString(System.IO.Stream,System.Text.Encoding,int) `` | `dc9636fd3ef4` | undetermined | real difference by hand trace, not executed |
| OpenRA | 11 | EQ006 | `` OpenRA.MiniYaml::FromFile(string,bool,System.Collections.Generic.Dictionary<string, string>) `` | `3ed9235b907f` | undetermined | real difference by hand trace, not executed |
| OpenRA | 12 | EQ006 | `` OpenRA.FileSystem.Folder::GetStream(string) `` | `5aa9fec862d2` | undetermined | real difference by hand trace, not executed |
| OpenRA | 13 | EQ006 | `` OpenRA.Mods.Common.Traits.RotationPaletteEffect::StartsWithAny(string,System.Collections.Generic.HashSet<string>) `` | `ef90ed238201` | undetermined | real difference by hand trace, not executed; non-public |
| OpenRA | 14 | EQ006 | `` OpenRA.Mods.Common.Widgets.ProductionTabsWidget::Scroll(int) `` | `162e973d805f` | undetermined | real difference by hand trace, not executed; non-public |
| OpenRA | 15 | EQ006 | `` OpenRA.Mods.Common.Traits.PlayerStatistics::ResolveOrder(OpenRA.Actor,OpenRA.Order) `` | `3bcaabd7550e` | undetermined | no differing ICU/NLS input found |
| OpenRA | 16 | EQ006 | `` OpenRA.FileSystem.Folder::Update(string,byte[]) `` | `ef8316585ec5` | undetermined | no differing ICU/NLS input found |
| OpenRA | 17 | EQ006 | `` OpenRA.Mods.Common.Widgets.Logic.GameSaveBrowserLogic::IsLoadPanelEnabled(OpenRA.Manifest) `` | `726de6bb4bfa` | undetermined | real difference by hand trace, not executed |
| OpenRA | 18 | EQ006 | `` OpenRA.FileSystem.ZipFileLoader.ReadWriteZipFile::.ctor(string,bool) `` | `69b071c8f8e6` | undetermined | real difference by hand trace, not executed; non-public type |
| OpenRA | 19 | EQ006 | `` OpenRA.Mods.Common.SubCellInit::Initialize(int) `` | `5945047356d1` | false positive | P2-190 |
| OpenRA | 20 | EQ006 | `` OpenRA.FileSystem.Folder::.ctor(string) `` | `713729276b97` | undetermined | real difference by hand trace, not executed |
| OpenRA | 21 | EQ006 | `` OpenRA.CryptoUtil::DecryptString(System.Security.Cryptography.RSAParameters,string) `` | `b31b36a6f035` | undetermined | real difference by hand trace, not executed |
| OpenRA | 22 | EQ006 | `` OpenRA.Mods.Common.Widgets.Logic.ObserverStatsLogic::AverageOrdersPerMinute(double) `` | `e0a23402b963` | undetermined | real difference by hand trace, not executed; non-public |
| OpenRA | 23 | EQ006 | `` OpenRA.Graphics.ImmutablePalette::.ctor(string,int[]) `` | `398b295eb909` | undetermined | real difference by hand trace, not executed |
| OpenRA | 24 | EQ006 | `` OpenRA.Platform::get_RuntimeVersion() `` | `699f2c2d5010` | undetermined | real by construction (reports the runtime's version), not executed |
| OpenRA | 25 | EQ006 | `` OpenRA.Mods.Common.Widgets.LabelWithHighlightWidget::MakeComponents(string) `` | `6f5caea1d66f` | false positive | P2-189 |
| OpenRA | 26 | EQ006 | `` OpenRA.FieldLoader::Load(object,OpenRA.MiniYaml) `` | `42c49f14a91e` | false positive | P2-190 |
| OpenRA | 27 | EQ006 | `` OpenRA.StreamExts::ReadASCII(System.IO.Stream,int) `` | `e02d4d2d9ed7` | false positive | P2-190 |
| OpenRA | 28 | EQ006 | `` OpenRA.Platform::GetCurrentPlatform() `` | `6be409a34dd5` | false positive | P2-190 |
| OpenRA | 29 | EQ006 | `` OpenRA.FileSystem.Folder::Delete(string) `` | `7fbd69e2d586` | undetermined | no differing ICU/NLS input found |
| OpenRA | 30 | EQ006 | `` OpenRA.Mods.Cnc.Graphics.VoxelModelCache::LoadVoxel(string,OpenRA.MiniYaml) `` | `6b5b384ca07f` | false positive | P2-189 |
| OpenRA | 31 | EQ006 | `` OpenRA.StreamExts::ReadASCIIZ(System.IO.Stream) `` | `d2a63b4110f0` | false positive | P2-190 |
