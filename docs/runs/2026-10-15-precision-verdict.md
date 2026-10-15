# P2-212 verdict: the five large pairs on one commit, adjudicated on fresh samples (2026-10-15)

`gitextensions-9860` (net5.0 to net6.0), `jellyfin-13023` (net8.0 to net9.0), `gitextensions-8522` (.NET Framework 4.8
to .NET 5), `duplicati-3124` (.NET Framework to .NET 5, first step) and `openra-17989` (Mono/net472 plus .NET 5), each
run in `full` mode, compare mode quick (`compare`'s default, ADR 0052; every summary records `run.properties.mode`
quick), `--jobs 4`, at equiv `bfbf413e` (`main`, which holds P2-164), one after the other from one build. Per-pair
detail is in `docs/runs/2026-10-15-full-<slug>/SUMMARY.md`. Nothing under `src/`, `tests/` or `tools/` was changed.

Every number here stands next to the one in the last verdict of its pair: `docs/runs/2026-10-12-upgrade-verdict.md`
(P2-124, equiv `1d767f54`) for the upgrades and `docs/runs/2026-10-14-migrations-verdict.md` (P2-130, equiv
`e98fd1b9`) for the migrations. This file replaces both as the adjudication of the five large pairs.

**These pairs take no part in ADR 0028's rule table here.** This file measures and adjudicates; it has no continue,
re-scope or stop line. `docs/runs/2026-09-30-full-verdict.md` remains the ADR 0028 verdict.

## Outcome in one line

**The fixes did what they claimed where a default run can show it: Divergent falls from 426 to 341 over the five
pairs, EQ006 from 360 to 288, and the solver proves 31.9% of the changed pairs (from 25.8%). Divergent precision is
85.7% (6 of 7) on the upgrades, from 40.0% (6 of 15), and 2.2% (1 of 46) on the migrations, from 1.4% (1 of 69).** On
the migrations the fresh samples still find 45 false positives: 9 are P2-192's results, which a default run cannot
show because the declaration P2-192 needs is not in the pair's committed config (P2-213, new); the other 36 have
fourteen causes, five of them new tickets (P2-216 to P2-220), none larger than 5 results. 90 of the 136 sampled
migration results are undetermined, 57 of them a real difference by hand trace that nothing could execute.

## How these runs differ from the last ones

| | Last verdict | These runs |
|---|---|---|
| equiv commit | `1d767f54` (upgrades), `e98fd1b9` (migrations) | `bfbf413e`, all five |
| Compare mode | quick | quick |
| Pairs verified at a time | 4 (`--jobs 4`) | 4 (`--jobs 4`), so the run-time row keeps one meaning |
| Wall-clock | 264 s, 164 s, 413 s, 547 s, 292 s | 271 s, 168 s, 363 s, 447 s, 217 s |

