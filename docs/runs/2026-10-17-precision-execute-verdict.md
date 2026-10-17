# P2-237 verdict: the three migration pairs with execution, P2-212's undetermined results re-adjudicated (2026-10-17)

`gitextensions-8522` (.NET Framework 4.8 to .NET 5), `duplicati-3124` (.NET Framework to .NET 5, first step) and
`openra-17989` (Mono/net472 plus .NET 5), each run in `full` mode with `--execute`, compare mode quick (`compare`'s
default, ADR 0052; every summary records `run.properties.mode` quick), `--jobs 4`, at equiv `de93b9c7` (`main`), one
after the other from one Release build. Per-pair detail is in `docs/runs/2026-10-17-full-execute-<slug>/SUMMARY.md`.
Nothing under `src/`, `tests/` or `tools/` was changed.

Every number here stands next to P2-212's (`docs/runs/2026-10-15-precision-verdict.md`, equiv `bfbf413e`), whose
migration sample (136 results: 1 confirmed, 45 false positive, 90 undetermined) is re-adjudicated here by execution.
This file replaces it as the adjudication of the three migration pairs; the two upgrade pairs keep P2-212's.

**These pairs take no part in ADR 0028's rule table here.** This file measures and adjudicates; it has no continue,
re-scope or stop line. `docs/runs/2026-09-30-full-verdict.md` remains the ADR 0028 verdict.

## Outcome in one line

**Execution confirms 16 of P2-212's 90 undetermined migration results and refutes 1; 71 stay undetermined and 2 are no
longer Divergent. Migration Divergent precision is 35.4% (17 of 48) over the sampled results still Divergent at
`de93b9c7`, and 27.0% (17 of 63) over P2-212's sample as drawn, from 2.2% (1 of 46).** Replay alone confirms one of
them (every EQ002 replay is `not-constructible`); the other 15 were run with `equiv mcp --execute`'s `probe` on inputs
a trace chose. Of the 58 results that were "a real difference by hand trace, not executed", 8 are confirmed, 1 is a
false positive and 49 are still undetermined, almost all because no driver can call them: private members, receivers
with no public parameterless constructor, object, struct or `CancellationToken` arguments, and OpenRA's mod projects
under `probe`. No replay is `not-reproduced`.

## The runtime (criterion 1)

- Each migration's `run.properties.runtimes` (P2-212's SARIF, and these runs again): `gitextensions-8522` legacy net461
  (48 projects), modern net5.0 (43); `duplicati-3124` legacy net471 (51), modern net5.0 (52); `openra-17989` legacy
  net472 (9), modern net5.0 (8). The legacy sides run on the .NET Framework 4.8 CLR that is part of Windows (a driver's
  `app.config` names the project's sku). Only net5.0 was missing.
- Installed: `dotnet-runtime-5.0.17-win-x64.exe` (Microsoft .NET Runtime 5.0.17, x64), the .NET Runtime "Windows x64
  installer" that `dotnet.microsoft.com/download/dotnet/5.0` links, downloaded from `builds.dotnet.microsoft.com`,
  26,578,888 bytes. Its SHA512 (`717ec805...39be2ec5`) equals the one the download page publishes, and its Authenticode
  signature is valid (Microsoft Corporation). Installed side by side, `/install /quiet /norestart`, exit 0. Not the
  SDK, not the ASP.NET Core hosting bundle, not the Windows Desktop runtime.
- No side needed a framework the runtime installer does not carry: a replay driver runs on `Microsoft.NETCore.App`
  only (`ReplayDriverFactory` writes its `runtimeconfig.json` with `desktop: false`), and quick mode runs no member
  drivers. Git Extensions' WinForms assembly loaded and ran under `probe` on `Microsoft.NETCore.App` 5.0.17.
- `dotnet --list-runtimes` before: `Microsoft.AspNetCore.App 10.0.12`; `Microsoft.NETCore.App 6.0.36, 10.0.9, 10.0.12`;
  `Microsoft.WindowsDesktop.App 10.0.9, 10.0.12`. After the install: the same, plus `Microsoft.NETCore.App 5.0.17`.
- Criterion 8: the uninstall was started (the installer's `/uninstall /quiet /norestart`, elevated); its elevation
  prompt was cancelled, and the user then said to keep the runtime installed. `dotnet --list-runtimes` at the end is
  the after-install list above, with `Microsoft.NETCore.App 5.0.17`. Nothing but these runs and the probes below ran on
  it.

## The runs (criterion 2)

| Pair | Wall-clock | Exit | `run.properties.mode` | Runtimes, legacy / modern | `unverified` | Notifications | Config |
|---|---|---|---|---|---|---|---|
| gitextensions-8522 | 488 s (363 s in P2-212) | 1 | quick | net461 (48) / net5.0 (43) | 0 | 0 | none |
| duplicati-3124 | 563 s (447 s) | 1 | quick | net471 (51) / net5.0 (52) | 0 | 0 | `tools/corpus/configs/duplicati-3124.json` (P2-213) |
| openra-17989 | 272 s (217 s) | 1 | quick | net472 (9) / net5.0 (8) | 0 | 0 | `tools/corpus/configs/openra-17989.json` (P2-197) |

No run has a lowering notification, an entry in `run.properties.unverified` or exit 5; exit 1 means Divergent results
exist. Every project loads on both sides. The runs went 2026-10-17 14:02 to 14:24 local time. The `execute` phase took
9.0 s, 9.1 s and 1.3 s; the rest of the extra time is in `lower` (+61%, +24%, +43%) and `verify` (+20%, +23%, +16%), on
all three pairs, OpenRA included, whose every count is P2-212's. Execution does not run in either phase: P2-244 (new)
measures the two commits back to back.

| Pair | | Matched | Congruent | Changed | Solver proves | Unknown | EQ002 | EQ006 |
|---|---|---|---|---|---|---|---|---|
| gitextensions-8522 | P2-212 | 13,592 | 12,722 | 870 | 154 (17.7%) | 556 | 23 | 137 |
| | now | 13,592 | 12,755 | 837 | 154 (18.4%) | 538 | 24 | 121 |
| duplicati-3124 | P2-212 | 6,294 | 5,639 | 655 | 199 (30.4%) | 316 | 23 | 117 |
| | now | 6,294 | 5,654 | 640 | 213 (33.3%) | 309 | 13 | 105 |
| openra-17989 | P2-212 | 10,120 | 9,895 | 225 | 29 (12.9%) | 162 | 0 | 34 |
| | now | 10,120 | 9,895 | 225 | 29 (12.9%) | 162 | 0 | 34 |

Between the two commits, P2-213's committed config makes 8 of the 9 P2-192 results in Duplicati's sample Equivalent
(bounded), and the done precision tickets clear 7 of P2-212's sampled EQ006 false positives (now congruent). The
sampled false positives those tickets left still Divergent are each recorded in the owning ticket's Notes.

## Replay (criterion 3)

Every EQ002 and EQ006 in the three runs carries `properties.replay`.

| Pair | EQ002 | EQ002 replay | EQ006 | EQ006 replay |
|---|---|---|---|---|
| gitextensions-8522 | 24 | `not-constructible` 24 | 121 | `reproduced` 2, `not-applicable` 4, `not-constructible` 115 |
| duplicati-3124 | 13 | `not-constructible` 13 | 105 | `reproduced` 8, `not-applicable` 6, `not-constructible` 91 |
| openra-17989 | 0 | | 34 | `not-constructible` 34 |
| **All three** | **37** | `not-constructible` 37 | **260** | `reproduced` 10, `not-applicable` 10, `not-constructible` 240 |

Why a replay is not constructible, over the 277 (each summary splits it per pair): the divergence is only in the call
trace, which a driver does not observe, 115; the method is not public 59; the model constrains a field, a cast or an
array 49; the receiver type has no public parameterless constructor 27; the call traces split before the outcome 9; a
side gave no comparable outcome 7; an argument the driver cannot build 5; not a method or a property getter 3; both
sides threw where the model does not 2; a by-reference parameter 1. No replay is `not-reproduced`, so criterion 5 files
nothing: no soundness or modelling alarm.

The 10 `reproduced` EQ006 are `GitCommands.AppSettings::GetDictionaryDir()` and `::GetGitExtensionsDirectory()` on Git
Extensions, and eight `SystemIOWindows` and `SystemIOLinux` path members on Duplicati;
`SystemIOWindows::GetFileAttributes(string)` is in P2-212's sample (below). The other nine are not in the sample and are
not adjudicated here; P2-240 says why a `reproduced` needs checking before it counts.

## Re-adjudication (criterion 4)

P2-130's method, unchanged, with execution now possible: **confirmed** needs an executed oracle, here the replay or a
`probe` run on both runtimes whose outcomes differ (a different returned value, a different exception type, or one side
throwing); **false positive** is a hand trace, here with an execution beside it, showing the claimed difference cannot
happen on the pair's runtimes; anything else stays **undetermined**, with its obstacle. Each P2-212 result was matched to
these runs by procedure identity and rule, as P2-212 matched P2-130's.

- Decision: an EQ002 is confirmed by a replay `reproduced` (none here) or by a `probe` run that shows the difference at
  the outcome. `probe` builds its drivers through the same factory replay does (`IReplayDriverFactory.Probe`), so an
  EQ002 it shows differing is executed as a replay would execute it, with the input chosen by a trace instead of by the
  model. One EQ002 is confirmed that way (`LocalHook::GetProcAddress(string,string)`: EasyHook throws for a module that
  is not loaded, and the modern copy returns without calling it).
- Decision: for an EQ006 the execution is the runtime row's own witness carried through the body: a path holding a
  character .NET Framework rejects, a code point the two runtimes' Unicode tables classify differently, a character ICU
  ignores and NLS does not, the runtime's own version. A `not-applicable` replay (equal outcomes on the model's input)
  confirms nothing and refutes nothing: EQ006 claims the member differs, not that the model's input shows it (ADR 0035's
  2026-09-28 clarification). The appendix says for each row whether execution could confirm it, and why not.
