# P2-124 verdict: the two .NET-to-.NET upgrades again, after the runtime-row fix (2026-10-12)

`jellyfin-13023` (net8.0 to net9.0, a pure bump) and `gitextensions-9860` (net5.0 to net6.0, a bump
with fixes), each run in `full` mode at equiv `1d767f54`, one after the other, on a box that ran
nothing else. Per-pair detail is in `docs/runs/2026-10-12-full-<slug>/SUMMARY.md`. Nothing under
`src/`, `tests/` or `tools/` was changed.

Every number here stands next to the one in P2-066's verdict, `docs/runs/2026-10-03-upgrade-verdict.md`
(equiv `8e0ed3c`), which this file replaces as the adjudication of the upgrade pairs.

**These pairs take no part in ADR 0028's rule table.** No threshold is applied to them, no agent
median includes them, and this file has no continue, re-scope or stop line.

## Outcome in one line

**The runtime-row fix did what it was expected to: EQ006 falls from 161 to 4, Divergent from 173 to
15, and the solver now proves half of each pair's changed pairs, where it proved none of Jellyfin's.
Of the 15 Divergent, 6 are real behaviour changes and 9 are false positives with two causes, each
now a ticket.** No behaviour change either pull request made is reported Equivalent.

## How these runs differ from P2-066's

| | P2-066 (2026-10-03) | These runs |
|---|---|---|
| equiv commit | `8e0ed3c` | `1d767f54` |
| Compare mode | before the modes (ADR 0049): bound 3, resourceLimit 5000000 | `quick`, the default (ADR 0052): bound 3, resourceLimit 2000000, timeoutMs 60000. A `thorough` run of each pair followed and is reported in its own section |
| Pairs verified at a time | 1 | 4 (`--jobs 4`, as the 2026-10-08 runs) |
| Machine | the two runs shared it | one run at a time, nothing else running |
| Wall-clock | 1h32m and 1h11m | 164 s and 264 s |

- Decision: the ticket names no compare mode. `equiv-corpus-run` asks for thorough for a verdict and
  `equiv-scoreboard` takes only quick runs as a source, and criterion 6 applies the scoreboard. So
  both were run: the tables below are the quick runs, which the scoreboard rests on, and the
  thorough runs' differences have their own section, adjudicated the same way.
- Decision: `--jobs 4`, the value the 2026-10-08 runs of these pairs used, so the times compare
  (`equiv-corpus-run` section 5).

So a count can differ from P2-066's because of a fix or because of the smaller resource limit.
The 2026-10-08 scoreboard rerun (`docs/runs/2026-10-08-scoreboard-rerun.md`) is the same mode and
`--jobs` as these runs, eleven days of fixes earlier.

## The table

Shares are over ADR 0034's changed pairs. An Unknown that is `unmatched-overload` is not a matched
pair and is left out.

| Pair | | Matched pairs | Congruent pairs | Changed pairs | Proved Equivalent | Unknown | Divergent |
|---|---|---|---|---|---|---|---|
| jellyfin-13023 | P2-066 | 14503 | 13927 (96.0%) | 573 | 0 | 462 (80.6%) | 111 (19.4%): EQ002 9, EQ006 102 |
| | now | 14398 | 14276 (99.2%) | 122 | 62 (50.8%): bounded 51, lockstep-induction 11 | 59 (48.4%) | 1 (0.8%): EQ002 1, EQ006 0 |
| gitextensions-9860 | P2-066 | 14020 | 13295 (94.8%) | 722 | 239 (33.1%): bounded 227, lockstep-induction 12 | 421 (58.3%) | 62 (8.6%): EQ002 3, EQ006 59 |
| | now | 14073 | 13468 (95.7%) | 605 | 300 (49.6%): bounded 283, lockstep-induction 17 | 291 (48.1%) | 14 (2.3%): EQ002 10, EQ006 4 |
| Both | P2-066 | 28523 | 27222 (95.4%) | 1295 | 239 (18.5%) | 883 (68.2%) | 173 (13.4%) |
| | now | 28471 | 27744 (97.4%) | 727 | 362 (49.8%) | 350 (48.1%) | 15 (2.1%) |