- `.corpus/` was made new for this ticket (`-Prepare`, `-Fetch` of the five pairs, restore of both sides of each:
  MSBuild with `RestorePackagesConfig` for the migrations' legacy sides, `dotnet restore` otherwise, `--configfile
  <this repo>/NuGet.config` for Jellyfin, P2-115 being open). Every restore succeeded.
- The runs went 2026-10-15 17:05 to 17:29 local time. Two file-reading trace agents (the adjudication below) ran
  beside the `duplicati-3124` and `openra-17989` runs; nothing else ran. Every query that ran out of budget ended on
  the resource limit and none on the wall clock (`run.properties.queryEndings`), so this decides no result.
- Decision: the fresh draw is P2-130's method with the seed the ticket's number: per rule, the results sorted by
  `resultFingerprint/v1`, `random.Random(212).shuffle`, first 30. P2-130 used 47, after P2-047; a new seed makes the
  draw fresh, and the overlap with P2-130's samples is whatever the shuffle gives.
- Decision: a result P2-124 or P2-130 adjudicated is the same procedure when its identity and rule match, not its
  fingerprint: a fingerprint moves between commits for the same procedure (P2-047's and P2-130's for
  `EasyHook.LocalHook::Release()` differ). Its cause is unchanged, and its classification kept without a new trace,
  when it was confirmed or undetermined, or a false positive whose owning ticket is still open (after the moves
  P2-200, P2-201 and P2-192 recorded: to P2-207, P2-208, P2-205 and P2-206). A false positive whose owner is done was
  traced again, since its fix was meant to move it.
- Decision: P2-192's results are the exception. The 9 in Duplicati's sample are the procedures P2-192's Notes
  traced, still EQ002 because this run passes no declaration (the SARIF has no `callIdentitiesMapped`); their cause
  is unchanged and they are counted false positives under P2-213, without a new trace. `PatchManager::ToPatch(...)` on
  `gitextensions-9860` is the result P2-211's Goal traces (P2-164 left it); it is counted under P2-211 the same way.

## Crashes (criterion 2)

None. All five runs exit 1 (Divergent results exist), with no `run.properties.unverified` entry and no tool execution
notification. Every project loads on both sides of all five pairs; no project is skipped.

## Per pair (criterion 3)

Shares are over ADR 0034's changed pairs. An Unknown that is `unmatched-overload` is not a matched pair and is left
out (20, 1, 19, 6 and 0 of them). The `runtime-rule` column is the count of changed pairs whose
`properties.notCongruent` names a runtime rule first (P2-173): their two texts are equal. P2-124's runs came before
P2-173, so they have none.

| Pair | | Matched pairs | Congruent pairs | Changed pairs | Proved Equivalent by the solver, by `proofMethod` | Unknown, by reason | EQ002 | EQ006 | `runtime-rule` |
|---|---|---|---|---|---|---|---|---|---|
| gitextensions-9860 | P2-124 | 14,073 | 13,468 (95.7%) | 605 | 300 (49.6%): bounded 283, lockstep-induction 17 | 291 (48.1%): opaque 227, timeout 37, abstraction 25, unaligned-loop 1, recursion 1 | 10 | 4 | n/a |
| | now | 14,073 | 13,540 (96.2%) | 533 | 321 (60.2%): bounded 299, lockstep-induction 22 | 206 (38.6%): opaque 158, timeout 28, abstraction 19, unaligned-loop 1 | 6 | 0 | 6 (1.1%) |
| jellyfin-13023 | P2-124 | 14,398 | 14,276 (99.2%) | 122 | 62 (50.8%): bounded 51, lockstep-induction 11 | 59 (48.4%): timeout 22, opaque 20, abstraction 10, unaligned-loop 7 | 1 | 0 | n/a |
| | now | 14,398 | 14,276 (99.2%) | 122 | 63 (51.6%): bounded 52, lockstep-induction 11 | 58 (47.5%): timeout 22, opaque 19, abstraction 10, unaligned-loop 7 | 1 | 0 | 10 (8.2%) |
| gitextensions-8522 | P2-130 | 13,592 | 12,654 (93.1%) | 938 | 140 (14.9%): bounded 126, lockstep-induction 14 | 603 (64.3%): abstraction 252, opaque 233, timeout 104, unaligned-loop 13, recursion 1 | 29 | 166 | 493 (52.6%) |
| | now | 13,592 | 12,722 (93.6%) | 870 | 154 (17.7%): bounded 136, lockstep-induction 18 | 556 (63.9%): opaque 232, abstraction 219, timeout 93, unaligned-loop 11, recursion 1 | 23 | 137 | 476 (54.7%) |
| duplicati-3124 | P2-130 | 6,294 | 5,587 (88.8%) | 707 | 189 (26.7%): bounded 162, lockstep-induction 27 | 352 (49.8%): opaque 125, timeout 124, abstraction 92, unaligned-loop 8, recursion 3 | 24 | 142 | 269 (38.0%) |
| | now | 6,294 | 5,639 (89.6%) | 655 | 199 (30.4%): bounded 167, lockstep-induction 32 | 316 (48.2%): opaque 128, timeout 115, abstraction 64, unaligned-loop 8, recursion 1 | 23 | 117 | 236 (36.0%) |
| openra-17989 | P2-130 | 10,120 | 9,799 (96.8%) | 321 | 4 (1.2%): bounded 3, lockstep-induction 1 | 267 (83.2%): timeout 114, abstraction 99, opaque 42, unaligned-loop 10, recursion 2 | 2 | 48 | 242 (75.4%) |
| | now | 10,120 | 9,895 (97.8%) | 225 | 29 (12.9%): bounded 18, lockstep-induction 11 | 162 (72.0%): timeout 68, abstraction 50, opaque 36, unaligned-loop 6, recursion 2 | 0 | 34 | 152 (67.6%) |
| **All five** | last | 58,477 | 55,784 (95.4%) | 2,693 | 695 (25.8%) | 1,572 (58.4%) | 66 | 360 | |
| | now | 58,477 | 56,072 (95.9%) | 2,405 | 766 (31.9%) | 1,298 (54.0%) | 53 | 288 | 880 (36.6%) |

- `gitextensions-9860`: EQ006 4 to 0 (P2-163) and EQ002 10 to 6 (P2-164). 453 of its 533 changed pairs are not
  congruent by `context`, the interpolated string bound to `string.Format` on net5.0 and to
  `DefaultInterpolatedStringHandler` on net6.0; the solver proves 298 of them. 53 fewer `no-body` Unknowns (P2-165).
- `jellyfin-13023`: as P2-124's run, one more pair proved.
- `gitextensions-8522`: EQ006 166 to 137 and EQ002 29 to 23; the solver proves 154 where it proved 140.
- `duplicati-3124`: EQ006 142 to 117. Its EQ002 stay at 23, 11 of them P2-192's (below).
- `openra-17989`: changed pairs 321 to 225 and the solver proves 29 of them, from 4: P2-197's config gives the four
  mod projects net5.0, and 52 of the 69 bodies under the .NET 9 float rule are congruent. EQ002 2 to 0 (P2-184).

The 880 `runtime-rule` pairs are 620 of the 1,298 Unknowns (47.8%) and 259 of the 341 Divergent; the solver proves 1.
By the first row each Unknown names: `Path.Combine(string,string)` 79, `extern` 76 (the `[DllImport]` bodies, P2-214),
`String.StartsWith(string)` 68, `File.Delete` 26, `String.EndsWith(string)` 21, then rows of 18 or fewer.

## Adjudication (criterion 4)

P2-130's method, unchanged: **confirmed** needs an executed oracle (replay or a test); **false positive** is a hand
trace showing the claimed difference cannot happen on the pair's runtimes; anything else is **undetermined**, with its
obstacle. No replay exists for these runs and none of the five pairs' runtimes is installed (the box has .NET 6.0 and
10.0), so nothing new was executed and a difference real by hand trace is undetermined. P2-130's decisions stand
(Duplicati's device-path results, a `using` added to a disposable, the `StringBuilder` interpolated append).

On the upgrades every EQ002 and EQ006 was adjudicated (7). On each migration pair the fresh draw is 30 EQ002 and 30
EQ006, or every one where the pair has fewer: 23 + 30, 23 + 30 and 0 + 30. Of the 143 results, 82 keep P2-124's or
P2-130's classification (the decision above) and 61 were traced again by hand. The tracing was done by three
sub-agents, one per migration pair, under one written brief; their tables were read in full and their causes checked
against the open tickets before any was filed.

| Pair | Rule | In run | Sampled | Kept | Traced | Confirmed | False positive | Undetermined | Precision |
|---|---|---|---|---|---|---|---|---|---|
| gitextensions-9860 | EQ002 | 6 | 6 | 6 | 0 | 5 | 1 | 0 | 83.3% (5 of 6) |
| | EQ006 | 0 | 0 | | | | | | n/a |
| jellyfin-13023 | EQ002 | 1 | 1 | 1 | 0 | 1 | 0 | 0 | 100% (1 of 1) |
| **Upgrades** | **all** | 7 | 7 | 7 | 0 | 6 | 1 | 0 | **85.7% (6 of 7)**, from 40.0% (6 of 15) |
| gitextensions-8522 | EQ002 | 23 | 23 | 23 | 0 | 1 | 6 | 16 | 14.3% (1 of 7) |
| | EQ006 | 137 | 30 | 6 | 24 | 0 | 11 | 19 | 0% (0 of 11) |
| | **all** | 160 | 53 | 29 | 24 | 1 | 17 | 35 | **5.6% (1 of 18)**, from 3.4% (1 of 29) |
| duplicati-3124 | EQ002 | 23 | 23 | 23 | 0 | 0 | 15 | 8 | 0% (0 of 15) |
| | EQ006 | 117 | 30 | 5 | 25 | 0 | 6 | 24 | 0% (0 of 6) |
| | **all** | 140 | 53 | 28 | 25 | 0 | 21 | 32 | **0% (0 of 21)**, from 0% (0 of 27) |
| openra-17989 | EQ006 | 34 | 30 | 18 | 12 | 0 | 7 | 23 | **0% (0 of 7)**, from 0% (0 of 13) |
| **Migrations** | **all** | 334 | 136 | 75 | 61 | 1 | 45 | 90 | **2.2% (1 of 46)**, from 1.4% (1 of 69) |

"Kept" for Duplicati counts P2-192's 9 and for `gitextensions-9860` P2-211's 1 (the decision above). If every
undetermined migration result were real, migration precision would be at most 66.9% (91 of 136), from 53.1%.

- The upgrades: P2-163 and P2-164 removed 8 of the 9 false positives; the six confirmed results are P2-124's,
  unchanged. The ninth is `PatchManager::ToPatch(...)`, whose builder is handed to another call before the append
  (P2-211).
- Undetermined, by obstacle (90): 57 a real difference by hand trace, not executed; 9 depend on the process
  environment or the host (ANSI code page, a manifest, file access rights, network timing, power hardware, current
  directory); 5 on the host OS (`SystemIOLinux`, POSIX only); 8 no differing ICU/NLS input found; 5 real by
  construction (`GetHashCode`, the runtime's own version, a retyped getter, the build-generated commit constant); 4
  third-party code outside the checkout; 2 the device-path decision.
- Real behaviour changes the traces found, none executed, so undetermined: `Thread.Abort` on net5.0 in Duplicati's
  `HttpServerConnection::Close()` (always) and `PeriodicOutput::Dispose()` (a race); P2-130's
  `S3IAM::CreateUnprivilegedUser(...)` and `GourceStart::linkLabel1_LinkClicked(...)`, unchanged; a signed zero through
  OpenRA's public `float2::Max` and `Min`. Duplicati's `UpdaterManager::GetBestUpdateVersion(bool)` is EQ006 by a path
  row, and its body was also edited (the key it returns changed); the EQ006 names the row, not the edit.
- `gitextensions-8522` row 30 of the sample, `ConfigFileSettings::CreateSystemWide(bool)`, was P2-189's false positive:
  P2-189's row no longer fires, and what fires now is a second `Path.Combine` on a stored setting, a real difference.

### False-positive causes

| Ticket | Cause | Rule | 9860 | 8522 | Duplicati | OpenRA | All |
|---|---|---|---|---|---|---|---|
| P2-213 (S, new) | P2-192's replaced callees (the remoting `CallContext` shim, the hashing helper) are declared only in a config the default run does not pass; `tools/corpus/configs/` has none for Duplicati | EQ002 | | | 9 | | 9 |
| P2-216 (M, new) | Five rows match calls their change cannot reach: `Process.StartInfo` of a new process, UTF-8 decoding of constant well-formed bytes, `TreeNode.ImageIndex` on `Count - 1`, `OSVersion.Version.Major` against 6, the ICU `Equals` row on an ordinal comparison a solution getter returns | EQ006 | | 4 | 1 | | 5 |
| P2-218 (M, new) | A valid path by five routes no done ticket follows: an override called only by its base class after `File.Exists`, an NSubstitute `Returns` constant, an interface call on a sealed `static readonly` field, a static field the object's initializers already combined, a setting whose setters check `Directory.Exists` | EQ006 | | 3 | 2 | | 5 |
| P2-217 (M, new) | A `File.Exists` guard on a field or property read again at the call (P2-074 reads parameters and locals); and a swallowed throw where the modern side reaches only `File.Exists` | EQ006 | | 1 | 3 | | 4 |
| P2-207 (open) | A valid path a private method's callers, a reassigned local or a lazily filled field holds | EQ006 | | 2 | | 2 | 4 |
| P2-193 (open) | The static ACL members against `FileSystemAclExtensions` | EQ002 | | | 4 | | 4 |
| P2-208 (open) | A field written through a local function, a trailing separator or a private setter | EQ006 | | 1 | | 2 | 3 |
| P2-220 (M, new) | A row exclusion that needs another method's data flow: a `FieldInfo` from a cache filled with `Instance` flags, a private method whose only caller converts a signed zero to `int` (and P2-190's kept `OAuthHttpClient::.cctor()`) | EQ006 | | | | 2 | 2 |
| P2-185 (open) | An unchanged call whose return type a package upgrade retyped | EQ002 | | 2 | | | 2 |
| P2-186 (open) | An allocation-only constructor outside the effect-free catalogue | EQ002 | | 2 | | | 2 |
| P2-219 (S, new) | The "always throws on .NET" row for `RSA.FromXmlString`, which .NET Core 3.0 implements | EQ006 | | | | 1 | 1 |
| P2-211 (open) | What P2-164 leaves of the `StringBuilder` appends | EQ002 | 1 | | | | 1 |
| P2-187 (open) | A field renamed with the same initializer | EQ002 | | 1 | | | 1 |
| P2-097 (open) | A call to a renamed private helper | EQ002 | | 1 | | | 1 |
| P2-205 (open) | A sync call replaced by its `Async` twin and `GetResult` | EQ002 | | | 1 | | 1 |
| P2-206 (open) | A renamed static getter of the caller's type | EQ002 | | | 1 | | 1 |
| **All** | | | **1** | **17** | **21** | **7** | **46** |

No cause is larger than 9. The EQ006 causes (24 of 46) are still row reach: valid paths (16, in P2-207, P2-208,
P2-217, P2-218) and rows that match calls they cannot reach (7, P2-216, P2-220), plus one catalogue error (P2-219).
No false positive in the samples is one a done ticket's fix should have cleared and did not, except P2-192's 9, which
need the declaration.

## The fixes against the old audits (criterion 5)

For each fix, the false positives its Goal claimed (the procedures P2-124's and P2-130's adjudications gave it) and
what each is now. P2-197's claim is the 69 unedited OpenRA bodies under the .NET 9 float rule in its baseline run.

| Ticket | Its Goal claimed | Congruent | Proved by the solver | Unknown | Still Divergent |
|---|---|---|---|---|---|
| P2-163 | 4 false EQ006 on `gitextensions-9860` (the `TreeNodeCollection` indexer read) | 4 | 0 | 0 | 0 |
| P2-164 | 5 false EQ002 on `gitextensions-9860` (`StringBuilder.Append($"...")`) | 0 | 4 (bounded 2, lockstep-induction 2) | 0 | 1: `PatchManager::ToPatch(...)`, P2-211 |
| P2-184 | 9 false EQ002 on the three migrations (a rewritten call never closed) | 0 | 8 (bounded 5, lockstep-induction 3) | 1 (abstraction: `FileStatusList::IsFilterMatch`, P2-181's case) | 0 |
| P2-188 | 16 false EQ006 (13 of P2-130's sample, 3 of P2-047's) on a valid path | 5 | 0 | 2 (abstraction 1, opaque 1) | 9: P2-200's 6 and P2-201's 3 below |
| P2-189 | 11 false EQ006 on rows with no precondition | 7 | 1 (lockstep-induction) | 2 (abstraction 1, timeout 1) | 1: `CreateSystemWide(bool)`, now another call and a real difference (undetermined) |
| P2-190 | 13 false EQ006 on rows matching calls their change misses | 11 | 0 | 0 | 2: `FieldLoader::Load` and `OAuthHttpClient::.cctor()`, kept by P2-190's size guard, P2-220 |
| P2-192 | 11 false EQ002 on Duplicati (replaced callees) | 0 | 0 | 0 | 11: no declaration in a default run, P2-213 |
| P2-197 | 69 OpenRA bodies under the .NET 9 float rule (31 timeout, 26 abstraction, 8 opaque, 4 unaligned-loop) | 52 | 0 | 17 (timeout 9, abstraction 3, opaque 3, unaligned-loop 2), another row of theirs in (net472, net5.0] | 0 |
| P2-200 | 8 of P2-188's, more than one call away | 2 | 0 | 0 | 6: P2-207 |
| P2-201 | 4 of P2-188's, a field or get-only property | 0 | 0 | 1 (opaque, `rebound-call`) | 3: P2-208 |

P2-184's row counts P2-047's row 30, the same procedure as P2-130's sample row 16, once. The three `Harvester`
getters that P2-191 counted are congruent too (P2-197's config): P2-191's one false positive is gone.

Against the scoreboard's two weak rows: the upgrades' precision row rises to 85.7%, and the one false positive left
has its ticket. On the migrations the fixes cleared what they claimed in a default run (P2-192 aside), so Divergent
falls by 85 and the solver proves more, but the fresh samples reach the next causes in the same proportion: the
precision row moves from 1.4% to 2.2%, and each cause left holds 1 to 5 results.

## Findings (criterion 6)

No run crashed. 5% of the changed pairs is 27 on `gitextensions-9860`, 7 on `jellyfin-13023`, 44 on
`gitextensions-8522`, 33 on `duplicati-3124` and 12 on `openra-17989`. "Alone" is ADR 0028's measure: the changed
pairs whose reason set is that one reason.

| Ticket | Finding | Pairs |
|---|---|---|
| P2-213 (S, new) | P2-192's declaration is not in the pair's committed config: 9 of Duplicati's 21 sampled false positives, all 11 of P2-192's results | duplicati-3124 |
| P2-214 (M, new) | `no-body` alone in 51 of 870 changed pairs (5.9%): unedited `[DllImport]` bodies, which ADR 0058 leaves Unknown on any pair that leaves .NET Framework; no open owner since P2-165 | gitextensions-8522 |
| P2-215 (M, new) | `Conversion` alone in 8 of 122 changed pairs (6.6%); no open owner since P2-123 | jellyfin-13023 |
| P2-216 to P2-220 (new) | The five new false-positive causes above, each grouping the shapes one fix would read | gitextensions-8522, duplicati-3124, openra-17989 |
| P2-179, existing and open | `rebound-call` alone in 121 of 870 (13.9%) | gitextensions-8522 |
| P2-182, existing and open | `DelegateCreation` alone in 20 of 225 (8.9%) on OpenRA, and 28 of 533 (5.3%) on `gitextensions-9860`, 22 of whose Unknowns are interpolated-string bodies (`context`) | openra-17989, gitextensions-9860 |
| P2-180, existing and open | `CaughtException` alone in 36 of 655 (5.5%) on Duplicati and 14 of 225 (6.2%) on OpenRA | duplicati-3124, openra-17989 |
| P2-102 and P2-139, existing | `InterpolatedString` alone in 38 of 533 (7.1%); as P2-124 found, P2-139 counted its forms and none is at 5% alone; P2-102 owns one | gitextensions-9860 |

Below 5% and not filed: on `gitextensions-9860` `LocalFunction` and `rebound-call` 25 each (4.7%); on
`gitextensions-8522` `LocalFunction` 34 (3.9%); on `duplicati-3124` `iterator` 21 (3.2%) and `no-body` 19 (2.9%, P2-214's
measure); on `openra-17989` `ArrayElementReference` 9 (4.0%, P2-183 open). `timeout` and `abstraction` are not opaque
reasons; the `runtime-rule` count above is most of both.

Not a result of `equiv`: `corpus.ps1 -Unchanged` prints its table to the host, so a script that captures the success
stream gets nothing; the numbers in each summary were read from the console.

## Scoreboard (criterion 7)

`equiv-scoreboard` was applied over the five large pairs, all from these runs at one commit. Rows rewritten: the
Measured cell of every row these runs measure, Proved Equivalent, Equivalent by congruence, Changed pairs the solver
proves, Unknown, Divergent, Divergent precision (migrations and upgrades), Left for a reviewer, Run time. The header
(5 pairs, 58,477 matched), Soundness, Large pairs that run to a result and Repeatability did not change.

| Row | Was | Now |
|---|---|---|
| Proved Equivalent | 96.6% (56,479 of 58,477) | 97.2% (56,838 of 58,477) |
| Equivalent by congruence | 95.4% (55,784 of 58,477) | 95.9% (56,072 of 58,477) |
| Changed pairs the solver proves | 25.8% (695 of 2,693) | 31.9% (766 of 2,405) |
| Unknown | 2.7% of pairs (1,572), 58.4% of changed pairs; opaque 647, abstraction 478, budget 401, unaligned loop 39, recursion 7 | 2.2% of pairs (1,298), 54.0% of changed pairs; opaque 573, abstraction 362, budget 326, unaligned loop 33, recursion 4 |
| Divergent | 0.7% of pairs (426), 15.8% of changed pairs | 0.6% of pairs (341), 14.2% of changed pairs |
| Divergent precision | migrations 1.4% (1 of 69), upgrades 40.0% (6 of 15) | migrations 2.2% (1 of 46), upgrades 85.7% (6 of 7) |
| Left for a reviewer | 3.4% of pairs (1,998) | 2.8% of pairs (1,639) |
| Run time | mean 6 min (3 min to 9 min) | mean 5 min (3 min to 7 min) |

Every row that moved got better.

## Next milestone (criterion 8)

Ranked by what each moves on the Unknown and Divergent precision rows; `docs/ROADMAP.md` "Scoreboard milestone 3"
holds the list. For opaque reasons the count is the changed pairs where the reason is alone.
1. P2-213 (S): 9 of the migrations' 45 sampled false positives, and all 11 of P2-192's results, for one committed file.
2. Valid paths (16 false positives): P2-218 (5), P2-207 (4), P2-217 (4), P2-208 (3).
3. Rows that cannot reach their change (8): P2-216 (5), P2-220 (2), P2-219 (1).
4. The EQ002 causes, 1 to 4 each (13): P2-193 (4), P2-185 (2), P2-186 (2), P2-211, P2-187, P2-097, P2-205, P2-206.
5. Unknown, the opaque reasons at or above 5% alone: P2-179 (`rebound-call`, 121 on `gitextensions-8522`), P2-214
   (`no-body`, 51, and 76 `runtime-rule` Unknowns in all), P2-182 (`DelegateCreation`, 20 and 28), P2-180
   (`CaughtException`, 36 and 14), P2-215 (`Conversion`, 8).

Not on it: the 620 `runtime-rule` Unknowns that remain after P2-181 are mostly path and culture rows (`Path.Combine`
79, `String.StartsWith` 68, `File.Delete` 26, `String.EndsWith` 21); the valid-path tickets above take some of the
path ones. `timeout` (326) is the solver budget, as P2-130 said.

## Method notes

- Runs: `./tools/corpus/corpus.ps1 -Compare <slug> -Mode quick -CompareArgs '--jobs', '4'`, five in a row from one
  script and one Release build of `bfbf413e`. `openra-17989` got its committed config (`tools/corpus/configs/`, P2-197)
  through `-Compare`; no other pair has one.
- Counts come from the SARIF logs (`run.properties.loweringCensus`, results by rule, `proofMethod`, `unknownReason`,
  `notCongruent`, `reviewList`), read with throwaway scripts under the session's scratch directory, and agree with
  `corpus.ps1 -Metrics`. P2-124's and P2-130's SARIF are no longer on the box; their numbers are their verdicts', and
  their adjudicated results were matched by procedure identity from their appendices and tables. P2-197's 69 bodies
  were read from its baseline run's SARIF, still on the box.
- The tracing tables with each result's one-line cause stay under `.corpus/`; the appendix below holds identities and
  classes only.

## Appendix: every adjudicated result

In sample order per pair (the draw above); the upgrades in result order.

| Pair | # | Rule | Procedure | Fingerprint | Classification | Ticket (false positive), basis (confirmed) or obstacle (undetermined) |
|---|---|---|---|---|---|---|
| Git Extensions 9860 | 0 | EQ002 | `` GitExtensions.Program::Main() `` | `3c7a06642555` | confirmed | kept from P2-124 (P2-066) |
| Git Extensions 9860 | 1 | EQ002 | `` GitCommands.Patches.PatchManager::ToPatch(System.Collections.Generic.IEnumerable<global::GitCommands.Patches.Chunk>,GitCommands.Patches.SubChunkToPatchFnc) `` | `6ecfb092aead` | false positive | P2-211 |
| Git Extensions 9860 | 2 | EQ002 | `` ResourceManager.Xliff.TranslationSerializer::Serialize(ResourceManager.Xliff.TranslationFile,string) `` | `9144842f2198` | confirmed | kept from P2-124 (P2-066) |
| Git Extensions 9860 | 3 | EQ002 | `` GitExtensions.Plugins.Gource.GourcePlugin::DownloadFile(string,string) `` | `a5f6b99e9e42` | confirmed | kept from P2-124 |
| Git Extensions 9860 | 4 | EQ002 | `` GitExtensions.Plugins.Gource.GourcePlugin::SearchForGourceUrl() `` | `f8cc47ab6644` | confirmed | kept from P2-124 |
| Git Extensions 9860 | 5 | EQ002 | `` BugReporter.Program::Main() `` | `884b0ab09c88` | confirmed | kept from P2-124 (P2-066); not a change the upgrade made |
| Jellyfin | 0 | EQ002 | `` Jellyfin.Server.Helpers.StartupHelpers::PerformStaticInitialization() `` | `1bd2b7a79b53` | confirmed | kept from P2-124 (P2-066) |
| Git Extensions 8522 | 0 | EQ002 | `` EasyHook.HookAccessControl::SetInclusiveACL(int[]) `` | `b8563157e808` | undetermined | kept from P2-130 Git Extensions #10: real difference by hand trace, not executed |
| Git Extensions 8522 | 1 | EQ002 | `` EasyHook.LocalHook::Release() `` | `f98ef866387d` | confirmed | kept from P2-047 (row 18: replay `reproduced`) |
| Git Extensions 8522 | 2 | EQ002 | `` EasyHook.LocalHook::GetProcAddress(string,string) `` | `382e6ddf0d74` | undetermined | kept from P2-130 Git Extensions #1: real difference by hand trace, not executed |
| Git Extensions 8522 | 3 | EQ002 | `` EasyHook.LocalHook::IsThreadIntercepted(int) `` | `a3a8958bc604` | undetermined | kept from P2-130 Git Extensions #25: real difference by hand trace, not executed |
| Git Extensions 8522 | 4 | EQ002 | `` GitExtensions.UITests.Script.ScriptRunnerTests::Setup() `` | `2c47f64c4e81` | false positive | P2-187 |
| Git Extensions 8522 | 5 | EQ002 | `` GitUI.UserControls.RevisionGrid.Graph.RevisionGraphRevision::.ctor(GitUIPluginInterfaces.ObjectId,int) `` | `4bc2b0def908` | false positive | P2-186 |
| Git Extensions 8522 | 6 | EQ002 | `` GitCommands.EnvironmentPathsProvider::IsValidPath(string) `` | `1c2650b415c5` | undetermined | kept from P2-130 Git Extensions #12: real difference by hand trace, not executed |
| Git Extensions 8522 | 7 | EQ002 | `` EasyHook.HookAccessControl::SetExclusiveACL(int[]) `` | `e82d05c7ff8d` | undetermined | kept from P2-130 Git Extensions #14: real difference by hand trace, not executed |
| Git Extensions 8522 | 8 | EQ002 | `` GitUI.Theming.ThemeLoader::.ctor(GitUI.Theming.IThemeCssUrlResolver,GitUI.Theming.IThemeFileReader) `` | `9500d96f3927` | false positive | P2-186 |
| Git Extensions 8522 | 9 | EQ002 | `` GitCommandsTests.SshPathLocatorTest::File_system_access_throwing_should_return_empty_string() `` | `2af4a371a664` | false positive | P2-185 |
| Git Extensions 8522 | 10 | EQ002 | `` GitUI.Avatars.GithubAvatarProvider::GetAvatarAsync(string,string,int) `` | `4162ceb4d1b9` | false positive | P2-097 |
| Git Extensions 8522 | 11 | EQ002 | `` GitUI.UserControls.RevisionGrid.Graph.RevisionGraphRevision::get_StartSegments() `` | `18ddeb0fa18e` | undetermined | kept from P2-130 Git Extensions #0: real by construction (the getter's return type changed), not executed |
| Git Extensions 8522 | 12 | EQ002 | `` GitUIPluginInterfaces.ManagedExtensibility::TryLoadAssembly(System.IO.FileInfo) `` | `ac56e0e65b8f` | undetermined | kept from P2-130 Git Extensions #18: real difference by hand trace, not executed |
| Git Extensions 8522 | 13 | EQ002 | `` GitUI.Theming.ThemeLoader::StyleRuleThemeException(ExCSS.StyleRule,string) `` | `007c0477d601` | undetermined | kept from P2-130 Git Extensions #15: depends on library code outside the checkout (ExCSS 2.0.6 against 4.1.0), not executed |
| Git Extensions 8522 | 14 | EQ002 | `` BugReporter.Program::Main() `` | `aa53103de73e` | undetermined | kept from P2-130 Git Extensions #23: real by construction, not the migration (build-generated commit constant) |
| Git Extensions 8522 | 15 | EQ002 | `` Git.hub.Client::setOAuth2Token(string) `` | `b02b0d38a8f0` | undetermined | kept from P2-130 Git Extensions #9: depends on library code outside the checkout (RestSharp 106.11.4 on both sides), not executed |
| Git Extensions 8522 | 16 | EQ002 | `` GitUI.OsShellUtil::PickFolder(System.Windows.Forms.IWin32Window,string) `` | `29f7a853882f` | undetermined | kept from P2-130 Git Extensions #8: real difference by hand trace, not executed |
| Git Extensions 8522 | 17 | EQ002 | `` GitCommandsTests.SshPathLocatorTest::Find_on_gitBinDir_parent_throwing_should_return_empty_string() `` | `4685d00903ca` | false positive | P2-185 |
| Git Extensions 8522 | 18 | EQ002 | `` EasyHook.LocalHook::get_HookBypassAddress() `` | `d09c0d65c7bb` | undetermined | kept from P2-130 Git Extensions #6: real difference by hand trace, not executed |
| Git Extensions 8522 | 19 | EQ002 | `` CommonTestUtils.MockExecutable.MockProcess::Verify() `` | `c7220fc7467b` | undetermined | kept from P2-130 Git Extensions #2: real difference by hand trace, not executed |
| Git Extensions 8522 | 20 | EQ002 | `` GitUI.Theming.ThemeModule::ResetGdiCaches() `` | `01889bee21ab` | undetermined | kept from P2-130 Git Extensions #17: real difference by hand trace, not executed |
| Git Extensions 8522 | 21 | EQ002 | `` GitCommandsTests.Settings.FileSettingsCacheTests::ctor_FileWatcher_Filter_should_be_default_if_invalid_dir(string) `` | `c8de28bb25c6` | undetermined | kept from P2-130 Git Extensions #4: real difference by hand trace, not executed |
| Git Extensions 8522 | 22 | EQ002 | `` GitUI.CommitInfo.CommitInfo.BranchComparer::Compare(string,string) `` | `a3a728141a9f` | undetermined | kept from P2-047 (row 21: real difference by hand trace, not executed: non-public) |
| Git Extensions 8522 | 23 | EQ006 | `` ConEmu.WinForms.AnsiStreamChunkEventArgs::GetText(System.Text.Encoding) `` | `0be924d76e64` | undetermined | real difference by hand trace, not executed (caller-chosen encoding, ill-formed bytes) |
| Git Extensions 8522 | 24 | EQ006 | `` GitCommands.GitRefName::GetRemoteName(string) `` | `471c40414caf` | undetermined | no differing ICU/NLS input found (the call is a Debug.Assert condition) |
| Git Extensions 8522 | 25 | EQ006 | `` GitUI.NBugReports.BugReportInvoker::Report(System.Exception,bool) `` | `230e24cf7a05` | undetermined | kept from P2-130 Git Extensions #49: depends on process current directory |
| Git Extensions 8522 | 26 | EQ006 | `` GitCommands.Settings.GitExtSettingsCache::ReadSettings(string) `` | `44f8406d7748` | false positive | P2-218 |
| Git Extensions 8522 | 27 | EQ006 | `` GitCommands.PathUtil::TryFindShellPath(string,out string) `` | `e42e7cdc622c` | false positive | P2-217 |
| Git Extensions 8522 | 28 | EQ006 | `` GitCommandsTests.Settings.FileSettingsCacheTests::SaveImpl_should_create_folder_if_absent() `` | `4cd2523685fd` | false positive | P2-208 |
| Git Extensions 8522 | 29 | EQ006 | `` GitCommands.AppSettings::get_AvatarImageCachePath() `` | `1303d6c70d61` | false positive | P2-207 |
| Git Extensions 8522 | 30 | EQ006 | `` GitCommands.Settings.ConfigFileSettings::CreateSystemWide(bool) `` | `62a7b6c929ba` | undetermined | real difference by hand trace, not executed (P2-189's row no longer fires; the second Combine on a stored setting does) |
| Git Extensions 8522 | 31 | EQ006 | `` ICSharpCode.TextEditor.Document.DocumentFactory::CreateFromFile(string) `` | `b93c4926a47f` | undetermined | kept from P2-130 Git Extensions #48: real difference by hand trace, not executed |
| Git Extensions 8522 | 32 | EQ006 | `` GitUITests.Theming.ThemeLoaderTests::Should_throw_When_cyclic_css_imports() `` | `b270e5ab2db1` | false positive | P2-218 |
| Git Extensions 8522 | 33 | EQ006 | `` GitCommands.ExternalLinks.ExternalLink::GetHashCode() `` | `b49a928f213e` | undetermined | kept from P2-130 Git Extensions #40: real by construction (hash differs per runtime), not executed |
| Git Extensions 8522 | 34 | EQ006 | `` GitCommandsTests.Config.ConfigFileTest::Cleanup() `` | `156d4bf5ebfa` | undetermined | depends on the file's access rights on the host |
| Git Extensions 8522 | 35 | EQ006 | `` ResourceManager.LinkFactory::ExecuteLink(string,System.Action<global::ResourceManager.CommandEventArgs>,System.Action<string>) `` | `5229dd26af6c` | false positive | P2-216 |
| Git Extensions 8522 | 36 | EQ006 | `` GitCommands.GitModule::InTheMiddleOfMerge() `` | `56da97efeb29` | false positive | P2-218 |
| Git Extensions 8522 | 37 | EQ006 | `` CommonTestUtils.GitModuleTestHelper::EnsureCreatedInTempFolder(string) `` | `9bae3cf3de37` | undetermined | no differing ICU/NLS input found |
| Git Extensions 8522 | 38 | EQ006 | `` GitCommandsTests.Git.EncodingHelperTest::DecodeStringTestUtf8() `` | `b63ca2f447d9` | false positive | P2-216 |
| Git Extensions 8522 | 39 | EQ006 | `` CommonTestUtils.GitModuleTestHelper::CreateRepoFile(string,string,string) `` | `5dcbc6d291c1` | undetermined | real difference by hand trace, not executed |
| Git Extensions 8522 | 40 | EQ006 | `` GitCommands.FileInfoExtensions::MakeFileTemporaryWritable(string,System.Action<string>) `` | `584d95e4eeb1` | undetermined | depends on the file's access rights on the host |
| Git Extensions 8522 | 41 | EQ006 | `` GitUI.CommandsDialogs.FormVerify::GuessFileTypeWithContent(string) `` | `d45b8aa7876e` | undetermined | no differing ICU/NLS input found |
| Git Extensions 8522 | 42 | EQ006 | `` GitUI.CommandsDialogs.SettingsDialog.SettingsTreeViewUserControl::AddPage(System.Windows.Forms.TreeNodeCollection,GitUI.CommandsDialogs.SettingsDialog.ISettingsPage,System.Drawing.Image) `` | `21ea785c3540` | false positive | P2-216 |
| Git Extensions 8522 | 43 | EQ006 | `` GitUI.SpellChecker.SpellCheckerHelper::IsSeparator(char) `` | `5a0f97e014a9` | undetermined | real difference by hand trace, not executed (a reclassified code point) |
| Git Extensions 8522 | 44 | EQ006 | `` ConEmu.WinForms.AnsiStreamChunkEventArgs::GetMbcsText() `` | `010118a88a18` | undetermined | depends on process environment (ANSI code page) |
| Git Extensions 8522 | 45 | EQ006 | `` GitExtensions.Plugins.Gource.GourceStart::linkLabel1_LinkClicked(object,System.Windows.Forms.LinkLabelLinkClickedEventArgs) `` | `172ab08d40f6` | undetermined | kept from P2-130 Git Extensions #47: real difference by hand trace, not executed |
| Git Extensions 8522 | 46 | EQ006 | `` GitUI.UserControls.ExListView::.ctor() `` | `97a421a6f2a3` | false positive | P2-216 |
| Git Extensions 8522 | 47 | EQ006 | `` ICSharpCode.TextEditor.Document.FileSyntaxModeProvider::GetSyntaxModeFile(ICSharpCode.TextEditor.Document.SyntaxMode) `` | `d56eb58228db` | undetermined | real difference by hand trace, not executed |
| Git Extensions 8522 | 48 | EQ006 | `` GitUI.Script.ScriptOptionsParser::DependsOnSelectedRevision(string) `` | `5ba823711dd7` | undetermined | no differing ICU/NLS input found |
| Git Extensions 8522 | 49 | EQ006 | `` GitUI.Interops.DwmApi.DwmApi::IsWindows10BuildOrGreater(int) `` | `47efcdf55e5d` | undetermined | depends on process environment (host executable's manifest) |
| Git Extensions 8522 | 50 | EQ006 | `` GitCommands.Settings.ConfigFileSettings::CreateLocal(GitCommands.GitModule,GitCommands.Settings.ConfigFileSettings,GitUIPluginInterfaces.SettingLevel,bool) `` | `0747852b3c5b` | false positive | P2-207 |
| Git Extensions 8522 | 51 | EQ006 | `` ICSharpCode.TextEditor.Util.FileReader::AutoDetect(System.IO.Stream,byte,byte,System.Text.Encoding) `` | `d7ffa5af213a` | undetermined | depends on process environment (ANSI code page) |
| Git Extensions 8522 | 52 | EQ006 | `` GitCommands.PathUtil::Combine(string,string) `` | `9b30e2a53780` | undetermined | real difference by hand trace, not executed |
| Duplicati | 0 | EQ002 | `` Duplicati.GUI.TrayIcon.Program::GetWinformsInstance() `` | `b9882e379cf8` | undetermined | kept from P2-130 Duplicati #18: real difference by hand trace, not executed: UI |
| Duplicati | 1 | EQ002 | `` Duplicati.Library.Common.IO.SystemIOWindows::SetAccessControlDir(string,System.Security.AccessControl.DirectorySecurity) `` | `f49c7b66807b` | false positive | P2-193 |
| Duplicati | 2 | EQ002 | `` Duplicati.Library.Localization.LocalizationContext::.ctor(System.Globalization.CultureInfo) `` | `441031e80442` | false positive | P2-213 |
| Duplicati | 3 | EQ002 | `` Duplicati.Library.Backend.S3IAM::CreateUnprivilegedUser(string,string,string) `` | `6d8d81324e0e` | undetermined | kept from P2-130 Duplicati #19: real difference by hand trace, not executed |
| Duplicati | 4 | EQ002 | `` Duplicati.Library.Localization.LocalizationService::get_Current() `` | `2887e830b600` | false positive | P2-213 |
| Duplicati | 5 | EQ002 | `` Duplicati.Library.Logging.Log::set_CurrentScope(Duplicati.Library.Logging.LogScope) `` | `610683f9ca1a` | false positive | P2-213 |
| Duplicati | 6 | EQ002 | `` Duplicati.Library.AutoUpdater.UpdaterManager::RunFromMostRecent(System.Reflection.MethodInfo,string[],Duplicati.Library.AutoUpdater.AutoUpdateStrategy) `` | `1f66711aa158` | undetermined | kept from P2-130 Duplicati #5: real difference by hand trace, not executed; depends on process environment |
| Duplicati | 7 | EQ002 | `` Duplicati.Library.Common.IO.SystemIOWindows::GetAccessControlDir(string) `` | `e4cbfee396b9` | false positive | P2-193 |
| Duplicati | 8 | EQ002 | `` Duplicati.Library.Utility.Power.WindowsPowerSupplyState::GetSource() `` | `66d2f65fb32f` | undetermined | kept from P2-130 Duplicati #15: real difference by hand trace, not executed; depends on the machine's power hardware |
| Duplicati | 9 | EQ002 | `` Duplicati.Library.Localization.LocalizationContext::Dispose() `` | `21a0359b0fd3` | false positive | P2-213 |
| Duplicati | 10 | EQ002 | `` Duplicati.Library.Main.BackendManager::GetFileHasherStream(System.IO.Stream,System.Security.Cryptography.CryptoStreamMode,out System.Func<string>) `` | `4a3cd6feac45` | false positive | P2-213 |
| Duplicati | 11 | EQ002 | `` Duplicati.Library.Main.Utility.Metahash::.ctor(System.Collections.Generic.Dictionary<string, string>,Duplicati.Library.Main.Options) `` | `1485309eb88d` | undetermined | kept from P2-130 Duplicati #22: depends on library code outside the checkout (FasterHashing 1.3.0, binary only) for a hash name it does not support |
| Duplicati | 12 | EQ002 | `` Duplicati.Library.Utility.Utility::CalculateHash(System.IO.Stream) `` | `ad97a796d61d` | false positive | P2-206 |
| Duplicati | 13 | EQ002 | `` Duplicati.Library.Utility.HashCalculatingStream::.ctor(System.IO.Stream,string) `` | `04d82279cac7` | undetermined | kept from P2-130 Duplicati #12: depends on library code outside the checkout (FasterHashing 1.3.0, binary only) for a hash name it does not support |
| Duplicati | 14 | EQ002 | `` Duplicati.Library.Backend.AzureBlob.AzureBlobWrapper::AddFileStream(string,System.IO.Stream,System.Threading.CancellationToken) `` | `ab1d17bf7233` | undetermined | kept from P2-130 Duplicati #9: real difference by hand trace, not executed |
| Duplicati | 15 | EQ002 | `` Duplicati.Library.Backend.S3IAM::CanCreateUser(string,string) `` | `b691923f95ef` | false positive | P2-205 |
| Duplicati | 16 | EQ002 | `` Duplicati.Library.Common.IO.SystemIOWindows::GetAccessControlFile(string) `` | `214d7b9a4ecc` | false positive | P2-193 |
| Duplicati | 17 | EQ002 | `` Duplicati.Library.Utility.CallContextSettings`1.ContextGuard::.ctor() `` | `4e6e4b769560` | false positive | P2-213 |
| Duplicati | 18 | EQ002 | `` Duplicati.Library.Main.BackendManager::CalculateFileHash(System.IO.Stream) `` | `dd3ceb27877a` | false positive | P2-213 |
| Duplicati | 19 | EQ002 | `` Duplicati.Library.Utility.CallContextSettings`1::get_ContextID() `` | `c258e2f1548b` | false positive | P2-213 |
| Duplicati | 20 | EQ002 | `` Duplicati.Library.Utility.CallContextSettings`1.ContextGuard::Dispose() `` | `09e4350d4bbe` | false positive | P2-213 |
| Duplicati | 21 | EQ002 | `` Duplicati.Library.Common.IO.SystemIOWindows::SetAccessControlFile(string,System.Security.AccessControl.FileSecurity) `` | `d072f53919bd` | false positive | P2-193 |
| Duplicati | 22 | EQ002 | `` Duplicati.GUI.TrayIcon.Program::TryGetWinforms() `` | `6b406f3a2ae3` | undetermined | kept from P2-130 Duplicati #21: real difference by hand trace on a POSIX host (legacy under Mono), not executed; depends on host OS |
| Duplicati | 23 | EQ006 | `` Duplicati.Library.Common.IO.SystemIOLinux::PathGetFileName(string) `` | `7c80d62743d6` | undetermined | real difference by hand trace on a .NET Framework host, not executed; depends on host OS (SystemIOLinux on POSIX only) |
| Duplicati | 24 | EQ006 | `` Duplicati.GUI.TrayIcon.HttpServerConnection::Close() `` | `3908493f5f48` | undetermined | real difference by hand trace, not executed (Thread.Abort throws on net5.0) |
| Duplicati | 25 | EQ006 | `` Duplicati.UnitTest.BorderTests::TearDown() `` | `c6874c1ea39c` | false positive | P2-217 |
| Duplicati | 26 | EQ006 | `` Duplicati.Library.Common.IO.SystemIOLinux::GetLastWriteTimeUtc(string) `` | `74fc138ccdee` | undetermined | real difference by hand trace on a .NET Framework host, not executed; depends on host OS (SystemIOLinux on POSIX only) |
| Duplicati | 27 | EQ006 | `` Duplicati.UnitTest.CommandLineOperationsTests::get_zipAlternativeFilepath() `` | `192aea2e9998` | false positive | P2-218 |
| Duplicati | 28 | EQ006 | `` Duplicati.Library.Common.IO.SystemIOLinux::FileDelete(string) `` | `e72da95df63f` | undetermined | real difference by hand trace on a .NET Framework host, not executed; depends on host OS (SystemIOLinux on POSIX only) |
| Duplicati | 29 | EQ006 | `` Duplicati.Library.AutoUpdater.UpdaterManager::GetBestUpdateVersion(bool) `` | `1af4d83b9777` | undetermined | real difference by hand trace, not executed (the body also changed: an edited key the EQ006 label does not name) |
| Duplicati | 30 | EQ006 | `` Duplicati.Library.Backend.Mega.MegaBackend::PutAsync(string,string,System.Threading.CancellationToken) `` | `38ce2ab2092d` | undetermined | real difference by hand trace, not executed (a public filename parameter) |
| Duplicati | 31 | EQ006 | `` Duplicati.Library.Backend.Dropbox::Get(string,string) `` | `ff21bb979fa0` | undetermined | real difference by hand trace, not executed (a public filename parameter) |
| Duplicati | 32 | EQ006 | `` Duplicati.Library.Compression.FileArchiveDirectory::CreateFile(string,Duplicati.Library.Interface.CompressionHint,System.DateTime) `` | `7e4b0b0bf69b` | undetermined | real difference by hand trace, not executed (a public parameter) |
| Duplicati | 33 | EQ006 | `` Duplicati.Library.Compression.FileArchiveDirectory::FileExists(string) `` | `c73530c96aa9` | undetermined | real difference by hand trace, not executed (a public parameter) |
| Duplicati | 34 | EQ006 | `` Duplicati.Library.Compression.FileArchiveDirectory::ListFiles(string) `` | `d06976ec61a9` | undetermined | real difference by hand trace, not executed (a public parameter) |
| Duplicati | 35 | EQ006 | `` Duplicati.Library.Utility.TempFile::Dispose(bool) `` | `b710f34b74f2` | false positive | P2-217 |
| Duplicati | 36 | EQ006 | `` Duplicati.Library.Backend.WEBDAV::PutAsync(string,string,System.Threading.CancellationToken) `` | `30f9ecc9e0f0` | undetermined | real difference by hand trace, not executed (a public filename parameter) |
| Duplicati | 37 | EQ006 | `` Duplicati.Library.Utility.TempFile::.ctor(string) `` | `fa345df83d6a` | undetermined | real difference by hand trace, not executed |
| Duplicati | 38 | EQ006 | `` Duplicati.Server.LogWriteHandler::SetServerFile(string,Duplicati.Library.Logging.LogMessageType) `` | `cbae3ab75f11` | undetermined | real difference by hand trace, not executed (an unvalidated command-line option) |
| Duplicati | 39 | EQ006 | `` Duplicati.Library.Common.IO.SystemIOLinux::PathChangeExtension(string,string) `` | `7c1ceab6d9b8` | undetermined | kept from P2-130 Duplicati #25: real difference by hand trace on a .NET Framework host, not executed; depends on host OS (Duplicati selects `SystemIOLinux` on POSIX hosts only) |
| Duplicati | 40 | EQ006 | `` Duplicati.Server.SingleInstance::m_filewatcher_Created(object,System.IO.FileSystemEventArgs) `` | `a520911a192b` | false positive | P2-217 |
| Duplicati | 41 | EQ006 | `` Duplicati.Library.Utility.TempFile::.ctor() `` | `d07bda39ac71` | false positive | P2-218 |
| Duplicati | 42 | EQ006 | `` Duplicati.Library.Compression.FileArchiveDirectory::OpenRead(string) `` | `db85392854c2` | undetermined | kept from P2-130 Duplicati #50: real difference by hand trace, not executed |
| Duplicati | 43 | EQ006 | `` Duplicati.Library.Snapshots.UsnJournalService::IsPathEnumerated(string) `` | `392f17807c99` | false positive | P2-216 |
| Duplicati | 44 | EQ006 | `` Duplicati.CommandLine.Commands.PeriodicOutput::Dispose() `` | `e632d91142a6` | undetermined | real difference by hand trace, not executed (Thread.Abort throws on net5.0; a timing race) |
| Duplicati | 45 | EQ006 | `` Duplicati.Library.Backend.Dropbox::PutAsync(string,string,System.Threading.CancellationToken) `` | `0527f055c187` | undetermined | real difference by hand trace, not executed (a public filename parameter) |
| Duplicati | 46 | EQ006 | `` Duplicati.Library.Backend.Jottacloud::Get(string,string) `` | `d00d06277f90` | undetermined | kept from P2-130 Duplicati #29: real difference by hand trace, not executed |
| Duplicati | 47 | EQ006 | `` Duplicati.Library.Backend.SharePointBackend::uploadFileSlicePerSlice(Microsoft.SharePoint.Client.ClientContext,Microsoft.SharePoint.Client.Folder,System.IO.Stream,string,System.Threading.CancellationToken) `` | `9b56c032efc5` | undetermined | kept from P2-130 Duplicati #40: real difference by hand trace, not executed: non-public |
| Duplicati | 48 | EQ006 | `` Duplicati.Library.Backend.GoogleCloudStorage.GoogleCloudStorage::PutAsync(string,string,System.Threading.CancellationToken) `` | `221a31511eca` | undetermined | real difference by hand trace, not executed (a public filename parameter) |
| Duplicati | 49 | EQ006 | `` Duplicati.Library.Common.IO.SystemIOWindows::PathChangeExtension(string,string) `` | `7be87aa9263b` | undetermined | kept from P2-130 Duplicati #53: depends on .NET Framework device-path handling, not checked |
| Duplicati | 50 | EQ006 | `` Duplicati.Library.Snapshots.SnapshotBase::GetAttributes(string) `` | `c870e3aea3b6` | undetermined | real difference by hand trace, not executed |
| Duplicati | 51 | EQ006 | `` Duplicati.Library.UsageReporter.ReportSet::DoInitCLR() `` | `6d7d507639f9` | undetermined | real by construction (formats the runtime's own version) |
| Duplicati | 52 | EQ006 | `` Duplicati.Library.Common.IO.SystemIOWindows::GetFileAttributes(string) `` | `8b7b9971d237` | undetermined | depends on .NET Framework device-path handling, not checked (carried decision) |
| OpenRA | 0 | EQ006 | `` OpenRA.Mods.D2k.UtilityCommands.D2kMapImporter::.ctor(string,string,OpenRA.Ruleset) `` | `e96d149c45e7` | undetermined | kept from P2-130 OpenRA #2: real difference by hand trace, not executed; non-public |
| OpenRA | 1 | EQ006 | `` OpenRA.FileFormats.ReplayMetadata::RenameFile(string) `` | `cfb3dceb53a3` | false positive | P2-208 |
| OpenRA | 2 | EQ006 | `` OpenRA.Mods.Common.Traits.RotationPaletteEffect::StartsWithAny(string,System.Collections.Generic.HashSet<string>) `` | `ef90ed238201` | undetermined | kept from P2-130 OpenRA #13: real difference by hand trace, not executed; non-public |
| OpenRA | 3 | EQ006 | `` OpenRA.FieldLoader::Load(object,OpenRA.MiniYaml) `` | `42c49f14a91e` | false positive | P2-220 |
| OpenRA | 4 | EQ006 | `` OpenRA.Mods.Common.Traits.PlayerStatistics::ResolveOrder(OpenRA.Actor,OpenRA.Order) `` | `3bcaabd7550e` | undetermined | kept from P2-130 OpenRA #15: no differing ICU/NLS input found |
| OpenRA | 5 | EQ006 | `` OpenRA.Download::CancelAsync() `` | `d1a8b116f5b5` | undetermined | depends on network timing (whether a response is being read), not executed |
| OpenRA | 6 | EQ006 | `` OpenRA.FileSystem.ZipFileLoader.ReadWriteZipFile::.ctor(string,bool) `` | `69b071c8f8e6` | undetermined | kept from P2-130 OpenRA #18: real difference by hand trace, not executed; non-public type |
| OpenRA | 7 | EQ006 | `` OpenRA.CryptoUtil::DecryptString(System.Security.Cryptography.RSAParameters,string) `` | `b31b36a6f035` | undetermined | kept from P2-130 OpenRA #21: real difference by hand trace, not executed |
| OpenRA | 8 | EQ006 | `` OpenRA.FileSystem.Folder::.ctor(string) `` | `713729276b97` | undetermined | kept from P2-130 OpenRA #20: real difference by hand trace, not executed |
| OpenRA | 9 | EQ006 | `` OpenRA.MiniYaml::FromFile(string,bool,System.Collections.Generic.Dictionary<string, string>) `` | `3ed9235b907f` | undetermined | kept from P2-130 OpenRA #11: real difference by hand trace, not executed |
| OpenRA | 10 | EQ006 | `` OpenRA.Mods.Common.Widgets.Logic.GameSaveBrowserLogic::Select(string) `` | `9474d0f8ede2` | false positive | P2-207 |
| OpenRA | 11 | EQ006 | `` OpenRA.float2::Max(OpenRA.float2,OpenRA.float2) `` | `7e1993120319` | undetermined | real difference by hand trace, not executed (a public method given -0 and +0) |
| OpenRA | 12 | EQ006 | `` OpenRA.Mods.Common.Commands.ChatCommands::OnChat(string,string) `` | `050a9ecc9ae4` | undetermined | real difference by hand trace, not executed (an ignorable character before the prefix under ICU) |
| OpenRA | 13 | EQ006 | `` OpenRA.Mods.Common.Widgets.Logic.GameSaveBrowserLogic::Load() `` | `c5f9f1f163d8` | false positive | P2-208 |
| OpenRA | 14 | EQ006 | `` OpenRA.Mods.Cnc.FileFormats.VxlReader::Load(string) `` | `980b381d5c67` | undetermined | real difference by hand trace, not executed (a public method with no caller in the solution) |
| OpenRA | 15 | EQ006 | `` OpenRA.Game::InitializeSettings(OpenRA.Arguments) `` | `9604e8e1297e` | false positive | P2-207 |
| OpenRA | 16 | EQ006 | `` OpenRA.LocalPlayerProfile::.ctor(string,OpenRA.PlayerDatabase) `` | `6b9172d42d0e` | false positive | P2-219 |
| OpenRA | 17 | EQ006 | `` OpenRA.Mods.Common.Widgets.ScrollPanelWidget::Scroll(int,bool) `` | `ff9f320d3d1d` | undetermined | kept from P2-130 OpenRA #7: real difference by hand trace, not executed; non-public |
| OpenRA | 18 | EQ006 | `` OpenRA.Graphics.SpriteFont::MaxLineWidth(string[],System.Func<string, float>) `` | `6bb296f8ee0a` | false positive | P2-220 |
| OpenRA | 19 | EQ006 | `` OpenRA.float2::FromAngle(float) `` | `f397fec71b1b` | undetermined | kept from P2-130 OpenRA #6: real difference by hand trace, not executed |
| OpenRA | 20 | EQ006 | `` OpenRA.StreamExts::ReadString(System.IO.Stream,System.Text.Encoding,int) `` | `dc9636fd3ef4` | undetermined | kept from P2-130 OpenRA #10: real difference by hand trace, not executed |
| OpenRA | 21 | EQ006 | `` OpenRA.FileSystem.Folder::OpenPackage(string,OpenRA.FileSystem.FileSystem) `` | `9711a14506e4` | undetermined | kept from P2-130 OpenRA #4: real difference by hand trace, not executed |
| OpenRA | 22 | EQ006 | `` OpenRA.Platform::get_RuntimeVersion() `` | `699f2c2d5010` | undetermined | kept from P2-130 OpenRA #24: real by construction (reports the runtime's version), not executed |
| OpenRA | 23 | EQ006 | `` OpenRA.FileSystem.Folder::Delete(string) `` | `7fbd69e2d586` | undetermined | kept from P2-130 OpenRA #29: no differing ICU/NLS input found |
| OpenRA | 24 | EQ006 | `` OpenRA.Graphics.ImmutablePalette::.ctor(string,int[]) `` | `398b295eb909` | undetermined | kept from P2-130 OpenRA #23: real difference by hand trace, not executed |
| OpenRA | 25 | EQ006 | `` OpenRA.FileSystem.Folder::Update(string,byte[]) `` | `ef8316585ec5` | undetermined | kept from P2-130 OpenRA #16: no differing ICU/NLS input found |
| OpenRA | 26 | EQ006 | `` OpenRA.Mods.Cnc.FileFormats.HvaReader::Load(string) `` | `0c89dac157f4` | undetermined | real difference by hand trace, not executed (a public method with no caller in the solution) |
| OpenRA | 27 | EQ006 | `` OpenRA.Mods.Common.Widgets.Logic.ObserverStatsLogic::AverageOrdersPerMinute(double) `` | `e0a23402b963` | undetermined | kept from P2-130 OpenRA #22: real difference by hand trace, not executed; non-public |
| OpenRA | 28 | EQ006 | `` OpenRA.Mods.Common.Widgets.Logic.GameSaveBrowserLogic::IsLoadPanelEnabled(OpenRA.Manifest) `` | `290d22a3af1a` | undetermined | kept from P2-130 OpenRA #17: real difference by hand trace, not executed |
| OpenRA | 29 | EQ006 | `` OpenRA.float2::Min(OpenRA.float2,OpenRA.float2) `` | `89d467102967` | undetermined | real difference by hand trace, not executed (a public method given -0 and +0) |