- Decision: a `probe` whose outcomes differ for a reason other than the claimed one confirms nothing. Duplicati's
  `WindowsPowerSupplyState::GetSource()` differed only because the modern driver lacked `System.Management`'s
  implementation (P2-240), so it stays undetermined.
- Decision: Duplicati's `TempFile::.ctor(string)` is private and no driver calls a constructor (P2-242); it was run
  through `TempFile::WrapExistingFile(string)`, a public method whose body is that constructor call, and is confirmed by
  it.
- Decision: Duplicati's `SystemIOLinux` members are confirmed on the pair's detected runtimes (net471 against net5.0),
  where they were run directly; Duplicati selects that class on POSIX hosts only, where the legacy side would run on
  Mono. The appendix says so on each of the four rows.
- The tracing and the probes were done by three sub-agents, one per pair, under one written brief; their tables were
  read in full and their results checked against `probe`'s own output. The one replay confirmation
  (`SystemIOWindows::GetFileAttributes(string)`) was probed again on two paths, which agree with it.

| Pair | Rule | Undetermined in P2-212 | Confirmed | False positive | Still undetermined | No longer Divergent |
|---|---|---|---|---|---|---|
| gitextensions-8522 | EQ002 | 16 | 1 | 1 | 14 | 0 |
| | EQ006 | 19 | 3 | 0 | 16 | 0 |
| duplicati-3124 | EQ002 | 8 | 0 | 0 | 6 | 2 (EQ001 bounded) |
| | EQ006 | 24 | 10 | 0 | 14 | 0 |
| openra-17989 | EQ006 | 23 | 2 | 0 | 21 | 0 |
| **All three** | **all** | **90** | **16** | **1** | **71** | **2** |

By what confirmed them: replay 1 (`SystemIOWindows::GetFileAttributes(string)`), `probe` 15. The false positive is
`FileSettingsCacheTests::ctor_FileWatcher_Filter_should_be_default_if_invalid_dir(string)`: each side's test asserts its
own runtime's `FileSystemWatcher.Filter` default, so both pass, as `probe` shows (P2-243, new). The two no longer
Divergent are `Metahash::.ctor(...)` and `HashCalculatingStream::.ctor(...)`, Equivalent by bounded proof now that
P2-213's config maps the hashing helper.

The 71 still undetermined, by obstacle: not callable by a driver (private, a receiver type with no public parameterless
constructor, or abstract) 41; an argument no driver builds (`CancellationToken`, a delegate, an array, a struct, an
object) 10; in OpenRA's mod projects, whose runtime comes only from the pair's config, which `probe` does not read
(P2-241), 8, 3 of them with no other obstacle; a constructor, or struct arguments or a struct result, which no driver
calls, builds or compares (P2-242) 5; not safe to run, the host's settings, files or access rights, or a result that cannot be
compared 4; a difference only in a field written 1; `probe` ran with equal outcomes on every input tried 1
(`EnvironmentPathsProvider::IsValidPath(string)`); `probe` differed for another reason 1 (P2-240).

## Precision with execution (criterion 6)

Precision is confirmed / (confirmed + false positive), undetermined left out. P2-212's 45 false positives and 1
confirmed keep their classification: nothing executed contradicts them, and none has a `reproduced` replay. The
confirmed one, `EasyHook.LocalHook::Release()`, rests on P2-047's replay; at this commit its model differs only in the
call trace, so this run's replay of it is `not-constructible`, which takes nothing back. "Still
Divergent" counts only the sampled results that are Divergent, with the same rule, in these runs: a false positive a done
ticket has since cleared is not a Divergent of these runs.

| Pair | Rule | P2-212 (as drawn) | Now, as drawn | Now, still Divergent at `de93b9c7` |
|---|---|---|---|---|
| gitextensions-8522 | EQ002 | 14.3% (1 of 7) | 22.2% (2 of 9) | 22.2% (2 of 9) |
| | EQ006 | 0% (0 of 11) | 21.4% (3 of 14) | 30.0% (3 of 10) |
| | **all** | **5.6% (1 of 18)** | **21.7% (5 of 23)** | **26.3% (5 of 19)** |
| duplicati-3124 | EQ002 | 0% (0 of 15) | 0% (0 of 15) | 0% (0 of 7) |
| | EQ006 | 0% (0 of 6) | 62.5% (10 of 16) | 76.9% (10 of 13) |
| | **all** | **0% (0 of 21)** | **32.3% (10 of 31)** | **50.0% (10 of 20)** |
| openra-17989 | EQ006 | **0% (0 of 7)** | **22.2% (2 of 9)** | **22.2% (2 of 9)** |
| **Migrations** | EQ002 | 4.5% (1 of 22) | 8.3% (2 of 24) | 12.5% (2 of 16) |
| | EQ006 | 0% (0 of 24) | 38.5% (15 of 39) | 46.9% (15 of 32) |
| | **all** | **2.2% (1 of 46)** | **27.0% (17 of 63)** | **35.4% (17 of 48)** |