Jellyfin's matched pairs fall by 105 because code the regex source generator emits is no longer a
procedure (P2-118; 145 and 143 generated procedures are left out) while controller actions that
were `unmatched-overload` are now matched (P2-119).

Unknown by reason, all Unknown results:

| Pair | | opaque | timeout | abstraction | unaligned-loop | recursion | unmatched-overload |
|---|---|---|---|---|---|---|---|
| jellyfin-13023 | P2-066 | 243 | 138 | 52 | 29 | 0 | 63 |
| | now | 20 | 22 | 10 | 7 | 0 | 1 |
| gitextensions-9860 | P2-066 | 253 | 98 | 52 | 16 | 2 | 20 |
| | now | 227 | 37 | 25 | 1 | 1 | 20 |

Every project loads on both sides of both pairs. Every detected runtime comes from the project's
target framework attribute, except `Git.hub` on both sides of `gitextensions-9860`, which takes its
host project's.

### Why unedited code is not congruent

| Pair | | Changed pairs in edited files | In byte-identical files | What the byte-identical ones are |
|---|---|---|---|---|
| jellyfin-13023 | P2-066 | 49 | 524 | 353 hold a `String.Equals` call with a `StringComparison`, whose row had no change point. 63 hold a call that binds to a new .NET 9 overload. |
| | now | 24 | 98 | 60 are proved by the solver, 18 are `timeout`, 16 `opaque`, 4 `abstraction`. None is Divergent. |
| gitextensions-9860 | P2-066 | 25 | 697 | 417 hold an interpolated string, which binds to `string.Format` on net5.0 and to `DefaultInterpolatedStringHandler` on net6.0. The solver proves 223 of them. |
| | now | 25 | 580 | 402 hold an interpolated string by the same text scan, and the solver proves 262 of them. 49 are `extern` methods (P2-165). |

The pure bump is now what it should be: 98 unedited bodies out of congruence, from 524, and 60 of
them proved. The bump with fixes still has 580, because the interpolated-string binding really does
differ between net5.0 and net6.0; the solver recovers more of it than it did.

## The 353 Jellyfin bodies and the 120 false EQ006 (criterion 4)

P2-066's list of the 353 bodies was not kept. The same text scan (a changed pair of P2-066's run, in
a byte-identical file, whose body holds an `Equals` call that takes a `StringComparison`) was run
again over P2-066's SARIF and finds 350 of them; the 3 it misses are within the 13 bodies in
generated files this checkout does not have. Each was then looked up in the new run by identity, or
by file and line where P2-119 renamed a controller action to its route.

| Set | Congruent | Proved by the solver | Unknown | Divergent |
|---|---|---|---|---|
| The 353 Jellyfin bodies (350 found again) | 334 | 6 | 10 (`timeout` 9, `opaque` 1) | 0 |
| The 120 false EQ006 of P2-066 | 118 | 2 | 0 | 0 |
| of them jellyfin-13023 (97) | 96 | 1 | 0 | 0 |
| of them gitextensions-9860 (23) | 22 | 1 | 0 | 0 |
| All 161 EQ006 of P2-066 | 154 | 5 | 1 (`opaque`) | 1 |

In P2-066's run the 350 bodies were 87 EQ006 and 263 Unknown (`opaque` 118, `timeout` 112,
`abstraction` 20, `unaligned-loop` 13). So the row with no change point cost Jellyfin 263 Unknowns
as well as its false EQ006, and 253 of those are gone with it.

The one EQ006 left of the 161 is
`GitUI.CommandsDialogs.RevisionFileTreeController::Find(System.Windows.Forms.TreeNodeCollection,string)`,
which P2-066 already classed a false positive.

## EQ006 by runtime-change row

A pair's interval is (older runtime, newer runtime].

### jellyfin-13023, interval (net8.0, net9.0]

| Row (member) | `changedIn` | EQ006, P2-066 | EQ006, now |
|---|---|---|---|
| `System.String::Equals(string,System.StringComparison)` and the static form | null then | 92 | 0 |
| `System.String::Split(char[],System.StringSplitOptions)` and the `string[]` form | null then | 5 | 0 |
| `System.String::GetHashCode(` | null then | 2 | 0 |
| `System.Char::IsLetterOrDigit(char)` | null then | 1 | 0 |
| `System.IO.File::ReadAllText(string,System.Text.Encoding)` | null then | 1 | 0 |
| `System.IO.File::WriteAllText(string,string,System.Text.Encoding)` | null then | 1 | 0 |