- Decision: the scoreboard's migration row takes the last column, 35.4% (17 of 48): it is the precision of the
  Divergent results these runs report, over every one of them P2-212's random draw holds, with only execution added to
  P2-212's adjudication. The as-drawn column is the like-for-like with P2-212's 2.2% and is lower; both are printed so
  neither is read alone. The upgrades keep P2-212's 85.7% (6 of 7).
- If every result still undetermined were real, migration precision over the still-Divergent sample would be at most
  73.9% (88 of 119); P2-212's ceiling was 66.9%.

Of the results P2-212 counted as "a real difference by hand trace, not executed" (57 in its text): its appendix has 58
rows whose obstacle is that and nothing else, 15 EQ002 and 43 EQ006. P2-212's obstacle list counts 8 ICU/NLS rows where
its appendix has 7, and the ticket's 18/39 split also counted Duplicati's three EQ002 that depend on the process
environment, the power hardware or the host OS. Those 58:

| Rule | Hand-trace rows | Confirmed | False positive | Still undetermined |
|---|---|---|---|---|
| EQ002 | 15 | 1 | 1 | 13 |
| EQ006 | 43 | 7 | 0 | 36 |
| **All** | **58** | **8** | **1** | **49** |

The other 32 of the 90: 8 confirmed (the four `SystemIOLinux` rows that depended on the host OS, Duplicati's two
device-path rows, Git Extensions' `DependsOnSelectedRevision(string)`, for which P2-212 had found no differing ICU/NLS
input, and OpenRA's `Platform::get_RuntimeVersion()`, real by construction), 2 no longer Divergent, 22 undetermined.

## Findings and tickets (criterion 5 and beyond)

No replay is `not-reproduced`, so criterion 5 opens no ticket. What execution found about execution itself, the one new
false positive and the slower runs each became a ticket:

| Ticket | Finding |
|---|---|
| P2-240 (M, new) | A replay or `probe` driver copies nothing from a folder that holds a reference assembly, so a NuGet package the project compiled against its `ref/` assembly (`System.Management`) has no implementation at run time and the modern side throws `FileNotFoundException`: a difference the solution would not show, which a replay would call `reproduced` |
| P2-241 (S, new) | `probe` analyses a pair with the default config, never the pair's own: OpenRA's four mod projects, whose net5.0 comes from `tools/corpus/configs/openra-17989.json`, have no detected runtime under `probe`, which blocked 8 sampled rows |
| P2-242 (M, new) | No replay or `probe` driver calls a constructor, passes a struct, or compares a struct or `IntPtr` result (`NotComparable` on both sides, reported equal): 5 sampled rows were blocked by these, and 30 of the runs' `not-constructible` replays name a receiver with no public parameterless constructor or a member that is not a method or a getter |
| P2-243 (S, new) | False EQ002: a test that asserts its own runtime's `FileSystemWatcher.Filter` default on each side |
| P2-244 (S, new) | The three runs took 25% to 34% longer than P2-212's at `bfbf413e`, in `lower` and `verify` on all three pairs, OpenRA's identical results included: measure both commits back to back on one box |

The causes of P2-212's 45 false positives are unchanged and keep their tickets (P2-212's table).

## Scoreboard (criterion 7)

`equiv-scoreboard` was applied over the five large pairs: `gitextensions-9860` and `jellyfin-13023` by their 2026-10-15
runs (P2-212), the three migrations by these runs, so the Measured cells name both dates. Rows rewritten: Proved
Equivalent, Equivalent by congruence, Changed pairs the solver proves, Unknown, Divergent, Divergent precision
(migrations), Left for a reviewer, Run time. The header (5 pairs, 58,477 matched), Soundness, Large pairs that run to a
result and Repeatability did not change, nor did the upgrades' and cleanups' precision.

| Row | Was | Now |
|---|---|---|
| Proved Equivalent | 97.2% (56,838 of 58,477) | 97.3% (56,900 of 58,477) |
| Equivalent by congruence | 95.9% (56,072 of 58,477) | 96.0% (56,120 of 58,477) |
| Changed pairs the solver proves | 31.9% (766 of 2,405) | 33.1% (780 of 2,357) |
| Unknown | 2.2% of pairs (1,298), 54.0% of changed pairs; opaque 573, abstraction 362, budget 326, unaligned loop 33, recursion 4 | 2.2% of pairs (1,273), 54.0% of changed pairs; opaque 573, abstraction 346, budget 319, unaligned loop 31, recursion 4 |
| Divergent | 0.6% of pairs (341), 14.2% of changed pairs | 0.5% of pairs (304), 12.9% of changed pairs |
| Divergent precision | migrations 2.2% (1 of 46) | migrations 35.4% (17 of 48) |
| Left for a reviewer | 2.8% of pairs (1,639) | 2.7% of pairs (1,577) |
| Run time | mean 5 min (3 min to 7 min) | mean 6 min (3 min to 9 min) |

Run time got worse (P2-244); every other row that moved got better.

## Method notes

- `.corpus/` was made new for this ticket (`-Prepare`, `-Fetch` of the three pairs, restore of both sides of each:
  MSBuild with `RestorePackagesConfig` for the legacy sides, `dotnet restore` for the modern ones). Every restore
  succeeded.
- Runs: `./tools/corpus/corpus.ps1 -Compare <slug> -Mode quick -Tag execute -CompareArgs '--jobs', '4', '--execute'`,
  three in a row from one script and one Release build of `de93b9c7`; `-Compare` passed Duplicati's and OpenRA's
  committed configs. The probes ran afterwards, never during a run.
- `probe`: `equiv mcp --execute` from the same build, driven over stdio by a throwaway script under the session's
  scratch directory, one server per batch and one batch per pair at a time; each call loads both solutions again (about
  one minute on Duplicati, three on Git Extensions and OpenRA). 41 probes ran; none errored and every driver compiled.
  Inputs were chosen by tracing each body against its runtime row; none named a real user file or a network address.
- Counts come from the SARIF logs, read with throwaway scripts that reproduce P2-212's summary numbers exactly on
  P2-212's own SARIF. The probe inputs and outcomes, and each row's trace, stay under the session's scratch directory;
  the appendix holds identities, classes, exception types and kinds only.

## Appendix: every re-adjudicated result

The 90 results P2-212 left undetermined, in its sample order per pair. "P2-212 obstacle" is P2-212's, without its
"kept from ..." prefix. "Replay" is `properties.replay` of the same procedure and rule in these runs.

| Pair | # | Rule | Procedure | P2-212 obstacle | Replay | Now | Basis |
|---|---|---|---|---|---|---|---|
| Git Extensions 8522 | 0 | EQ002 | `` EasyHook.HookAccessControl::SetInclusiveACL(int[]) `` | real difference by hand trace, not executed | not-constructible | undetermined | not callable: the receiver type has no public parameterless constructor; an `int[]` argument |
| Git Extensions 8522 | 2 | EQ002 | `` EasyHook.LocalHook::GetProcAddress(string,string) `` | real difference by hand trace, not executed | not-constructible | confirmed | `probe`, a module that is not loaded: legacy threw `DllNotFoundException` (EasyHook), modern returned |
| Git Extensions 8522 | 3 | EQ002 | `` EasyHook.LocalHook::IsThreadIntercepted(int) `` | real difference by hand trace, not executed | not-constructible | undetermined | not callable: the receiver type has no public parameterless constructor |
| Git Extensions 8522 | 6 | EQ002 | `` GitCommands.EnvironmentPathsProvider::IsValidPath(string) `` | real difference by hand trace, not executed | not-constructible | undetermined | `probe` on five path shapes: equal outcomes; what is left needs a host path whose attributes cannot be read |
| Git Extensions 8522 | 7 | EQ002 | `` EasyHook.HookAccessControl::SetExclusiveACL(int[]) `` | real difference by hand trace, not executed | not-constructible | undetermined | not callable: the receiver type has no public parameterless constructor; an `int[]` argument |
| Git Extensions 8522 | 11 | EQ002 | `` GitUI.UserControls.RevisionGrid.Graph.RevisionGraphRevision::get_StartSegments() `` | real by construction (the getter's return type changed), not executed | not-constructible | undetermined | not callable: the receiver type has no public parameterless constructor |
| Git Extensions 8522 | 12 | EQ002 | `` GitUIPluginInterfaces.ManagedExtensibility::TryLoadAssembly(System.IO.FileInfo) `` | real difference by hand trace, not executed | not-constructible | undetermined | not callable by a driver: private |
| Git Extensions 8522 | 13 | EQ002 | `` GitUI.Theming.ThemeLoader::StyleRuleThemeException(ExCSS.StyleRule,string) `` | depends on library code outside the checkout (ExCSS 2.0.6 against 4.1.0), not executed | not-constructible | undetermined | not callable by a driver: private; the difference is in ExCSS |
| Git Extensions 8522 | 14 | EQ002 | `` BugReporter.Program::Main() `` | real by construction, not the migration (build-generated commit constant) | not-constructible | undetermined | not callable by a driver: private; needs a message loop |
| Git Extensions 8522 | 15 | EQ002 | `` Git.hub.Client::setOAuth2Token(string) `` | depends on library code outside the checkout (RestSharp 106.11.4 on both sides), not executed | not-constructible | undetermined | the difference is a field written, which no driver observes |
| Git Extensions 8522 | 16 | EQ002 | `` GitUI.OsShellUtil::PickFolder(System.Windows.Forms.IWin32Window,string) `` | real difference by hand trace, not executed | not-constructible | undetermined | needs a window argument and opens a dialog |
| Git Extensions 8522 | 18 | EQ002 | `` EasyHook.LocalHook::get_HookBypassAddress() `` | real difference by hand trace, not executed | not-constructible | undetermined | not callable: the receiver type has no public parameterless constructor |
| Git Extensions 8522 | 19 | EQ002 | `` CommonTestUtils.MockExecutable.MockProcess::Verify() `` | real difference by hand trace, not executed | not-constructible | undetermined | not callable by a driver: private nested type |
| Git Extensions 8522 | 20 | EQ002 | `` GitUI.Theming.ThemeModule::ResetGdiCaches() `` | real difference by hand trace, not executed | not-constructible | undetermined | not callable by a driver: private |
| Git Extensions 8522 | 21 | EQ002 | `` GitCommandsTests.Settings.FileSettingsCacheTests::ctor_FileWatcher_Filter_should_be_default_if_invalid_dir(string) `` | real difference by hand trace, not executed | not-constructible | false positive | P2-243: each side asserts its own runtime's `FileSystemWatcher.Filter` default, so both pass (`probe`: both returned) |
| Git Extensions 8522 | 22 | EQ002 | `` GitUI.CommitInfo.CommitInfo.BranchComparer::Compare(string,string) `` | real difference by hand trace, not executed: non-public) | not-constructible | undetermined | not callable: the receiver type has no public parameterless constructor |
| Git Extensions 8522 | 23 | EQ006 | `` ConEmu.WinForms.AnsiStreamChunkEventArgs::GetText(System.Text.Encoding) `` | real difference by hand trace, not executed (caller-chosen encoding, ill-formed bytes) | not-constructible | undetermined | not callable: the receiver type has no public parameterless constructor |
| Git Extensions 8522 | 24 | EQ006 | `` GitCommands.GitRefName::GetRemoteName(string) `` | no differing ICU/NLS input found (the call is a Debug.Assert condition) | not-constructible | undetermined | the culture-sensitive call only feeds a `Debug.Assert`; not safe to run (an assert dialog) |
| Git Extensions 8522 | 25 | EQ006 | `` GitUI.NBugReports.BugReportInvoker::Report(System.Exception,bool) `` | depends on process current directory | not-constructible | undetermined | needs an `Exception` argument; shows a dialog or starts a process |
| Git Extensions 8522 | 30 | EQ006 | `` GitCommands.Settings.ConfigFileSettings::CreateSystemWide(bool) `` | real difference by hand trace, not executed (P2-189's row no longer fires; the second Combine on a stored setting does) | not-constructible | undetermined | needs the host's stored setting to hold an invalid path; the result is not comparable |
| Git Extensions 8522 | 31 | EQ006 | `` ICSharpCode.TextEditor.Document.DocumentFactory::CreateFromFile(string) `` | real difference by hand trace, not executed | not-constructible | undetermined | the result (`IDocument`) is not comparable; needs a file on disk |
| Git Extensions 8522 | 33 | EQ006 | `` GitCommands.ExternalLinks.ExternalLink::GetHashCode() `` | real by construction (hash differs per runtime), not executed | not-constructible | undetermined | not callable: the receiver type has no public parameterless constructor |
| Git Extensions 8522 | 34 | EQ006 | `` GitCommandsTests.Config.ConfigFileTest::Cleanup() `` | depends on the file's access rights on the host | not-constructible | undetermined | needs a file the process cannot write (host access rights) |
| Git Extensions 8522 | 37 | EQ006 | `` CommonTestUtils.GitModuleTestHelper::EnsureCreatedInTempFolder(string) `` | no differing ICU/NLS input found | not-constructible | undetermined | not callable by a driver: private |
| Git Extensions 8522 | 39 | EQ006 | `` CommonTestUtils.GitModuleTestHelper::CreateRepoFile(string,string,string) `` | real difference by hand trace, not executed | not-constructible | undetermined | not callable: the receiver type has no public parameterless constructor (an optional parameter); runs git |
| Git Extensions 8522 | 40 | EQ006 | `` GitCommands.FileInfoExtensions::MakeFileTemporaryWritable(string,System.Action<string>) `` | depends on the file's access rights on the host | not-constructible | undetermined | an argument no driver builds (`Action<string>`) |
| Git Extensions 8522 | 41 | EQ006 | `` GitUI.CommandsDialogs.FormVerify::GuessFileTypeWithContent(string) `` | no differing ICU/NLS input found | not-constructible | undetermined | not callable by a driver: private |
| Git Extensions 8522 | 43 | EQ006 | `` GitUI.SpellChecker.SpellCheckerHelper::IsSeparator(char) `` | real difference by hand trace, not executed (a reclassified code point) | not-applicable | confirmed | `probe`, three reclassified code points: legacy returned true, modern false |
| Git Extensions 8522 | 44 | EQ006 | `` ConEmu.WinForms.AnsiStreamChunkEventArgs::GetMbcsText() `` | depends on process environment (ANSI code page) | not-constructible | undetermined | not callable: the receiver type has no public parameterless constructor |
| Git Extensions 8522 | 45 | EQ006 | `` GitExtensions.Plugins.Gource.GourceStart::linkLabel1_LinkClicked(object,System.Windows.Forms.LinkLabelLinkClickedEventArgs) `` | real difference by hand trace, not executed | not-constructible | undetermined | not callable by a driver: private; starts a URL |
| Git Extensions 8522 | 47 | EQ006 | `` ICSharpCode.TextEditor.Document.FileSyntaxModeProvider::GetSyntaxModeFile(ICSharpCode.TextEditor.Document.SyntaxMode) `` | real difference by hand trace, not executed | not-constructible | undetermined | not callable: the receiver type has no public parameterless constructor |
| Git Extensions 8522 | 48 | EQ006 | `` GitUI.Script.ScriptOptionsParser::DependsOnSelectedRevision(string) `` | no differing ICU/NLS input found | not-applicable | confirmed | `probe`, a control character ICU ignores: legacy returned false, modern true |
| Git Extensions 8522 | 49 | EQ006 | `` GitUI.Interops.DwmApi.DwmApi::IsWindows10BuildOrGreater(int) `` | depends on process environment (host executable's manifest) | not-constructible | undetermined | not callable by a driver: private; depends on the host executable's manifest |
| Git Extensions 8522 | 51 | EQ006 | `` ICSharpCode.TextEditor.Util.FileReader::AutoDetect(System.IO.Stream,byte,byte,System.Text.Encoding) `` | depends on process environment (ANSI code page) | not-constructible | undetermined | not callable by a driver: private |
| Git Extensions 8522 | 52 | EQ006 | `` GitCommands.PathUtil::Combine(string,string) `` | real difference by hand trace, not executed | not-applicable | confirmed | `probe`, a path holding a character .NET Framework rejects: legacy returned null, modern returned the combined path |
| Duplicati | 0 | EQ002 | `` Duplicati.GUI.TrayIcon.Program::GetWinformsInstance() `` | real difference by hand trace, not executed: UI | not-constructible | undetermined | not callable by a driver: private; the difference is in the call trace (UI) |
| Duplicati | 3 | EQ002 | `` Duplicati.Library.Backend.S3IAM::CreateUnprivilegedUser(string,string,string) `` | real difference by hand trace, not executed | not-constructible | undetermined | not callable by a driver: private; needs a live IAM client |
| Duplicati | 6 | EQ002 | `` Duplicati.Library.AutoUpdater.UpdaterManager::RunFromMostRecent(System.Reflection.MethodInfo,string[],Duplicati.Library.AutoUpdater.AutoUpdateStrategy) `` | real difference by hand trace, not executed; depends on process environment | not-constructible | undetermined | arguments no driver builds (`MethodInfo`, `string[]`); depends on the process environment |
| Duplicati | 8 | EQ002 | `` Duplicati.Library.Utility.Power.WindowsPowerSupplyState::GetSource() `` | real difference by hand trace, not executed; depends on the machine's power hardware | not-constructible | undetermined | `probe` differed, but by a missing `System.Management` implementation (P2-240), not the claimed cause, which needs power hardware |
| Duplicati | 11 | EQ002 | `` Duplicati.Library.Main.Utility.Metahash::.ctor(System.Collections.Generic.Dictionary<string, string>,Duplicati.Library.Main.Options) `` | depends on library code outside the checkout (FasterHashing 1.3.0, binary only) for a hash name it does not support | none (not Divergent) | no longer Divergent | no longer Divergent: EQ001 bounded |
| Duplicati | 13 | EQ002 | `` Duplicati.Library.Utility.HashCalculatingStream::.ctor(System.IO.Stream,string) `` | depends on library code outside the checkout (FasterHashing 1.3.0, binary only) for a hash name it does not support | none (not Divergent) | no longer Divergent | no longer Divergent: EQ001 bounded |
| Duplicati | 14 | EQ002 | `` Duplicati.Library.Backend.AzureBlob.AzureBlobWrapper::AddFileStream(string,System.IO.Stream,System.Threading.CancellationToken) `` | real difference by hand trace, not executed | not-constructible | undetermined | not callable: the receiver type has no public parameterless constructor; an argument no driver builds (`CancellationToken`) |
| Duplicati | 22 | EQ002 | `` Duplicati.GUI.TrayIcon.Program::TryGetWinforms() `` | real difference by hand trace on a POSIX host (legacy under Mono), not executed; depends on host OS | not-constructible | undetermined | not callable by a driver: private; depends on the host OS |
| Duplicati | 23 | EQ006 | `` Duplicati.Library.Common.IO.SystemIOLinux::PathGetFileName(string) `` | real difference by hand trace on a .NET Framework host, not executed; depends on host OS (SystemIOLinux on POSIX only) | not-applicable | confirmed | `probe`, a path holding a character .NET Framework rejects: legacy threw `ArgumentException`, modern returned (a POSIX-only class, run directly) |
| Duplicati | 24 | EQ006 | `` Duplicati.GUI.TrayIcon.HttpServerConnection::Close() `` | real difference by hand trace, not executed (Thread.Abort throws on net5.0) | not-constructible | undetermined | not callable: the receiver type has no public parameterless constructor (a live server) |
| Duplicati | 26 | EQ006 | `` Duplicati.Library.Common.IO.SystemIOLinux::GetLastWriteTimeUtc(string) `` | real difference by hand trace on a .NET Framework host, not executed; depends on host OS (SystemIOLinux on POSIX only) | not-constructible | confirmed | `probe`, a path holding a character .NET Framework rejects: legacy threw `ArgumentException`, modern `IOException` (a POSIX-only class, run directly) |
| Duplicati | 28 | EQ006 | `` Duplicati.Library.Common.IO.SystemIOLinux::FileDelete(string) `` | real difference by hand trace on a .NET Framework host, not executed; depends on host OS (SystemIOLinux on POSIX only) | not-applicable | confirmed | `probe`, a path holding a character .NET Framework rejects: legacy threw `ArgumentException`, modern `IOException` (a POSIX-only class, run directly) |
| Duplicati | 29 | EQ006 | `` Duplicati.Library.AutoUpdater.UpdaterManager::GetBestUpdateVersion(bool) `` | real difference by hand trace, not executed (the body also changed: an edited key the EQ006 label does not name) | not-constructible | undetermined | not callable by a driver: private |
| Duplicati | 30 | EQ006 | `` Duplicati.Library.Backend.Mega.MegaBackend::PutAsync(string,string,System.Threading.CancellationToken) `` | real difference by hand trace, not executed (a public filename parameter) | not-constructible | undetermined | an argument no driver builds (`CancellationToken`) |
| Duplicati | 31 | EQ006 | `` Duplicati.Library.Backend.Dropbox::Get(string,string) `` | real difference by hand trace, not executed (a public filename parameter) | not-constructible | confirmed | `probe`, a path holding a character .NET Framework rejects: legacy threw `ArgumentException`, modern `IOException`, before any network call |
| Duplicati | 32 | EQ006 | `` Duplicati.Library.Compression.FileArchiveDirectory::CreateFile(string,Duplicati.Library.Interface.CompressionHint,System.DateTime) `` | real difference by hand trace, not executed (a public parameter) | not-constructible | undetermined | not callable: the receiver type has no public parameterless constructor |
| Duplicati | 33 | EQ006 | `` Duplicati.Library.Compression.FileArchiveDirectory::FileExists(string) `` | real difference by hand trace, not executed (a public parameter) | not-constructible | undetermined | not callable: the receiver type has no public parameterless constructor |
| Duplicati | 34 | EQ006 | `` Duplicati.Library.Compression.FileArchiveDirectory::ListFiles(string) `` | real difference by hand trace, not executed (a public parameter) | not-constructible | undetermined | not callable: the receiver type has no public parameterless constructor |
| Duplicati | 36 | EQ006 | `` Duplicati.Library.Backend.WEBDAV::PutAsync(string,string,System.Threading.CancellationToken) `` | real difference by hand trace, not executed (a public filename parameter) | not-constructible | undetermined | an argument no driver builds (`CancellationToken`) |
| Duplicati | 37 | EQ006 | `` Duplicati.Library.Utility.TempFile::.ctor(string) `` | real difference by hand trace, not executed | not-constructible | confirmed | `probe` through `TempFile::WrapExistingFile(string)`, which only calls the private constructor, a path holding a character .NET Framework rejects: legacy threw `ArgumentException`, modern `IOException` |
| Duplicati | 38 | EQ006 | `` Duplicati.Server.LogWriteHandler::SetServerFile(string,Duplicati.Library.Logging.LogMessageType) `` | real difference by hand trace, not executed (an unvalidated command-line option) | not-constructible | confirmed | `probe`, a path holding a character .NET Framework rejects: legacy threw `ArgumentException`, modern `IOException` |
| Duplicati | 39 | EQ006 | `` Duplicati.Library.Common.IO.SystemIOLinux::PathChangeExtension(string,string) `` | real difference by hand trace on a .NET Framework host, not executed; depends on host OS (Duplicati selects `SystemIOLinux` on POSIX hosts only) | not-applicable | confirmed | `probe`, a path holding a character .NET Framework rejects: legacy threw `ArgumentException`, modern returned (a POSIX-only class, run directly) |
| Duplicati | 42 | EQ006 | `` Duplicati.Library.Compression.FileArchiveDirectory::OpenRead(string) `` | real difference by hand trace, not executed | not-constructible | undetermined | not callable: the receiver type has no public parameterless constructor |
| Duplicati | 44 | EQ006 | `` Duplicati.CommandLine.Commands.PeriodicOutput::Dispose() `` | real difference by hand trace, not executed (Thread.Abort throws on net5.0; a timing race) | not-constructible | undetermined | not callable by a driver: private nested type; a timing race |
| Duplicati | 45 | EQ006 | `` Duplicati.Library.Backend.Dropbox::PutAsync(string,string,System.Threading.CancellationToken) `` | real difference by hand trace, not executed (a public filename parameter) | not-constructible | undetermined | an argument no driver builds (`CancellationToken`) |
| Duplicati | 46 | EQ006 | `` Duplicati.Library.Backend.Jottacloud::Get(string,string) `` | real difference by hand trace, not executed | not-constructible | confirmed | `probe`, a path holding a character .NET Framework rejects: legacy threw `ArgumentException`, modern `IOException`, before any network call |
| Duplicati | 47 | EQ006 | `` Duplicati.Library.Backend.SharePointBackend::uploadFileSlicePerSlice(Microsoft.SharePoint.Client.ClientContext,Microsoft.SharePoint.Client.Folder,System.IO.Stream,string,System.Threading.CancellationToken) `` | real difference by hand trace, not executed: non-public | not-constructible | undetermined | not callable by a driver: private; SharePoint client objects and a `CancellationToken` |
| Duplicati | 48 | EQ006 | `` Duplicati.Library.Backend.GoogleCloudStorage.GoogleCloudStorage::PutAsync(string,string,System.Threading.CancellationToken) `` | real difference by hand trace, not executed (a public filename parameter) | not-constructible | undetermined | an argument no driver builds (`CancellationToken`) |
| Duplicati | 49 | EQ006 | `` Duplicati.Library.Common.IO.SystemIOWindows::PathChangeExtension(string,string) `` | depends on .NET Framework device-path handling, not checked | not-constructible | confirmed | `probe`, a path holding a character .NET Framework rejects (three of them): legacy threw `ArgumentException`, modern returned; .NET Framework checks the device-prefixed path too |
| Duplicati | 50 | EQ006 | `` Duplicati.Library.Snapshots.SnapshotBase::GetAttributes(string) `` | real difference by hand trace, not executed | not-constructible | undetermined | not callable: abstract type |
| Duplicati | 51 | EQ006 | `` Duplicati.Library.UsageReporter.ReportSet::DoInitCLR() `` | real by construction (formats the runtime's own version) | not-constructible | undetermined | not callable by a driver: private; the difference is a static field written |
| Duplicati | 52 | EQ006 | `` Duplicati.Library.Common.IO.SystemIOWindows::GetFileAttributes(string) `` | depends on .NET Framework device-path handling, not checked (carried decision) | reproduced | confirmed | replay `reproduced`; `probe` agrees on two paths: legacy threw `ArgumentException`, modern `FileNotFoundException` |
| OpenRA | 0 | EQ006 | `` OpenRA.Mods.D2k.UtilityCommands.D2kMapImporter::.ctor(string,string,OpenRA.Ruleset) `` | real difference by hand trace, not executed; non-public | not-constructible | undetermined | `probe` cannot run it: the mod project's net5.0 comes only from the pair's config (P2-241); private; by hand trace both sides then throw alike, the difference is console output |
| OpenRA | 2 | EQ006 | `` OpenRA.Mods.Common.Traits.RotationPaletteEffect::StartsWithAny(string,System.Collections.Generic.HashSet<string>) `` | real difference by hand trace, not executed; non-public | not-constructible | undetermined | not callable by a driver: private; `probe` cannot run it: the mod project's net5.0 comes only from the pair's config (P2-241) |
| OpenRA | 4 | EQ006 | `` OpenRA.Mods.Common.Traits.PlayerStatistics::ResolveOrder(OpenRA.Actor,OpenRA.Order) `` | no differing ICU/NLS input found | not-constructible | undetermined | not callable: the receiver type has no public parameterless constructor; the difference is in the call trace |
| OpenRA | 5 | EQ006 | `` OpenRA.Download::CancelAsync() `` | depends on network timing (whether a response is being read), not executed | not-constructible | undetermined | not callable: the receiver type has no public parameterless constructor; network timing |
| OpenRA | 6 | EQ006 | `` OpenRA.FileSystem.ZipFileLoader.ReadWriteZipFile::.ctor(string,bool) `` | real difference by hand trace, not executed; non-public type | not-constructible | undetermined | not callable by a driver: private nested type; its reading caller has an `out` parameter |
| OpenRA | 7 | EQ006 | `` OpenRA.CryptoUtil::DecryptString(System.Security.Cryptography.RSAParameters,string) `` | real difference by hand trace, not executed | not-constructible | undetermined | a struct argument (`RSAParameters`) and a matching private key |
| OpenRA | 8 | EQ006 | `` OpenRA.FileSystem.Folder::.ctor(string) `` | real difference by hand trace, not executed | not-constructible | undetermined | a constructor, which no driver calls (P2-242); every caller takes objects |
| OpenRA | 9 | EQ006 | `` OpenRA.MiniYaml::FromFile(string,bool,System.Collections.Generic.Dictionary<string, string>) `` | real difference by hand trace, not executed | not-constructible | confirmed | `probe`, a path holding a character .NET Framework rejects: legacy threw `ArgumentException`, modern `IOException` |
| OpenRA | 11 | EQ006 | `` OpenRA.float2::Max(OpenRA.float2,OpenRA.float2) `` | real difference by hand trace, not executed (a public method given -0 and +0) | not-constructible | undetermined | struct arguments (`float2`, P2-242); no caller with primitive parameters |
| OpenRA | 12 | EQ006 | `` OpenRA.Mods.Common.Commands.ChatCommands::OnChat(string,string) `` | real difference by hand trace, not executed (an ignorable character before the prefix under ICU) | not-constructible | undetermined | `probe` cannot run it: the mod project's net5.0 comes only from the pair's config (P2-241) |
| OpenRA | 14 | EQ006 | `` OpenRA.Mods.Cnc.FileFormats.VxlReader::Load(string) `` | real difference by hand trace, not executed (a public method with no caller in the solution) | not-constructible | undetermined | `probe` cannot run it: the mod project's net5.0 comes only from the pair's config (P2-241) |
| OpenRA | 17 | EQ006 | `` OpenRA.Mods.Common.Widgets.ScrollPanelWidget::Scroll(int,bool) `` | real difference by hand trace, not executed; non-public | not-constructible | undetermined | not callable by a driver: private; `probe` cannot run it: the mod project's net5.0 comes only from the pair's config (P2-241) |
| OpenRA | 19 | EQ006 | `` OpenRA.float2::FromAngle(float) `` | real difference by hand trace, not executed | not-constructible | undetermined | `probe` ran, but a struct result is not compared (P2-242) |
| OpenRA | 20 | EQ006 | `` OpenRA.StreamExts::ReadString(System.IO.Stream,System.Text.Encoding,int) `` | real difference by hand trace, not executed | not-constructible | undetermined | object arguments (`Stream`, `Encoding`) |
| OpenRA | 21 | EQ006 | `` OpenRA.FileSystem.Folder::OpenPackage(string,OpenRA.FileSystem.FileSystem) `` | real difference by hand trace, not executed | not-constructible | undetermined | not callable: the receiver type has no public parameterless constructor |
| OpenRA | 22 | EQ006 | `` OpenRA.Platform::get_RuntimeVersion() `` | real by construction (reports the runtime's version), not executed | not-constructible | confirmed | `probe`: both returned, different strings (each runtime reports its own version) |
| OpenRA | 23 | EQ006 | `` OpenRA.FileSystem.Folder::Delete(string) `` | no differing ICU/NLS input found | not-constructible | undetermined | not callable: the receiver type has no public parameterless constructor; no differing ICU/NLS input found |
| OpenRA | 24 | EQ006 | `` OpenRA.Graphics.ImmutablePalette::.ctor(string,int[]) `` | real difference by hand trace, not executed | not-constructible | undetermined | a constructor, which no driver calls (P2-242) |
| OpenRA | 25 | EQ006 | `` OpenRA.FileSystem.Folder::Update(string,byte[]) `` | no differing ICU/NLS input found | not-constructible | undetermined | not callable: the receiver type has no public parameterless constructor; a `byte[]` argument; no differing ICU/NLS input found |
| OpenRA | 26 | EQ006 | `` OpenRA.Mods.Cnc.FileFormats.HvaReader::Load(string) `` | real difference by hand trace, not executed (a public method with no caller in the solution) | not-constructible | undetermined | `probe` cannot run it: the mod project's net5.0 comes only from the pair's config (P2-241) |
| OpenRA | 27 | EQ006 | `` OpenRA.Mods.Common.Widgets.Logic.ObserverStatsLogic::AverageOrdersPerMinute(double) `` | real difference by hand trace, not executed; non-public | not-constructible | undetermined | not callable by a driver: private; `probe` cannot run it: the mod project's net5.0 comes only from the pair's config (P2-241) |
| OpenRA | 28 | EQ006 | `` OpenRA.Mods.Common.Widgets.Logic.GameSaveBrowserLogic::IsLoadPanelEnabled(OpenRA.Manifest) `` | real difference by hand trace, not executed | not-constructible | undetermined | an object argument (`Manifest`) and the host's support directory; `probe` cannot run it: the mod project's net5.0 comes only from the pair's config (P2-241) |
| OpenRA | 29 | EQ006 | `` OpenRA.float2::Min(OpenRA.float2,OpenRA.float2) `` | real difference by hand trace, not executed (a public method given -0 and +0) | not-constructible | undetermined | struct arguments (`float2`, P2-242); no caller with primitive parameters |

### The other 46 sampled results

P2-212's decided migration results, not re-adjudicated: what each is in these runs, and its replay. "Same rule" means
it is still that Divergent.

| Pair | # | Rule | Procedure | P2-212 | Now in these runs | Replay |
|---|---|---|---|---|---|---|
| Git Extensions 8522 | 1 | EQ002 | `` EasyHook.LocalHook::Release() `` | confirmed (kept from P2-047 (row 18: replay `reproduced`)) | same rule | not-constructible |
| Git Extensions 8522 | 4 | EQ002 | `` GitExtensions.UITests.Script.ScriptRunnerTests::Setup() `` | false positive (P2-187) | same rule | not-constructible |
| Git Extensions 8522 | 5 | EQ002 | `` GitUI.UserControls.RevisionGrid.Graph.RevisionGraphRevision::.ctor(GitUIPluginInterfaces.ObjectId,int) `` | false positive (P2-186) | same rule | not-constructible |
| Git Extensions 8522 | 8 | EQ002 | `` GitUI.Theming.ThemeLoader::.ctor(GitUI.Theming.IThemeCssUrlResolver,GitUI.Theming.IThemeFileReader) `` | false positive (P2-186) | same rule | not-constructible |
| Git Extensions 8522 | 9 | EQ002 | `` GitCommandsTests.SshPathLocatorTest::File_system_access_throwing_should_return_empty_string() `` | false positive (P2-185) | same rule | not-constructible |
| Git Extensions 8522 | 10 | EQ002 | `` GitUI.Avatars.GithubAvatarProvider::GetAvatarAsync(string,string,int) `` | false positive (P2-097) | same rule | not-constructible |
| Git Extensions 8522 | 17 | EQ002 | `` GitCommandsTests.SshPathLocatorTest::Find_on_gitBinDir_parent_throwing_should_return_empty_string() `` | false positive (P2-185) | same rule | not-constructible |
| Git Extensions 8522 | 26 | EQ006 | `` GitCommands.Settings.GitExtSettingsCache::ReadSettings(string) `` | false positive (P2-218) | same rule | not-constructible |
| Git Extensions 8522 | 27 | EQ006 | `` GitCommands.PathUtil::TryFindShellPath(string,out string) `` | false positive (P2-217) | same rule | not-constructible |
| Git Extensions 8522 | 28 | EQ006 | `` GitCommandsTests.Settings.FileSettingsCacheTests::SaveImpl_should_create_folder_if_absent() `` | false positive (P2-208) | EQ001:congruence | none |
| Git Extensions 8522 | 29 | EQ006 | `` GitCommands.AppSettings::get_AvatarImageCachePath() `` | false positive (P2-207) | same rule | not-constructible |
| Git Extensions 8522 | 32 | EQ006 | `` GitUITests.Theming.ThemeLoaderTests::Should_throw_When_cyclic_css_imports() `` | false positive (P2-218) | same rule | not-constructible |
| Git Extensions 8522 | 35 | EQ006 | `` ResourceManager.LinkFactory::ExecuteLink(string,System.Action<global::ResourceManager.CommandEventArgs>,System.Action<string>) `` | false positive (P2-216) | same rule | not-constructible |
| Git Extensions 8522 | 36 | EQ006 | `` GitCommands.GitModule::InTheMiddleOfMerge() `` | false positive (P2-218) | same rule | not-constructible |
| Git Extensions 8522 | 38 | EQ006 | `` GitCommandsTests.Git.EncodingHelperTest::DecodeStringTestUtf8() `` | false positive (P2-216) | EQ001:congruence | none |
| Git Extensions 8522 | 42 | EQ006 | `` GitUI.CommandsDialogs.SettingsDialog.SettingsTreeViewUserControl::AddPage(System.Windows.Forms.TreeNodeCollection,GitUI.CommandsDialogs.SettingsDialog.ISettingsPage,System.Drawing.Image) `` | false positive (P2-216) | EQ001:congruence | none |
| Git Extensions 8522 | 46 | EQ006 | `` GitUI.UserControls.ExListView::.ctor() `` | false positive (P2-216) | EQ001:congruence | none |
| Git Extensions 8522 | 50 | EQ006 | `` GitCommands.Settings.ConfigFileSettings::CreateLocal(GitCommands.GitModule,GitCommands.Settings.ConfigFileSettings,GitUIPluginInterfaces.SettingLevel,bool) `` | false positive (P2-207) | same rule | not-constructible |
| Duplicati | 1 | EQ002 | `` Duplicati.Library.Common.IO.SystemIOWindows::SetAccessControlDir(string,System.Security.AccessControl.DirectorySecurity) `` | false positive (P2-193) | same rule | not-constructible |
| Duplicati | 2 | EQ002 | `` Duplicati.Library.Localization.LocalizationContext::.ctor(System.Globalization.CultureInfo) `` | false positive (P2-213) | same rule | not-constructible |
| Duplicati | 4 | EQ002 | `` Duplicati.Library.Localization.LocalizationService::get_Current() `` | false positive (P2-213) | EQ001:bounded | none |
| Duplicati | 5 | EQ002 | `` Duplicati.Library.Logging.Log::set_CurrentScope(Duplicati.Library.Logging.LogScope) `` | false positive (P2-213) | EQ001:bounded | none |
| Duplicati | 7 | EQ002 | `` Duplicati.Library.Common.IO.SystemIOWindows::GetAccessControlDir(string) `` | false positive (P2-193) | same rule | not-constructible |
| Duplicati | 9 | EQ002 | `` Duplicati.Library.Localization.LocalizationContext::Dispose() `` | false positive (P2-213) | EQ001:bounded | none |
| Duplicati | 10 | EQ002 | `` Duplicati.Library.Main.BackendManager::GetFileHasherStream(System.IO.Stream,System.Security.Cryptography.CryptoStreamMode,out System.Func<string>) `` | false positive (P2-213) | EQ001:bounded | none |
| Duplicati | 12 | EQ002 | `` Duplicati.Library.Utility.Utility::CalculateHash(System.IO.Stream) `` | false positive (P2-206) | same rule | not-constructible |
| Duplicati | 15 | EQ002 | `` Duplicati.Library.Backend.S3IAM::CanCreateUser(string,string) `` | false positive (P2-205) | same rule | not-constructible |
| Duplicati | 16 | EQ002 | `` Duplicati.Library.Common.IO.SystemIOWindows::GetAccessControlFile(string) `` | false positive (P2-193) | same rule | not-constructible |
| Duplicati | 17 | EQ002 | `` Duplicati.Library.Utility.CallContextSettings`1.ContextGuard::.ctor() `` | false positive (P2-213) | EQ001:bounded | none |
| Duplicati | 18 | EQ002 | `` Duplicati.Library.Main.BackendManager::CalculateFileHash(System.IO.Stream) `` | false positive (P2-213) | EQ001:bounded | none |
| Duplicati | 19 | EQ002 | `` Duplicati.Library.Utility.CallContextSettings`1::get_ContextID() `` | false positive (P2-213) | EQ001:bounded | none |
| Duplicati | 20 | EQ002 | `` Duplicati.Library.Utility.CallContextSettings`1.ContextGuard::Dispose() `` | false positive (P2-213) | EQ001:bounded | none |
| Duplicati | 21 | EQ002 | `` Duplicati.Library.Common.IO.SystemIOWindows::SetAccessControlFile(string,System.Security.AccessControl.FileSecurity) `` | false positive (P2-193) | same rule | not-constructible |
| Duplicati | 25 | EQ006 | `` Duplicati.UnitTest.BorderTests::TearDown() `` | false positive (P2-217) | EQ001:congruence | none |
| Duplicati | 27 | EQ006 | `` Duplicati.UnitTest.CommandLineOperationsTests::get_zipAlternativeFilepath() `` | false positive (P2-218) | EQ001:congruence | none |
| Duplicati | 35 | EQ006 | `` Duplicati.Library.Utility.TempFile::Dispose(bool) `` | false positive (P2-217) | EQ001:congruence | none |
| Duplicati | 40 | EQ006 | `` Duplicati.Server.SingleInstance::m_filewatcher_Created(object,System.IO.FileSystemEventArgs) `` | false positive (P2-217) | same rule | not-constructible |
| Duplicati | 41 | EQ006 | `` Duplicati.Library.Utility.TempFile::.ctor() `` | false positive (P2-218) | same rule | not-constructible |
| Duplicati | 43 | EQ006 | `` Duplicati.Library.Snapshots.UsnJournalService::IsPathEnumerated(string) `` | false positive (P2-216) | same rule | not-constructible |
| OpenRA | 1 | EQ006 | `` OpenRA.FileFormats.ReplayMetadata::RenameFile(string) `` | false positive (P2-208) | same rule | not-constructible |
| OpenRA | 3 | EQ006 | `` OpenRA.FieldLoader::Load(object,OpenRA.MiniYaml) `` | false positive (P2-220) | same rule | not-constructible |
| OpenRA | 10 | EQ006 | `` OpenRA.Mods.Common.Widgets.Logic.GameSaveBrowserLogic::Select(string) `` | false positive (P2-207) | same rule | not-constructible |
| OpenRA | 13 | EQ006 | `` OpenRA.Mods.Common.Widgets.Logic.GameSaveBrowserLogic::Load() `` | false positive (P2-208) | same rule | not-constructible |
| OpenRA | 15 | EQ006 | `` OpenRA.Game::InitializeSettings(OpenRA.Arguments) `` | false positive (P2-207) | same rule | not-constructible |
| OpenRA | 16 | EQ006 | `` OpenRA.LocalPlayerProfile::.ctor(string,OpenRA.PlayerDatabase) `` | false positive (P2-219) | same rule | not-constructible |
| OpenRA | 18 | EQ006 | `` OpenRA.Graphics.SpriteFont::MaxLineWidth(string[],System.Func<string, float>) `` | false positive (P2-220) | same rule | not-constructible |