No row fires. The census counts 7 call sites of 1 runtime-change member in 3 pairs, from 817 call
sites of 14 members in 278 pairs.

### gitextensions-9860, interval (net5.0, net6.0]

| Row (member) | `changedIn` | EQ006, P2-066 | EQ006, now |
|---|---|---|---|
| The 15 rows that had no change point (`String.Equals` 12, `String.GetHashCode` 7, `Environment.GetFolderPath` 7, `Encoding.Default` 6, `Char.IsLetterOrDigit` 5, `String.Split` 4, `WebUtility.HtmlEncode` 4, `File.ReadAllText` 2, the two `System.Net.Http.Headers` constructors 4, `IsolatedStorageFileStream` 1, `File.WriteAllBytes` 1, `Environment.GetEnvironmentVariable` 1, `Math.Min` 1, `File.WriteAllText` 1) | null then | 56 | 0 |
| `System.Windows.Forms.ListViewGroupCollection::` | net6.0 | 1 | 0 |
| `System.IO.FileStream::get_Position(` | net6.0 | 1 | 0 |
| `System.Windows.Forms.TreeNodeCollection::get_Item(` | net6.0 | 1 | 4 |

**No EQ006 cites a row whose change point lies outside its pair's interval**, as in P2-066. All 4
cite one row whose `changedIn` is the pair's newer runtime. Three of the 4 are new because their
pairs were Unknown in P2-066's run (`unaligned-loop` 2, `recursion` 1) and are decided now.

## Adjudication of every EQ002 and EQ006 (criterion 3)

P2-066's method: **confirmed**, **false positive** (a hand trace shows the claimed difference
cannot be reached on the pair's runtimes) or **undetermined**. A result P2-066 adjudicated whose
procedure and row are unchanged keeps its classification. Replay gave nothing again: neither pair's
runtimes are installed, so no result has `replay` or `proofMethod: observed`.

- Decision: an EQ002 whose only difference is a `using` the pull request adds to a disposable is
  confirmed: the modern side calls `Dispose` where the legacy side never did. It is the change the
  author made, and it is not an "added effect-free BCL call" of P2-066's fourth rule, because
  disposing releases the resource.
- Decision: an EQ002 in a byte-identical file whose two call traces differ only in how an
  interpolated string reaches `StringBuilder.Append` is a false positive: both bindings append the
  same text (read from the two bodies and the two traces' member names; no value was compared).

| Pair | Rule | | Confirmed | False positive | Undetermined | Total | Precision |
|---|---|---|---|---|---|---|---|
| jellyfin-13023 | EQ002 | P2-066 | 2 | 3 | 4 | 9 | 40.0% (2 of 5) |
| | | now | 1 | 0 | 0 | 1 | 100% (1 of 1) |
| jellyfin-13023 | EQ006 | P2-066 | 0 | 97 | 5 | 102 | 0% (0 of 97) |
| | | now | 0 | 0 | 0 | 0 | n/a |
| gitextensions-9860 | EQ002 | P2-066 | 3 | 0 | 0 | 3 | 100% (3 of 3) |
| | | now | 5 | 5 | 0 | 10 | 50.0% (5 of 10) |
| gitextensions-9860 | EQ006 | P2-066 | 0 | 26 | 33 | 59 | 0% (0 of 26) |
| | | now | 0 | 4 | 0 | 4 | 0% (0 of 4) |
| **Both** | **all** | **P2-066** | **5** | **126** | **42** | **173** | **3.8% (5 of 131)** |
| | | **now** | **6** | **9** | **0** | **15** | **40.0% (6 of 15)** |

### Confirmed (6)

| Pair | Procedure | What changed | Basis |
|---|---|---|---|
| jellyfin-13023 | `Jellyfin.Server.Helpers.StartupHelpers::PerformStaticInitialization()` | No longer sets the two `ServicePointManager` properties | P2-066; procedure unchanged |
| gitextensions-9860 | `GitExtensions.Program::Main()` | Calls `Application.SetHighDpiMode` | P2-066; procedure unchanged |
| gitextensions-9860 | `ResourceManager.Xliff.TranslationSerializer::Serialize(ResourceManager.Xliff.TranslationFile,string)` | Writes through an `XmlWriter` in place of a `StreamWriter` | P2-066; procedure unchanged |
| gitextensions-9860 | `BugReporter.Program::Main()` | Passes a different commit hash, from a generated `ThisAssembly` constant | P2-066; procedure unchanged. Not a change the upgrade made |
| gitextensions-9860 | `GitExtensions.Plugins.Gource.GourcePlugin::DownloadFile(string,string)` | The `WebClient` is now disposed | The pull request adds `using` to the declaration; the modern trace has the one extra `Dispose` call. Unknown (`opaque`) in P2-066's run |
| gitextensions-9860 | `GitExtensions.Plugins.Gource.GourcePlugin::SearchForGourceUrl()` | The `WebClient` is now disposed | The same edit, the same trace difference. Unknown (`opaque`) in P2-066's run |

### False positives (9)

| Cause | Results | Ticket |
|---|---|---|
| `StringBuilder.Append($"...")` binds to `Append(string)` on net5.0 and to `AppendInterpolatedStringHandler` on net6.0; the traces differ and the text appended does not. In `GitCommands.Patches.PatchManager::GetSelectedLinesAsNewPatch(...)`, `PatchManager::ToPatch(...)`, `GitCommands.Patches.Chunk::ToPatch(...)`, `BugReporter.ErrorReportUrlBuilder::Build(...)` and `BugReporter.Serialization.SerializableException::ToString()`, all in byte-identical files. All five were Unknown in P2-066's run (`timeout` 2, `opaque` 2, `abstraction` 1) | 5 EQ002 | P2-164 (new) |
| The `TreeNodeCollection::get_Item(` row fires on a read of the indexer; only assigning a null node reaches the change. In `GitUI.CommandsDialogs.RevisionFileTreeController::Find(...)` (P2-066's classification kept), `GitExtensions.UITests.CommandsDialogs.FormBrowse_LeftPanel_ReorderNodesTest::ValidateOrder(...)`, `BugReporter.ExceptionDetails::Initialize(...)` and `BugReporter.ExceptionDetails::FillInnerExceptionTree(...)` | 4 EQ006 | P2-163 (new; P2-075 is done and left this row) |

### Where P2-066's other results went

| P2-066 result | Now |
|---|---|
| The 120 false EQ006 from rows with no change point | 118 congruent, 2 proved (above) |
| `ListViewGroupCollection` row, 1 false EQ006 (P2-075) | congruent |
| `FileStream.Position`, 1 false EQ006 (P2-114) | congruent |
| `Enum.GetValues<T>()` and `Enum.IsDefined<T>(v)`, 2 false EQ002 (P2-117) | both proved Equivalent |
| An added `ConfigureAwait(false)`, 1 false EQ002 (P2-071) | Unknown (`abstraction`) |
| The 38 undetermined EQ006 (29 with no documented change point, 9 `String.GetHashCode`) | 34 congruent, 3 proved, 1 Unknown (`opaque`), 0 Divergent: the question is closed by the change points P2-113 gave the rows, not by a measurement on the two runtimes |
| 4 undetermined EQ002 in code the regex source generator emits (P2-118) | no longer procedures |
| `Emby.Server.Implementations.ApplicationHost::GetCertificate(string,string)`, confirmed EQ002 | Unknown (`abstraction`, on `System.String::IsNullOrWhiteSpace(string)`) |

The last row is the one loss: a real behaviour change that was reported Divergent is now Unknown.
It is not reported Equivalent, so nothing is wrong, but the reviewer is told less. One result; no
ticket.

## What the pull requests changed, and what `equiv` said

No behaviour change either pull request made is reported Equivalent. Every solver-proved pair in a
file either pull request edits was read against the diff: none is in an edited method.

| Pair | Real behaviour changes in the diff | Reported, P2-066 | Reported, now |
|---|---|---|---|
| jellyfin-13023 | `StartupHelpers::PerformStaticInitialization()` | EQ002 | EQ002 |
| | `ApplicationHost::GetCertificate(string,string)` | EQ002 | Unknown (`abstraction`) |
| | `PluginManager::TryGetPluginDlls(...)` | Unknown (`timeout`) | Unknown (`timeout`) |
| | `ProbeResultNormalizer::GetMpegTimestamp(string)` | Unknown (`abstraction`) | Unknown (`abstraction`) |
| gitextensions-9860 | `Program::Main()` | EQ002 | EQ002 |
| | `TranslationSerializer::Serialize(...)` | EQ002 | EQ002 |
| | `GourcePlugin::DownloadFile(...)` and `::SearchForGourceUrl()` (not listed by P2-066) | Unknown (`opaque`) | EQ002, EQ002 |

Jellyfin's behaviour-preserving edits (`Enum.Parse<T>`, `Enum.GetValues<T>`, `Enum.IsDefined<T>`)
are now proved where the solver reaches them: 2 of the pairs in its edited files are Equivalent,
from none.

## Thorough runs

Each pair was run again in compare mode thorough, on the same build and checkouts (`--jobs 4`). The
first pass is the quick run; a budget pass and an IL pass then take the pairs still Unknown, at
bound 3, resourceLimit 30000000 and timeoutMs 600000 (ADR 0049, as P2-134 set it). No Divergent
or Equivalent of the quick runs changes in the thorough runs.

| Pair | | Wall-clock | Proved Equivalent | Unknown | Divergent |
|---|---|---|---|---|---|
| jellyfin-13023 | quick | 164 s | 62 (50.8%) | 59 (48.4%) | 1 (0.8%): EQ002 1, EQ006 0 |
| | thorough | 1725 s | 64 (52.5%) | 56 (45.9%) | 2 (1.6%): EQ002 2, EQ006 0 |
| gitextensions-9860 | quick | 264 s | 300 (49.6%) | 291 (48.1%) | 14 (2.3%): EQ002 10, EQ006 4 |
| | thorough | 4899 s | 302 (49.9%) | 272 (45.0%) | 31 (5.1%): EQ002 24, EQ006 7 |

| Pair | Pairs the budget pass took | Pairs the IL pass took | Results decided by the budget pass | By the IL pass |
|---|---|---|---|---|
| jellyfin-13023 | 30 (1515 s) | 1 | 12 | 0 |
| gitextensions-9860 | 52 (4147 s) | 42 (426 s) | 20 | 18 |

So thorough costs 10 and 19 times quick on these pairs, proves 4 more pairs between them, and adds
18 Divergent, none of which is a confirmed behaviour change.

The 18 Divergent the thorough runs add, adjudicated as above:

| Pair | Results | Classification | Cause | Ticket |
|---|---|---|---|---|
| gitextensions-9860 | 14 EQ002, `decidedBy: il-pass`, all in byte-identical files | false positive | An interpolated string with a hole P2-086 does not lower (a `DateTime`, a `double` with a format, an enum, a struct) is opaque to the first pass. From IL it is `string.Format` on net5.0 and the `DefaultInterpolatedStringHandler` members on net6.0, two call traces for the same text | P2-135, existing and open (Notes line added) |
| gitextensions-9860 | 1 EQ006, `TreeNodeCollection::get_Item(`, in `GitUI.CommandsDialogs.SettingsDialog.SettingsTreeViewUserControl::GotoPage(...)` | false positive | A read of the indexer, as the four of the quick run | P2-163 |
| gitextensions-9860 | 1 EQ006, `System.Text.StringBuilder::AppendLine(ref ` (changedIn net6.0), in `GitCommandsTests.Git.GitBlameCommitTests::TestToString()` | false positive | The row records that holes which read or change the builder see a different state. The three holes read two locals and call a method on a third; none touches the builder | P2-164 (Notes line) |
| gitextensions-9860 | 1 EQ006, `System.Environment::get_ProcessorCount(` (changedIn net6.0), in `GitUITests.CancellationTokenSequenceTests::Concurrent_callers_to_Next_only_result_in_one_non_cancelled_token_being_issued()` | undetermined | The count differs between the two runtimes only for a process with an affinity mask or a job CPU limit, and neither runtime is installed to measure it | none: the row is inside the interval and reachable |
| jellyfin-13023 | 1 EQ002, `decidedBy: budget-pass`, in `Jellyfin.Networking.Manager.NetworkManager::EnforceBindSettings(...)` | false positive | The method's text is the same on both sides; the pull request retyped the field it reads from `IReadOnlyList<IPData>` to `List<IPData>`. In the counterexample both sides throw the same exception after the same two calls and differ only in the content of an array heap no reached statement writes | P2-166 (new) |

Thorough precision over both pairs: 6 confirmed, 26 false positives, 1 undetermined of 33, which is
18.8% (6 of 32), against quick's 40.0% (6 of 15). The README's scoreboard rests on the quick runs
(`equiv-scoreboard`), so its precision row is the quick figure.

## Findings (criterion 5)

No run crashed: 0 unverified procedures and 0 notifications in all four runs. 5% of the changed
pairs is 7 on `jellyfin-13023` and 31 on `gitextensions-9860`.

| Ticket | Finding | Pairs |
|---|---|---|
| P2-163 (S, new) | The `TreeNodeCollection::get_Item(` row fires on every read of the indexer: all 4 EQ006 left on the two upgrades, all false | gitextensions-9860 |
| P2-164 (M, new) | `StringBuilder.Append` of an interpolated string is Divergent between net5.0 and net6.0: 5 false EQ002 | gitextensions-9860 |
| P2-165 (M, new) | An unedited `extern` method is Unknown (`no-body`) on every pair that crosses a runtime (ADR 0054 decision 4): 53 of 605 changed pairs (8.8%), the pair's largest opaque reason, and 2 of Jellyfin's 122 | both |
| P2-166 (M, new) | Thorough's budget pass reports EQ002 on a body whose text is unchanged and whose field was retyped: 1 false EQ002, thorough mode only | jellyfin-13023 |
| P2-102 and P2-139, existing | `InterpolatedString` is the first reason of 45 Unknowns (7.4%). P2-139 counted its forms on this pair on 2026-10-11 and found none at 5% alone; P2-102 owns one form. No new ticket | gitextensions-9860 |
| P2-115, existing and open | Jellyfin's restore still needs `--configfile` | jellyfin-13023 |

Below 5% and not filed: on `gitextensions-9860`, `DelegateCreation` 26 (4.3%), `LocalFunction` 25
(4.1%) and `rebound-call` 25 (4.1%) as first reasons; on `jellyfin-13023` no opaque reason is the
first reason of more than 3 Unknowns. `timeout` is 22 of Jellyfin's 122 changed pairs (18.0%) and 37
of Git Extensions' 605 (6.1%). `unaligned-loop` is 7 of Jellyfin's (5.7%), all seven in
`Jellyfin.Networking.Manager.NetworkManager`, whose field the pull request retyped. Neither is an
opaque reason or a false-positive cause, so criterion 5 files nothing for them; the roadmap's "Not
on the milestone" paragraph says what is known about the solver-budget Unknowns.

Not a result of `equiv`: `corpus.ps1 -Packages` stops with "The property 'Files' cannot be found on
this object" on both pairs, so the summaries have no package-change line.

## Scoreboard (criterion 6)

`equiv-scoreboard` was applied, from the two quick runs and the 2026-10-08 run of
`gitextensions-8522`. Rows rewritten: the header's matched-pair count, Proved Equivalent, Equivalent
by congruence, Changed pairs the solver proves, Unknown, Divergent, Divergent precision (upgrades:
6 of 15, from 5 of 131), Left for a reviewer, Run time, and the open soundness tickets named in the
Soundness row (P2-146 is done; P2-161 and P2-162 are open). Rows left alone: the seeded count in
Soundness, Large pairs that run to a result, Repeatability. The sentences and detail tables under
the scoreboard changed only where one of those numbers is in them.

## Method notes

- **Runs.** Four, one after the other, from one build of `1d767f54`: `jellyfin-13023` quick,
  `gitextensions-9860` quick, then each in thorough. `.corpus/` was made new for this ticket
  (`-Prepare`, `-Fetch`, restore), so nothing is carried over from P2-066's checkouts.
- **P2-066's results** were read from its SARIF files, which are still on the box, and matched to
  the new results by procedure identity.
- **`--execute`.** Not run (ticket Out of scope): the box has Microsoft.NETCore.App 6.0.36 and
  10.0.x, and no .NET 5, 8 or 9.
- **Tests.** Neither pair has a `verify_command`; no upstream test was run.
- Source text and model values stay under `.corpus/` (`docs/runs/README.md`).
