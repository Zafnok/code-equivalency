# P2-247 verdict: every Divergent of the five large pairs with execution, P2-237's 71 and a fresh exposure sample (2026-10-20)

`gitextensions-8522` (.NET Framework 4.8 to .NET 5), `gitextensions-9860` (net5.0 to net6.0), `jellyfin-13023` (net8.0 to
net9.0), `duplicati-3124` (.NET Framework 4.7.1 to .NET 5) and `openra-17989` (Mono/net472 plus .NET 5), each run in
`full` mode with `--execute`, compare mode quick (`compare`'s default, ADR 0052; every run's `run.properties.mode` is
quick), `--jobs 4`, with each pair's committed config, at equiv `4b2e0df6` (`main`), one after the other from one
Release build. Per-pair detail is in `docs/runs/2026-10-20-full-execute-<slug>/SUMMARY.md`. Nothing under `src/`,
`tests/` or `tools/` was changed.

This file replaces P2-212's (`docs/runs/2026-10-15-precision-verdict.md`) and P2-237's
(`docs/runs/2026-10-17-precision-execute-verdict.md`) as the adjudication of these pairs: it adjudicates every EQ002 and
EQ006 of the five runs (no sample), re-adjudicates P2-237's 71 undetermined results, and adjudicates a fresh random
sample of runtime exposures (EQ007, ADR 0060), 15 witnessed and 15 unwitnessed per migration. It names "Scoreboard
milestone 4" (`docs/ROADMAP.md`).

**These pairs take no part in ADR 0028's rule table here** (ADR 0060 decision 7). This file measures and adjudicates; it
has no continue, re-scope or stop line.

## Outcome in one line

**Migration Divergent precision is 52.2% (24 of 46) over every EQ002 and EQ006 a default run reports, from 11.1% (2 of
18) over P2-212's sample, and the upgrades' is 100% (6 of 6), from 85.7% (6 of 7); 19 of the 65 migration Divergents
stay undetermined. Of P2-237's 71 undetermined results, 40 are now confirmed, 4 false positives and 27 still
undetermined. Of 80 fresh exposures, witnessed ones are 70.8% precise (17 of 24) and unwitnessed ones 21.4% (6 of
28).** Execution itself produced three findings: a replay driver that dies on an assertion ends a run (P2-258, exit 5 on
`gitextensions-8522`); a .NET Framework driver runs with the quirks of an application that targets no framework, so
every `\\?\` path fails on the legacy side where the solution's own host accepts it (P2-259), which made 9 replays
`reproduced` here and two of P2-237's confirmations false; and `Math.Sin`/`Math.Cos` may differ between the runtimes on
ordinary arguments the runtime table does not cover (P2-272, a check before any soundness claim). No replay is
`not-reproduced`.

## The runtime (criterion 1)

- `dotnet --list-runtimes` before any run (the machine): `Microsoft.AspNetCore.App 10.0.12`; `Microsoft.NETCore.App
  5.0.17, 6.0.36, 10.0.9, 10.0.12`; `Microsoft.WindowsDesktop.App 10.0.9, 10.0.12`. `Microsoft.NETCore.App` 5.0.17 is
  the one P2-237 installed and the user kept.
- Against each pair's `run.properties.runtimes` (P2-238's summaries, and these runs again): `gitextensions-8522` net461
  (48) / net5.0 (43); `gitextensions-9860` net5.0 (43) / net6.0 (43); `jellyfin-13023` net8.0 (37) / net9.0 (37);
  `duplicati-3124` net471 (51) / net5.0 (52); `openra-17989` net472 (9) / net5.0 (8). The .NET Framework sides run on
  the 4.8 CLR that is part of Windows. Missing: `Microsoft.NETCore.App` 8.0 and 9.0 (Jellyfin), and
  `Microsoft.WindowsDesktop.App` 5.0 and 6.0, which a replay driver of a Windows Forms project runs on since P2-253 (Git
  Extensions' modern side, `gitextensions-9860`'s two sides).
- Asked, the user chose a private `dotnet` root over a machine-wide install, as P2-253 made one: a copy of `dotnet.exe`
  in the session's scratch folder, junctions to the machine install's `host`, `sdk`, `packs` and every installed shared
  framework version, and four runtimes from Microsoft's zips (`builds.dotnet.microsoft.com`):
  `dotnet-runtime-8.0.32-win-x64.zip`, `dotnet-runtime-9.0.21-win-x64.zip` and `windowsdesktop-runtime-6.0.36-win-x64.zip`,
  each SHA512 equal to the one the 8.0, 9.0 and 6.0 release metadata publishes, and
  `windowsdesktop-runtime-5.0.17-win-x64.zip`, for which the 5.0 metadata lists no hash, so each of its 280 binaries was
  checked instead (all four zips: every DLL and EXE has a valid Authenticode signature by Microsoft Corporation).
  `equiv` and its drivers ran with that root first on `PATH` and as `DOTNET_ROOT`. The machine install is unchanged, and
  the root is deleted with the session. Nothing but these runs and their probes ran on it.
- One side ran without a runtime it needs, as criterion 1 allows: `gitextensions-8522`'s first run, with
  `Microsoft.WindowsDesktop.App` 5.0, crashed (below), so the run this verdict rests on used the machine's runtimes,
  where a modern member of a Windows Forms project records "runtime net5.0-windows not installed" (3 replays). Its
  probes ran under the private root.

## The runs (criterion 2)

| Pair | Wall-clock | Exit | `run.properties.mode` | Runtimes, legacy / modern | `unverified` | Notifications | Config |
|---|---|---|---|---|---|---|---|
| gitextensions-8522, first run | 386 s | **5** | (no SARIF) | net461 / net5.0 | | | none |
| gitextensions-8522 | 363 s | 1 | quick | net461 (48) / net5.0 (43) | 0 | 0 | none |
| gitextensions-9860 | 271 s | 1 | quick | net5.0 (43) / net6.0 (43) | 0 | 0 | none |
| jellyfin-13023 | 193 s | 1 | quick | net8.0 (37) / net9.0 (37) | 0 | 0 | none |
| duplicati-3124 | 598 s | 1 | quick | net471 (51) / net5.0 (52) | 0 | 0 | `tools/corpus/configs/duplicati-3124.json` |
| openra-17989 | 263 s | 1 | quick | net472 (9) / net5.0 (8) | 0 | 0 | `tools/corpus/configs/openra-17989.json` |

The five went 2026-10-20 14:10 to 14:39 local time from one script, and the `gitextensions-8522` rerun 14:39 to 14:45
right after it, from the same build; no other corpus run was on the machine meanwhile (P2-248 had not started), and the
probes ran afterwards, never during a run. The runs read the checkouts and restores P2-237 made (the migrations) and
P2-212 made (the upgrades); only `pair.json` was copied.

**The crash.** The first `gitextensions-8522` run exited 5 after the `execute` phase and wrote no SARIF. With
`Microsoft.WindowsDesktop.App` 5.0 present, replay could build
`GitExtensions.Plugins.GitStatistics.PieChart.PieChart3D::GetSliceDisplacement(float,float)` for the first time; both
drivers stopped on the member's own `Debug.Assert`, and `ChildProcessHost.Session.Exchange` then read
`PrivateMemorySize64` of a process that had already left the snapshot ("Process has exited, so the requested
information is not available."), which nothing catches. P2-258 (new) owns it. The rerun left the desktop runtime out.

Against P2-238's default runs at `b0f56ff8`: every count is the same but `gitextensions-8522` EQ002 24 to 26 and EQ003
206 to 204 (P2-250's two new EQ002) and `duplicati-3124` EQ002 13 to 14 and EQ003 168 to 167. Divergent is 71 (EQ002
46, EQ006 25 as a default run reports them), from 68; runtime exposures 831, as before (237 witnessed, from 235).

## Replay (criterion 3)

Every EQ002 and EQ006 carries `properties.replay`. ADR 0060 decision 4 makes an exposure EQ006 under `--execute` when its
replay is `reproduced` (`proofMethod: observed`); those 11 are counted apart.

| Pair | Rule | Results | `reproduced` | `not-reproduced` | `not-constructible` | `not-applicable` |
|---|---|---|---|---|---|---|
| gitextensions-8522 | EQ002 | 26 | 0 | 0 | 26 | 0 |
| | EQ006 | 10 | 0 | 0 | 10 | 0 |
| | EQ006 observed | 2 | 2 | 0 | 0 | 0 |
| duplicati-3124 | EQ002 | 14 | 2 | 0 | 12 | 0 |
| | EQ006 | 14 | 0 | 0 | 14 | 0 |
| | EQ006 observed | 8 | 8 | 0 | 0 | 0 |
| openra-17989 | EQ006 | 1 | 0 | 0 | 0 | 1 |
| | EQ006 observed | 1 | 1 | 0 | 0 | 0 |
| gitextensions-9860 | EQ002 | 5 | 0 | 0 | 5 | 0 |
| jellyfin-13023 | EQ002 | 1 | 0 | 0 | 1 | 0 |
| **All five** | **EQ002** | **46** | **2** | **0** | **44** | **0** |
| | **EQ006** | **25** | **0** | **0** | **24** | **1** |
| | **EQ006 observed** | **11** | **11** | **0** | **0** | **0** |

Beside P2-237's: EQ002 was 0 of 37 constructible and is 2 of 46, both `SystemIOWindows::SetAccessControlFile`/`Dir`
on Duplicati, and both the harness (P2-259), not the edit; EQ006 was 10 `reproduced` and 10 `not-applicable` of 260,
and is 11 and 1 of 36 now that ADR 0060 reports the other exposures as EQ007. Of the 11 `reproduced` exposures, 4 show
the runtime change (three `GetHashCode()` on an instance a public constructor reaches, kept from P2-245, and OpenRA's
`Platform::get_RuntimeVersion()`); 7 are Duplicati `SystemIOWindows` members whose replay ran under the legacy
driver's legacy path handling (P2-259): 6 false positives and 1 undetermined.

Why a replay is not constructible, over the 68:

| Reason | EQ002 | EQ006 |
|---|---|---|
| the divergence is in the call trace | 21 | 12 |
| the call traces split before the outcome | 8 | 2 |
| the model constrains `typeof`, a static or undeclared field, or `thrown` | 7 | 5 |
| a framework or third-party object with no public parameterless constructor, or with model fields | 4 | 3 |
| an argument the driver cannot build (`IntPtr`, P2-256) | 2 | 0 |
| runtime net5.0-windows not installed (this run's Git Extensions host) | 1 | 2 |
| an abstract, interface or generic type no model type names | 1 | 0 |

The divergence lying in the call trace is still the largest reason (33 of 68): replay observes outcomes, not calls.

## Adjudication (criteria 4 to 6)

P2-237's method and Decision lines, unchanged: **confirmed** needs an executed oracle, a `reproduced` replay or a
`probe` run on both runtimes showing the claimed difference at the outcome; **false positive** needs a hand trace showing
the claimed difference cannot happen on the pair's runtimes; anything else is **undetermined**, with its one obstacle. A
replay or `probe` that differs for another reason confirms nothing. A result P2-212, P2-237 or P2-245 classified (with
the corrections P2-250, P2-252 and P2-253 made to P2-245's table), matched by procedure identity and rule, keeps that
classification unless this run's execution contradicts it.

- Decision: an exposure is one result whatever its rule id: P2-212's sampled EQ006 that ADR 0060 now reports as EQ007
  (or, observed, EQ006) is matched to it, as P2-245 matched them.
- Decision: a contradiction is a `reproduced` replay of a kept false positive, a `not-reproduced` replay of a kept
  confirmation, or an execution outside equiv that removes a kept confirmation's basis. Three occurred, all on Duplicati
  and all the legacy driver's legacy path handling (P2-259): `SetAccessControlFile` and `SetAccessControlDir` (P2-212
  false positives, `reproduced` here only by the harness, so P2-212's classification stands), and
  `SystemIOWindows::GetFileAttributes(string)`, which P2-237 confirmed by replay and `probe`: run outside equiv as
  Duplicati's hosts run it (.NET Framework 4.8 with a 4.7.1 target and `UseLegacyPathHandling=false`) against a net5.0
  build on 5.0.17, the two sides give the same outcome on every input, because the member passes a `\\?\` path, whose
  characters neither runtime checks. It is a false positive now (P2-260). P2-237's `SystemIOWindows::PathChangeExtension
  (string,string)` (P2-212 #49) rests on the same quirk and is a false positive too; it is an exposure of P2-212's
  sample, not a result counted here, so P2-238's "15 confirmed, 15 false positive" exposures are 14 and 16.
- Decision: for a witnessed exposure the model's input comes first; no model input could be built in any of the 35
  (every one is uninterpreted sorts, every replay `not-constructible`), so every confirmed exposure rests on the runtime
  row's own witness carried through the body, as P2-237's 15 did, and the appendix says so on each row.
- Decision: Duplicati `SystemIOWindows::PathGetFileName(string)` is undetermined, not the false positive its sub-agent
  wrote: the named path-character row cannot fire on its `\\?\` path, but the replay showed another returned-value
  difference on a relative name, under the harness's legacy path handling, that nothing checked under the solution's
  configuration.
- Decision: a member whose driver fails a `Debug.Assert` is never probed (it ends the probe server, P2-258); its row is
  undetermined with that obstacle.
- The Divergent rows, P2-237's 71 and the exposures were traced and probed by six sub-agents, one per migration and
  part, under one written brief (the session scratch folder's `brief.md`), the upgrades needing none (their six EQ002
  keep P2-212's classification and no replay contradicts it). Their tables were read in full and checked against
  `probe`'s own output. 174 `probe` calls ran; 20 returned an error before running (an argument or receiver no driver builds, an `extern` member, a constructor spelled wrongly and retried), none crashed the server, and every driver that was built compiled. The Duplicati sub-agent also built two small .NET Framework programs and a net5.0 one outside equiv to measure path handling (P2-259): the framework's path APIs and a copy of Duplicati's `PrefixWithUNC` and
`StripUNCPrefix`, kept in the scratch folder.

## Divergent precision (criteria 4 and 7)

Precision is confirmed / (confirmed + false positive), undetermined left out, over every EQ002 and EQ006 of the five
runs. "EQ006" is what a default run reports; the observed EQ006 (exposures a replay reproduced) are counted apart, as
the exposures they are in a default run.

| Pair | Rule | Results | Confirmed | False positive | Undetermined | Precision |
|---|---|---|---|---|---|---|
| Git Extensions 8522 | EQ002 | 26 | 7 | 9 | 10 | 43.8% (7 of 16) |
| Git Extensions 8522 | EQ006 | 10 | 4 | 4 | 2 | 50.0% (4 of 8) |
| Git Extensions 8522 | **EQ002 + EQ006** | 36 | 11 | 13 | 12 | **45.8% (11 of 24)** |
| Git Extensions 8522 | EQ006 observed (apart) | 2 | 2 | 0 | 0 | 100.0% (2 of 2) |
| Duplicati | EQ002 | 14 | 1 | 8 | 5 | 11.1% (1 of 9) |
| Duplicati | EQ006 | 14 | 11 | 1 | 2 | 91.7% (11 of 12) |
| Duplicati | **EQ002 + EQ006** | 28 | 12 | 9 | 7 | **57.1% (12 of 21)** |
| Duplicati | EQ006 observed (apart) | 8 | 1 | 6 | 1 | 14.3% (1 of 7) |
| OpenRA | EQ006 | 1 | 1 | 0 | 0 | 100.0% (1 of 1) |
| OpenRA | **EQ002 + EQ006** | 1 | 1 | 0 | 0 | **100.0% (1 of 1)** |
| OpenRA | EQ006 observed (apart) | 1 | 1 | 0 | 0 | 100.0% (1 of 1) |
| Git Extensions 9860 | EQ002 | 5 | 5 | 0 | 0 | 100.0% (5 of 5) |
| Git Extensions 9860 | **EQ002 + EQ006** | 5 | 5 | 0 | 0 | **100.0% (5 of 5)** |
| Jellyfin | EQ002 | 1 | 1 | 0 | 0 | 100.0% (1 of 1) |
| Jellyfin | **EQ002 + EQ006** | 1 | 1 | 0 | 0 | **100.0% (1 of 1)** |
| **Migrations** | EQ002 | 40 | 8 | 17 | 15 | 32.0% (8 of 25) |
| **Migrations** | EQ006 | 25 | 16 | 5 | 4 | 76.2% (16 of 21) |
| **Migrations** | **EQ002 + EQ006** | 65 | 24 | 22 | 19 | **52.2% (24 of 46)** |
| **Migrations** | EQ006 observed (apart) | 11 | 4 | 6 | 1 | 40.0% (4 of 10) |
| **Upgrades** | EQ002 | 6 | 6 | 0 | 0 | 100.0% (6 of 6) |
| **Upgrades** | **EQ002 + EQ006** | 6 | 6 | 0 | 0 | **100.0% (6 of 6)** |

Beside the README's rows: migrations **52.2% (24 of 46), from 11.1% (2 of 18)**, and upgrades **100% (6 of 6), from
85.7% (6 of 7)** (the upgrades' one false positive, `PatchManager::ToPatch(...)`, is Equivalent since P2-211). The
migration figure is a full count, not a sample: 65 results, 19 of them undetermined (29.2%). If every undetermined one
were real it would be at most 66.2% (43 of 65); if every one were false, 36.9% (24 of 65).

How the 46 decided migration results were decided: 30 keep an earlier classification no execution here contradicts (11
confirmed: P2-212 1, P2-237 1, P2-245 1, P2-250 4, P2-252 2, P2-253 2; 19 false positives: P2-212 15, P2-237 1, P2-245
3); 16 were decided here (13 confirmed by `probe`, 3 false positives by hand trace). Of the 22 results with no earlier
classification (the runs' EQ002 and EQ006 outside P2-212's sample, and P2-250's two new EQ002), 12 are confirmed, 3 false
positives and 7 undetermined. EQ006 is now the more precise rule (76.2%, 16 of 21): its confirmations are almost all the
path-character and ICU rows run with their own witness; EQ002's (32.0%, 8 of 25) falls on the kept false positives of
P2-212's sample, whose causes stay open (findings).

## P2-237's 71 (criterion 5)

Each of P2-237's 71 undetermined results, matched by procedure identity and rule: all 71 are still flagged at
`4b2e0df6` (none is Equivalent or Unknown), 25 as EQ002 or EQ006 and 46 as exposures.

| P2-237 obstacle | Rows | Confirmed | False positive | Still undetermined | No longer flagged |
|---|---|---|---|---|---|
| not callable by a driver | 41 | 24 | 3 | 14 | 0 |
| an argument no driver built | 10 | 6 | 0 | 4 | 0 |
| an OpenRA mod project under `probe` (P2-241) | 8 | 5 | 1 | 2 | 0 |
| a constructor or a struct (P2-242) | 5 | 5 | 0 | 0 | 0 |
| not safe to run, the host, or a result not comparable | 4 | 0 | 0 | 4 | 0 |
| a field written | 1 | 0 | 0 | 1 | 0 |
| `probe` equal on every input | 1 | 0 | 0 | 1 | 0 |
| `probe` differed for another reason (P2-240) | 1 | 0 | 0 | 1 | 0 |
| **All** | **71** | **40** | **4** | **27** | **0** |

Before this ticket, P2-245 and its corrections (P2-250, P2-252, P2-253) had decided 33 of the 71 (30 confirmed, 3 false
positives), and no execution here contradicts them. Of the 38 left, this audit confirms 10 (OpenRA's struct, constructor
and mod-project rows that P2-241, P2-242 and P2-246 made callable, by `probe`) and refutes 1 (OpenRA's
`D2kMapImporter::.ctor(...)`, a catch-all that swallows the path exception and then throws the same
`NullReferenceException` on both sides). 27 stay undetermined: their obstacles are in the next section and appendix B.
Every obstacle class P2-237 named that a reach ticket removed is now decided but for rows whose own obstacle is another
one; what is left is the window and desktop rule, unsafe members, results no driver compares, and differences in a field
written or the call trace.

## Exposure precision (criteria 6 and 7)

Drawn with Python's `random.Random(247)`, `sample(15)` of each class's results sorted by fingerprint, per migration in
the order Git Extensions, Duplicati, OpenRA, witnessed before unwitnessed, excluding the procedures of P2-212's sample.
OpenRA has only 5 witnessed exposures outside that sample, so all 5 are adjudicated.

| Pair | Class | Pool | Drawn | Confirmed | False positive | Undetermined | Precision |
|---|---|---|---|---|---|---|---|
| Git Extensions 8522 | witnessed | 88 | 15 | 6 | 3 | 6 | 66.7% (6 of 9) |
| Git Extensions 8522 | unwitnessed | 330 | 15 | 2 | 8 | 5 | 20.0% (2 of 10) |
| Duplicati | witnessed | 59 | 15 | 11 | 3 | 1 | 78.6% (11 of 14) |
| Duplicati | unwitnessed | 130 | 15 | 1 | 10 | 4 | 9.1% (1 of 11) |
| OpenRA | witnessed | 5 | 5 | 0 | 1 | 4 | 0.0% (0 of 1) |
| OpenRA | unwitnessed | 118 | 15 | 3 | 4 | 8 | 42.9% (3 of 7) |
| **Migrations** | witnessed | 152 | 35 | 17 | 7 | 11 | **70.8% (17 of 24)** |
| **Migrations** | unwitnessed | 578 | 45 | 6 | 22 | 17 | **21.4% (6 of 28)** |
| **Migrations** | **both** | | 80 | 23 | 29 | 28 | **44.2% (23 of 52)** |

Beside the README's "15 confirmed and 15 false positive" (all witnessed, P2-212's sample): 23 confirmed and 29 false
positives over the fresh 80, **witnessed 70.8% (17 of 24), unwitnessed 21.4% (6 of 28)**; 28 undetermined (35.0%). The
witnessed exposures confirm at the rate P2-237's sample did; the 594 unwitnessed ones are mostly false: where the solver
gives no witness, it is usually because none exists. Their false positives have clear causes (findings): a path the
runtime, the file system or the solution made (8), a signed-zero row no operand can reach (5), a catch-all or an
`Exists` guard (3), and 13 where the row cannot reach the outcome for another reason (a field that is not `static
readonly`, an argument never `null`, a handler never run, a version test both sides answer alike, an `extern` marshalled
alike, an ordinal comparison, a member .NET 5 implements, a `Char.IsLetter` test whose branches agree). Every confirmed
exposure was run with the runtime row's own witness by `probe`: the path-character rows (14), ICU's ignorable characters
under a culture-sensitive comparison (6, one of them `EndsWith` on a zip entry name), a reclassified code point, a
special folder and `Thread.Abort`.

## Undetermined, grouped by obstacle (criterion 7)

| Class | Undetermined | Of | Share |
|---|---|---|---|
| Migration Divergent (EQ002 + EQ006) | 19 | 65 | 29.2% |
| Migration EQ006 observed | 1 | 11 | 9.1% |
| Upgrade Divergent | 0 | 6 | 0% |
| P2-237's 71 | 27 | 71 | 38.0% |
| Exposures, witnessed | 11 | 35 | 31.4% |
| Exposures, unwitnessed | 17 | 45 | 37.8% |

The obstacles, over the 20 migration Divergents (D), the 15 undetermined of P2-237's 71 that are exposures now (71; its
other 12 are among the D) and the 28 sampled exposures (X):

| Obstacle | D | 71 | X | Owner |
|---|---|---|---|---|
| a window receiver or argument, a dialog or a message loop (ADR 0035's desktop rule) | 5 | 1 | 6 | none: by design |
| not safe to run: a process, the network, a live service, the user's or machine's folders | 2 | 3 | 6 | none: by design |
| the result is not comparable (a collection, an `Assembly`, a `Stream`, a handler, a runner object) | 4 | 2 | 2 | none |
| the difference is only in a field written, the call trace or console output | 3 | 3 | 4 | none: not an outcome |
| the host decides: its files, settings, hardware or executable manifest | 3 | 4 | 2 | none |
| an argument or receiver no driver builds (a generic collection, `XmlElement`, `ExCSS.StyleRule`, `out` parameters, a static the probe cannot set) | 2 | 1 | 4 | P2-273 for the generic collections (3) |
| an `extern` member, which no driver calls | 0 | 0 | 3 | none: by design |
| the driver fails a `Debug.Assert`, which ends the probe server | 0 | 1 | 1 | P2-258 |
| another runtime difference seen under the harness's path handling, not checked | 1 | 0 | 0 | P2-259 |

## Findings and tickets (criterion 8)

Every false-positive cause, crash and harness finding names its owning ticket. No replay is `not-reproduced`, so ADR
0035 decision 2's soundness alarm files nothing. A cause with several results is one ticket.

| Ticket | Finding | Results here |
|---|---|---|
| P2-258 (S, new) | Crash: a replay driver that stops on an assertion is read after it left the process snapshot; `Exchange` throws and the run exits 5 with no SARIF (`gitextensions-8522` with `Microsoft.WindowsDesktop.App` 5.0) | 1 run; 2 undetermined |
| P2-259 (S, new) | A .NET Framework driver runs with the compatibility quirks of no target framework (legacy path handling, blocked long paths, no async culture flow): every `\\?\` path fails on the legacy side | 9 `reproduced` replays; 2 of P2-237's confirmations removed |
| P2-260 (M, new) | False EQ002/EQ006: the path-character rows on a path the body prefixes with `\\?\`, which neither runtime checks (Duplicati's `SystemIOWindows`; P2-193's four ACL members) | 10 false positives (and P2-212's #49) |
| P2-261 (M, new) | False positives: a path the runtime or the solution made (the assembly's directory, a special or temp folder, a file the solution created, a field set through `Path.GetFullPath`) | 8 |
| P2-262 (M, new) | False exposures: the `Math.Min`/`Max`/`Ceiling` signed-zero and NaN rows where the data flow rules the witness out | 5 |
| P2-229 (M, open) | A path a valid directory's listing returned | 3 |
| P2-223 (open) | A path row whose throw a catch-all swallows | 3 (one of the 71) |
| P2-269 (M, new) | False EQ002: a framework fact the side's runtime fixes is a free input (P2-245's three refuted rows, which had no ticket) | 3 |
| P2-264 (S, new) | False exposures: an `Environment.Version`/`OSVersion` test both runtimes answer alike | 2 |
| P2-266 (S, new) | False exposures: the `FieldInfo.SetValue` row on a field that is not `static readonly` | 2 |
| P2-267 (S, new) | False exposures: a runtime-changed call only inside an event handler the body subscribes | 2 |
| P2-268 (S, new) | False exposures: a `null`-argument row on an array literal or a boxed struct | 2 |
| P2-185, P2-186 (open) | P2-212's EQ002 causes (kept) | 2 each |
| P2-214 (M, open) | An unedited `extern` whose marshalling is the same on both runtimes | 2 |
| P2-263 (S, new) | A `File.Delete` behind `File.Exists` of the same path in Duplicati's `Runner::Run` | 1 |
| P2-265 (S, new) | A `Char.IsLetter` test whose two branches return the same | 1 |
| P2-270 (M, new) | A path row on an NSubstitute property a test sets to a constant (P2-218 left it) | 1 |
| P2-271 (S, new) | An `as string` read of a slot the solution writes only strings to (P2-213 left it) | 1 |
| P2-097, P2-187, P2-205, P2-206, P2-243 (open) | P2-212's and P2-237's EQ002 causes (kept) | 1 each |
| P2-219, P2-224, P2-225 (open) | The RSA XML row on .NET 5; an ordinal comparison a solution getter returns; `UseShellExecute` set to .NET Framework's default | 1 each |
| P2-272 (S, new) | Check: `Math.Sin`/`Math.Cos` differ in the low bits between .NET Framework 4.8 and .NET 10 on ordinary angles (measured outside equiv), which the runtime table does not cover; measure against .NET 5, 6, 8 and 9 before any soundness claim | none counted |
| P2-273 (S, new) | Execution reach: a driver builds no generic collection argument | 3 undetermined |

Other things seen, not a ticket: ICU ignores every C0 control except tab, line feed, vertical tab, form feed and
carriage return, and U+007F (measured outside equiv), a wider witness than NUL and U+200B; `probe` refuses an `extern`
member by design; `probe`'s constructor key is the parameter types only, which the brief had wrong at first.

## Scoreboard (criterion 9)

`equiv-scoreboard` was applied over the five large pairs, all five by these runs. A row's counts are those a default
run reports: an observed EQ006 (an exposure a replay reproduced) is counted where a default run counts it, as a
witnessed exposure, so the rows stay comparable with P2-238's. Rows rewritten: Unknown, Divergent, Runtime exposure,
Divergent precision, and in the detail table below the scoreboard the Divergent and Unknown cells of
`gitextensions-8522` and `duplicati-3124`, the changed-pairs sentence, the Unknown reasons and the "A Divergent is usually
a real behaviour change" line. Unchanged: the header (5 pairs, 58,477 matched), Proved Equivalent (97.3%, 56,920),
Equivalent by congruence (96.0%), Changed pairs the solver proves (33.9%, 800 of 2,357), Left for a reviewer (2.7%,
1,557), Soundness, Large pairs that run to a result (5 of 5: each pair's latest run wrote SARIF; the crashed first
`gitextensions-8522` run is not its latest), Run time (mean 6 min, 3 min to 10 min) and Repeatability.

| Row | Was | Now |
|---|---|---|
| Unknown | 1.1% of pairs (658), 27.9% of changed pairs; opaque 416, abstraction 91, budget 129, unaligned loop 18, recursion 4 | 1.1% of pairs (655), 27.8% of changed pairs; opaque 417, abstraction 90, budget 126, unaligned loop 18, recursion 4 |
| Divergent | 0.1% of pairs (68), 2.9% of changed pairs | 0.1% of pairs (71), 3.0% of changed pairs |
| Runtime exposure | 831; 235 with a witness input, 596 without; of the sampled migration exposures 15 confirmed, 15 false positive | 831; 237 with a witness input, 594 without; of 80 drawn at random 23 confirmed, 29 false positive (witnessed 17 and 7, unwitnessed 6 and 22) |
| Divergent precision | migrations 11.1% (2 of 18); upgrades 85.7% (6 of 7) | migrations 52.2% (24 of 46), every Divergent counted; upgrades 100% (6 of 6) |

Divergent rose by 3 (P2-250's two EQ002 that were Unknown, and one more Duplicati EQ002 that was Unknown), which is the
3 Unknowns fewer. The precision rows rise because a full count with execution replaces a sample adjudicated mostly by
hand, not because a result changed.

## Scoreboard milestone 4 (criterion 10)

`docs/ROADMAP.md` gets "Scoreboard milestone 4 (2026-10-20)", ranking the open tickets by what this verdict counts
against each: false positives (Divergent and sampled exposures), undetermined results, and for Unknowns the changed pairs
carrying each open owner's opaque reason in these runs' `changedReasonSets` (P2-248 is not done): `Conversion` 109
(P2-215), `InterpolatedString` 93 (P2-102), `LocalFunction` 91 (P2-178), `no-body` 78 (P2-214), `ArrayElementReference`
39 (P2-183), counted over every reason set that holds the reason.

## Method notes

- Runs: `./tools/corpus/corpus.ps1 -Compare <slug> -Mode quick -Tag execute -CompareArgs '--jobs', '4', '--execute'`
  from one script, then the `gitextensions-8522` rerun with `-Tag execute-nodesktop` on the machine's `dotnet`; run
  directories `.corpus/pairs/<slug>/runs/20261020-14xx-full-quick-execute*` of this worktree.
- `probe`: `equiv mcp --execute` from the same build, driven over stdio by a scratch script (one server per batch, one
  batch per pair at a time, never during a run), under the private `dotnet` root. Inputs were chosen by tracing each
  body against its runtime row; none named a real user file or an address that resolves.
- Counts come from the SARIF logs, read by scratch scripts: the replay table, the rows matched to P2-212's sample and to
  P2-237's 71 by procedure identity and rule, the seeded draw, and the tables here. The probe inputs and outcomes, and
  each row's trace, stay in the session's scratch folder; the appendices hold identities, classes, exception types and
  kinds of input only.
- The sub-agents' brief is the scratch folder's `brief.md`; two corrections went to the later agents in their prompts
  (`probe`'s constructor key is the parameter types only; the legacy driver's path handling, P2-259).

## Appendix A: every EQ002 and EQ006 of the five runs

"Decided by" is the ticket whose classification stands (P2-247 where this audit decided it). "Cause" is the owning ticket of a false positive.

| Pair | Procedure | Rule | Replay | Classification | Decided by | Cause | Oracle or trace |
|---|---|---|---|---|---|---|---|
| Git Extensions 8522 | `BugReporter.Program::Main()` | EQ002 | not-constructible | undetermined | P2-247 |  | not safe to run: BugReporter Main runs a WinForms message loop (Application.Run) |
| Git Extensions 8522 | `CommonTestUtils.MockExecutable.MockProcess::Verify()` | EQ002 | not-constructible | false positive | P2-245 | P2-269 | the added CanRead guard only matters for a closed stream still held by StandardInput; the solution closes input only through StreamWriter.Close, which on .NET Framework nulls BaseStream (legacy skips) and on .NET 5 leaves CanRead false (modern skips), so both pass; probe 19-b (receiver by the nested type constructor MockProcess()): both returned |
| Git Extensions 8522 | `EasyHook.HookAccessControl::SetExclusiveACL(int[])` | EQ002 | not-constructible | confirmed | P2-250 |  | real: the modern copy compiles its native calls out (SUPPORT_THEMES undefined); P2-245 probe: legacy threw DllNotFoundException, modern returned |
| Git Extensions 8522 | `EasyHook.HookAccessControl::SetInclusiveACL(int[])` | EQ002 | not-constructible | confirmed | P2-250 |  | real: the modern copy compiles its native calls out (SUPPORT_THEMES undefined); P2-245 probe: legacy threw DllNotFoundException, modern returned |
| Git Extensions 8522 | `EasyHook.LocalHook::GetProcAddress(string,string)` | EQ002 | not-constructible | confirmed | P2-237 |  | 2-nomodule: legacy threw DllNotFoundException, modern returned (an IntPtr; the modern copy compiles to a constant return without SUPPORT_THEMES) |
| Git Extensions 8522 | `EasyHook.LocalHook::IsThreadIntercepted(int)` | EQ002 | not-constructible | confirmed | P2-250 |  | real: the modern copy compiles its native calls out (SUPPORT_THEMES undefined), so modern returns false without asking |
| Git Extensions 8522 | `EasyHook.LocalHook::Release()` | EQ002 | not-constructible | confirmed | P2-212 |  | kept from P2-047 (row 18: replay `reproduced`) |
| Git Extensions 8522 | `EasyHook.LocalHook::get_HookBypassAddress()` | EQ002 | not-constructible | confirmed | P2-250 |  | real: the modern copy compiles its native calls out (SUPPORT_THEMES undefined), so modern returns IntPtr.Zero without asking |
| Git Extensions 8522 | `Git.hub.Client::setOAuth2Token(string)` | EQ002 | not-constructible | undetermined | P2-247 |  | void method: the difference is only the authenticator object written to a RestClient field, which probe does not observe |
| Git Extensions 8522 | `GitCommands.EnvironmentPathsProvider::IsValidPath(string)` | EQ002 | not-constructible | undetermined | P2-247 |  | probe ran with equal outcomes on every input tried (wildcard characters, a plain relative path, a path through an existing file); a difference needs a path whose attribute query fails with an access or I/O error other than not-found (host files) |
| Git Extensions 8522 | `GitCommandsTests.Settings.FileSettingsCacheTests::ctor_FileWatcher_Filter_should_be_default_if_invalid_dir(string)` | EQ002 | not-constructible | false positive | P2-237 | P2-243 | FileSystemWatcher.Filter defaults differ by runtime and each side asserts its own runtime's default, so both pass (21-boo: both returned) |
| Git Extensions 8522 | `GitCommandsTests.SshPathLocatorTest::File_system_access_throwing_should_return_empty_string()` | EQ002 | not-constructible | false positive | P2-212 | P2-185 | P2-185 |
| Git Extensions 8522 | `GitCommandsTests.SshPathLocatorTest::Find_on_gitBinDir_parent_throwing_should_return_empty_string()` | EQ002 | not-constructible | false positive | P2-212 | P2-185 | P2-185 |
| Git Extensions 8522 | `GitExtensions.Program::Main()` | EQ002 | not-constructible | undetermined | P2-247 |  | Main runs the WinForms application (message loop, dialogs) and is void; the edit (theme loading compiled out by SUPPORT_THEMES) differs only in the call trace |
| Git Extensions 8522 | `GitExtensions.UITests.Script.ScriptRunnerTests::Setup()` | EQ002 | not-constructible | false positive | P2-212 | P2-187 | P2-187 |
| Git Extensions 8522 | `GitUI.Avatars.GithubAvatarProvider::GetAvatarAsync(string,string,int)` | EQ002 | not-constructible | false positive | P2-212 | P2-097 | P2-097 |
| Git Extensions 8522 | `GitUI.CommandsDialogs.FormCleanupRepository::AddPath_Click(object,System.EventArgs)` | EQ002 | not-constructible | undetermined | P2-247 |  | an instance member of a Form (window receiver refused) that opens a folder dialog; void, the difference is in the dialog used and the text box it fills |
| Git Extensions 8522 | `GitUI.CommitInfo.CommitInfo.BranchComparer::Compare(string,string)` | EQ002 | not-constructible | confirmed | P2-253 |  | probe on Microsoft.WindowsDesktop.App 5.0: legacy and modern returned different values (Comparer<string>.Default against StringComparer.Ordinal) |
| Git Extensions 8522 | `GitUI.CommitInfo.RefsFormatter::FilterAndFormatBranches(System.Collections.Generic.IEnumerable<string>,bool,bool)` | EQ002 | not-constructible | undetermined | P2-247 |  | not callable with a non-null element sequence: the probe builds a collection only for an array parameter and no non-generic type implements IEnumerable<string>; by hand a null branch name makes legacy throw (ArgumentNullException in LinkFactory) or add null where modern adds an empty string |
| Git Extensions 8522 | `GitUI.OsShellUtil::PickFolder(System.Windows.Forms.IWin32Window,string)` | EQ002 | not-constructible | undetermined | P2-247 |  | the IWin32Window parameter is refused (needs a live window) and the body opens a folder dialog |
| Git Extensions 8522 | `GitUI.Theming.ThemeLoader::.ctor(GitUI.Theming.IThemeCssUrlResolver,GitUI.Theming.IThemeFileReader)` | EQ002 | not-constructible | false positive | P2-212 | P2-186 | P2-186 |
| Git Extensions 8522 | `GitUI.Theming.ThemeLoader::StyleRuleThemeException(ExCSS.StyleRule,string)` | EQ002 | not-constructible | undetermined | P2-247 |  | not callable: ExCSS.StyleRule has no constructor shared by ExCSS 2.0.6 and 4.1.0, and a third-party type cannot be built by fields |
| Git Extensions 8522 | `GitUI.Theming.ThemeModule::ResetGdiCaches()` | EQ002 | not-constructible | false positive | P2-245 | P2-269 | probe 20-a: both returned; on .NET Framework 4.8 the reflected System.Drawing members exist with the expected types, so the InvalidCastException the model claims for legacy does not happen, and the modern body is compiled out (SUPPORT_THEMES undefined) |
| Git Extensions 8522 | `GitUI.UserControls.RevisionGrid.Graph.RevisionGraphRevision::.ctor(GitUIPluginInterfaces.ObjectId,int)` | EQ002 | not-constructible | false positive | P2-212 | P2-186 | P2-186 |
| Git Extensions 8522 | `GitUI.UserControls.RevisionGrid.Graph.RevisionGraphRevision::get_StartSegments()` | EQ002 | not-constructible | undetermined | P2-247 |  | result not comparable: the getter's declared type changed from SynchronizedCollection to ConcurrentBag, which the driver does not compare (P2-245 probe 11-a) |
| Git Extensions 8522 | `GitUIPluginInterfaces.ManagedExtensibility::TryLoadAssembly(System.IO.FileInfo)` | EQ002 | not-constructible | undetermined | P2-247 |  | result not comparable: the probe reports an Assembly-typed result as NotComparable on both sides, even for a file that is not an assembly, where both sides return null |
| Git Extensions 8522 | `ConEmu.WinForms.AnsiStreamChunkEventArgs::GetText(System.Text.Encoding)` | EQ006 | not-constructible | confirmed | P2-245 |  | probes 23-a, 23-c, 23-d (receiver by constructor, encoding by UTF8Encoding()): both returned, different strings (a different count of U+FFFD for ill-formed UTF-8); 23-b equal; receiver needs no trace |
| Git Extensions 8522 | `GitCommands.CommitMessageManager::WriteFile(string,string,string,System.Text.Encoding)` | EQ006 | not-constructible | confirmed | P2-247 |  | probe D04-a, a file path holding a character .NET Framework rejects: legacy threw ArgumentException (Path.GetDirectoryName), modern returned; control D04-b without the character returned on both |
| Git Extensions 8522 | `GitCommands.Git.IndexLockManager::IsIndexLocked()` | EQ006 | not-constructible | undetermined | P2-247 |  | needs a .git file whose gitdir line holds a character .NET Framework rejects (host files), and there legacy throws in GitDirectoryResolver (IsPathRooted) before the named Path.Combine; a GitModule working folder holding one cannot be constructed on legacy (Path.GetFullPath in its constructor) |
| Git Extensions 8522 | `GitCommands.Git.Tag.GitTagController::CreateTag(GitCommands.Git.Commands.GitCreateTagArgs,System.Windows.Forms.IWin32Window)` | EQ006 | not-constructible | false positive | P2-247 | P2-261 | the only Path.Combine argument is GitModule.WorkingDir, which GitModule's constructor normalises with Path.GetFullPath (or empties when Uri rejects it); on .NET Framework that rejects the same invalid path characters, so the Path.Combine never sees one there (the member also takes an IWin32Window and starts a process dialog) |
| Git Extensions 8522 | `GitExtensions.Plugins.Gource.GourceStart::LoadAvatarsAsync()` | EQ006 | not-constructible | false positive | P2-247 | P2-229 | the File.Delete argument comes from Directory.GetFiles of the temp avatars folder, which only returns names Windows allows, none holding a character .NET Framework rejects |
| Git Extensions 8522 | `GitUI.CommandsDialogs.FormVerify::GuessFileTypeWithContent(string)` | EQ006 | not-constructible | confirmed | P2-253 |  | probe on Microsoft.WindowsDesktop.App 5.0: legacy and modern returned different file names (ICU ignores a leading NUL or zero-width space) |
| Git Extensions 8522 | `GitUI.Editor.FileViewer::ViewFileAsync(string,bool,System.Action)` | EQ006 | not-constructible | undetermined | P2-247 |  | the receiver FileViewer is a UserControl (window receiver refused) and the outcome is a Task whose effect is what the control displays |
| Git Extensions 8522 | `GitUITests.Theming.ThemeLoaderTests::Should_throw_When_cyclic_css_imports()` | EQ006 | not-constructible | false positive | P2-212 | P2-270 | P2-218 |
| Git Extensions 8522 | `NetSpell.SpellChecker.Dictionary.WordDictionary::SaveUserFile()` | EQ006 | not-constructible | confirmed | P2-247 |  | probe D08-a (and D08-b), a user file name holding a character .NET Framework rejects: legacy threw ArgumentException (Path.Combine), modern threw IOException (FileStream); the receiver was created without a constructor, but new WordDictionary() and the public UserFile and EnableUserFile setters reach that state |
| Git Extensions 8522 | `ResourceManager.LinkFactory::ExecuteLink(string,System.Action<global::ResourceManager.CommandEventArgs>,System.Action<string>)` | EQ006 | not-constructible | false positive | P2-212 | P2-225 | P2-216 |
| Git Extensions 8522 | `Git.hub.Organization::GetHashCode()` | EQ006 (observed) | reproduced | confirmed | P2-245 |  | replay reproduced on an uninitialized receiver a public constructor reaches (P2-245 Finding 1) |
| Git Extensions 8522 | `Git.hub.User::GetHashCode()` | EQ006 (observed) | reproduced | confirmed | P2-245 |  | replay reproduced on an uninitialized receiver a public constructor reaches (P2-245 Finding 1) |
| Duplicati | `Duplicati.GUI.TrayIcon.Program::GetWinformsInstance()` | EQ002 | not-constructible | undetermined | P2-247 |  | probe P00-plain: both sides returned a TrayIconBase that probe does not compare; the difference is only the runner type constructed (WinFormsRunner against Win32Runner) |
| Duplicati | `Duplicati.GUI.TrayIcon.Program::TryGetWinforms()` | EQ002 | not-constructible | false positive | P2-245 | P2-269 | differs only when Platform.IsClientWindows is false, a non-Windows host, where .NET Framework 4.8 does not run; probe 22-plain: both returned true on Windows |
| Duplicati | `Duplicati.Library.AutoUpdater.UpdaterManager::RunFromMostRecent(System.Reflection.MethodInfo,string[],Duplicati.Library.AutoUpdater.AutoUpdateStrategy)` | EQ002 | not-constructible | undetermined | P2-247 |  | not safe to run: both sides spawn a process; the difference also needs the AUTOUPDATER_USE_APPDOMAIN environment variable |
| Duplicati | `Duplicati.Library.Backend.AzureBlob.AzureBlobWrapper::AddFileStream(string,System.IO.Stream,System.Threading.CancellationToken)` | EQ002 | not-constructible | confirmed | P2-252 |  | probe with an awaited task: legacy faulted with ArgumentNullException, modern NullReferenceException (the new source.Length read) |
| Duplicati | `Duplicati.Library.Backend.S3IAM::CanCreateUser(string,string)` | EQ002 | not-constructible | false positive | P2-212 | P2-205 | P2-205 |
| Duplicati | `Duplicati.Library.Backend.S3IAM::CreateUnprivilegedUser(string,string,string)` | EQ002 | not-constructible | undetermined | P2-247 |  | not safe to run: CreateUser, PutUserPolicy and CreateAccessKey call a live AWS IAM service before the outcomes split |
| Duplicati | `Duplicati.Library.Common.IO.SystemIOWindows::GetAccessControlDir(string)` | EQ002 | not-constructible | false positive | P2-212 | P2-260 | P2-193 |
| Duplicati | `Duplicati.Library.Common.IO.SystemIOWindows::GetAccessControlFile(string)` | EQ002 | not-constructible | false positive | P2-212 | P2-260 | P2-193 |
| Duplicati | `Duplicati.Library.Common.IO.SystemIOWindows::SetAccessControlDir(string,System.Security.AccessControl.DirectorySecurity)` | EQ002 | reproduced | false positive | P2-212 | P2-260 | kept from P2-212 (P2-193): this run's `reproduced` replay is the legacy driver's legacy path handling, not the edit (P2-259); the reproduced replay is the harness, not the edit: the legacy driver runs .NET Framework's legacy path handling (an app.config sku and no TargetFrameworkAttribute), which rejects every \\?\ path with ArgumentException, even a legal one (probes D19-cctl, D15-nctl, D16-nctl, D17-nctl); Duplicati's hosts target 4.7.1 and set UseLegacyPathHandling=false; under that setting Directory.SetAccessControl accepts the \\?\ path and returns as DirectoryInfo.SetAccessControl does (measured on .NET Framework 4.8, fresh and read-back DirectorySecurity), and SetMetadata never passes a null security object |
| Duplicati | `Duplicati.Library.Common.IO.SystemIOWindows::SetAccessControlFile(string,System.Security.AccessControl.FileSecurity)` | EQ002 | reproduced | false positive | P2-212 | P2-260 | kept from P2-212 (P2-193): this run's `reproduced` replay is the legacy driver's legacy path handling, not the edit (P2-259); the reproduced replay is the harness, not the edit: the legacy driver runs .NET Framework's legacy path handling (an app.config sku and no TargetFrameworkAttribute), which rejects every \\?\ path with ArgumentException, even a legal one (probes D19-cctl, D15-nctl, D16-nctl, D17-nctl); Duplicati's hosts target 4.7.1 and set UseLegacyPathHandling=false; under that setting File.SetAccessControl accepts the \\?\ path PrefixWithUNC always passes and returns as FileInfo.SetAccessControl does (measured on .NET Framework 4.8 for existing and missing targets, fresh and read-back FileSecurity), and SetMetadata never passes a null security object |
| Duplicati | `Duplicati.Library.Localization.LocalizationContext::.ctor(System.Globalization.CultureInfo)` | EQ002 | not-constructible | false positive | P2-212 | P2-271 | P2-213 |
| Duplicati | `Duplicati.Library.OAuthHttpClient::CreateMessageHandler(string,string)` | EQ002 | not-constructible | undetermined | P2-247 |  | the difference is only in properties set on the returned handler (ReadWriteTimeout and the certificate callback from HttpContextSettings), which no outcome compares; both sides return the handler |
| Duplicati | `Duplicati.Library.Utility.Power.WindowsPowerSupplyState::GetSource()` | EQ002 | not-constructible | undetermined | P2-247 |  | probe P08-plain ran with equal outcomes (both returned the same source; P2-240's FileNotFoundException is gone); the difference depends on the host's battery hardware |
| Duplicati | `Duplicati.Library.Utility.Utility::CalculateHash(System.IO.Stream)` | EQ002 | not-constructible | false positive | P2-212 | P2-206 | P2-206 |
| Duplicati | `Duplicati.CommandLine.RecoveryTool.FileIndex::SortFile(string,string)` | EQ006 | not-constructible | undetermined | P2-247 |  | probe D08-bar differs (legacy ArgumentException, modern IOException) but by File.Copy's own invalid-path change in the fallback; the named File.ReadAllLines change is swallowed by the catch-all around it, so the named change does not decide the outcome |
| Duplicati | `Duplicati.CommandLine.RecoveryTool.List::SelectListFile(string,string)` | EQ006 | not-constructible | confirmed | P2-247 |  | probe D09-bar, a folder holding a character .NET Framework rejects: legacy threw ArgumentException from Path.Combine, modern passed it and threw IOException from Directory.EnumerateFiles; control D09-ctl DirectoryNotFoundException on both |
| Duplicati | `Duplicati.Library.AutoUpdater.UpdaterManager::GetBestUpdateVersion(bool)` | EQ006 | not-constructible | undetermined | P2-247 |  | the difference is only in the call trace: the Path.Combine of the current file's content sits in a catch-all whose only effect is the OnError event; an INSTALLDIR holding a rejected character fails the type's static constructor on both sides first; the return also differs by the edited ApplicationBase key, not the named change |
| Duplicati | `Duplicati.Library.AutoUpdater.UpdaterManager::ReadInstalledManifest(string)` | EQ006 | not-constructible | confirmed | P2-247 |  | probe D06-bar, a folder holding a character .NET Framework rejects: legacy threw ArgumentException from Path.Combine (outside the try), modern returned; control D06-ctl returned on both |
| Duplicati | `Duplicati.Library.Backend.Rclone::Delete(string)` | EQ006 | not-constructible | confirmed | P2-247 |  | probe D11-bar, a remote name holding a character .NET Framework rejects: legacy threw ArgumentException from Path.Combine, modern threw Win32Exception from Process.Start; receiver state reachable from Rclone(url, options) as for D10; control D11-ctl Win32Exception on both |
| Duplicati | `Duplicati.Library.Backend.Rclone::Get(string,string)` | EQ006 | not-constructible | confirmed | P2-247 |  | probe D10-bar, a remote name holding a character .NET Framework rejects: legacy threw ArgumentException from Path.Combine, modern passed it and threw Win32Exception from Process.Start; receiver fields (a remote path, an executable name holding a rejected character so nothing can start) are a state Rclone(url, options) leaves from its rclone-remote-path and rclone-executable options; control D10-ctl Win32Exception on both |
| Duplicati | `Duplicati.Library.Backend.SharePointBackend::uploadFileSlicePerSlice(Microsoft.SharePoint.Client.ClientContext,Microsoft.SharePoint.Client.Folder,System.IO.Stream,string,System.Threading.CancellationToken)` | EQ006 | not-constructible | confirmed | P2-252 |  | probe with an awaited task: legacy faulted with ArgumentException (Path.GetFileName), modern NullReferenceException |
| Duplicati | `Duplicati.Library.Common.IO.VssBackupComponentsHelper::CreateVssBackupComponents()` | EQ006 | not-constructible | false positive | P2-247 | P2-261 | Path.Combine's parts are the executing assembly's directory (a Windows directory name, which cannot hold a character .NET Framework rejects) and constants, so the row's witness cannot occur |
| Duplicati | `Duplicati.Library.Main.BackendManager::CalculateFileHash(string)` | EQ006 | not-constructible | confirmed | P2-247 |  | probe D00-bar, a path holding a character .NET Framework rejects: legacy threw ArgumentException (File.OpenRead), modern threw IOException; control D00-ctl (a missing valid name) FileNotFoundException on both |
| Duplicati | `Duplicati.Library.Main.BasicResults::TaskControlRendevouz()` | EQ006 | not-constructible | confirmed | P2-247 |  | probe D01-abortb, receiver in the Abort state with the pause event set (fields; the state new DeleteResults() then Abort() leaves): modern threw PlatformNotSupportedException from Thread.Abort, legacy gave no answer because Thread.Abort aborted the driver's calling thread (the member working on .NET Framework); D01-abort was an argument-form error |
| Duplicati | `Duplicati.Library.Main.Database.LocalDatabase::CreateConnection(string)` | EQ006 | not-constructible | confirmed | P2-247 |  | probe D03-bar, a path holding a character .NET Framework rejects: legacy threw ArgumentException at Path.GetFullPath, modern passed it and threw FileNotFoundException from the SQLite loader; control D03-ctl (a valid name) gives that loader FileNotFoundException on both sides (the driver ships no SQLite), so the split is GetFullPath's validation |
| Duplicati | `Duplicati.Library.Main.Operation.Common.BackendHandler.FileEntryItem::CalculateFileHash(string)` | EQ006 | not-constructible | confirmed | P2-247 |  | probe D02-bar, a path holding a character .NET Framework rejects: legacy threw ArgumentException (File.OpenRead), modern threw IOException; control D02-ctl FileNotFoundException on both |
| Duplicati | `Duplicati.Library.Snapshots.LinuxSnapshot.SnapShot::ExecuteCommand(string,string,int)` | EQ006 | not-constructible | confirmed | P2-247 |  | probe D04-bar, a program name holding a character .NET Framework rejects (so no process can start): legacy threw ArgumentException from Path.Combine outside the try, modern passed it and threw System.Exception wrapping Process.Start's failure; control D04-ctl System.Exception on both; LinuxSnapshot runs on POSIX hosts only, member called directly on these runtimes |
| Duplicati | `Duplicati.Library.Snapshots.WindowsSnapshot::ConvertToLocalPath(string)` | EQ006 | not-constructible | confirmed | P2-247 |  | probe D05-bar, a path holding a character .NET Framework rejects: legacy threw ArgumentException from Path.IsPathRooted, modern threw InvalidOperationException (not rooted); receiver created without a constructor, but the outcome is decided before any field is read, so every constructed WindowsSnapshot gives it; control D05-ctl InvalidOperationException on both |
| Duplicati | `Duplicati.Library.Common.IO.SystemIOWindows::DirectoryCreate(string)` | EQ006 (observed) | reproduced | false positive | P2-247 | P2-260 | the row does not apply: PrefixWithUNC makes every path an \\?\ extended path, and .NET Framework with UseLegacyPathHandling=false (as Duplicati's hosts run) does not validate characters in one (Directory.CreateDirectory on one holding a rejected character threw IOException on .NET Framework 4.8, not ArgumentException); the legacy ArgumentException in the replay and probes is the harness: the legacy driver runs .NET Framework's legacy path handling (an app.config sku and no TargetFrameworkAttribute), which rejects every \\?\ path with ArgumentException, even a legal one (probes D19-cctl, D15-nctl, D16-nctl, D17-nctl); Duplicati's hosts target 4.7.1 and set UseLegacyPathHandling=false |
| Duplicati | `Duplicati.Library.Common.IO.SystemIOWindows::DirectoryDelete(string,bool)` | EQ006 (observed) | reproduced | false positive | P2-247 | P2-260 | the row does not apply: every path is an \\?\ extended path, which .NET Framework with UseLegacyPathHandling=false does not validate (Directory.Delete on one holding a rejected character threw IOException on .NET Framework 4.8); the legacy ArgumentException in the replay and probes is the harness: the legacy driver runs .NET Framework's legacy path handling (an app.config sku and no TargetFrameworkAttribute), which rejects every \\?\ path with ArgumentException, even a legal one (probes D19-cctl, D15-nctl, D16-nctl, D17-nctl); Duplicati's hosts target 4.7.1 and set UseLegacyPathHandling=false |
| Duplicati | `Duplicati.Library.Common.IO.SystemIOWindows::FileCreate(string)` | EQ006 (observed) | reproduced | false positive | P2-247 | P2-260 | the row does not apply: every path is an \\?\ extended path, which .NET Framework with UseLegacyPathHandling=false does not validate (File.Create on one holding a rejected character threw IOException on .NET Framework 4.8); the legacy ArgumentException in the replay and probes is the harness: the legacy driver runs .NET Framework's legacy path handling (an app.config sku and no TargetFrameworkAttribute), which rejects every \\?\ path with ArgumentException, even a legal one (probes D19-cctl, D15-nctl, D16-nctl, D17-nctl); Duplicati's hosts target 4.7.1 and set UseLegacyPathHandling=false |
| Duplicati | `Duplicati.Library.Common.IO.SystemIOWindows::FileDelete(string)` | EQ006 (observed) | reproduced | false positive | P2-247 | P2-260 | the row does not apply: every path is an \\?\ extended path, which .NET Framework with UseLegacyPathHandling=false does not validate (File.Delete on one holding a rejected character threw IOException on .NET Framework 4.8); the legacy ArgumentException in the replay and probes is the harness: the legacy driver runs .NET Framework's legacy path handling (an app.config sku and no TargetFrameworkAttribute), which rejects every \\?\ path with ArgumentException, even a legal one (probes D19-cctl, D15-nctl, D16-nctl, D17-nctl); Duplicati's hosts target 4.7.1 and set UseLegacyPathHandling=false |
| Duplicati | `Duplicati.Library.Common.IO.SystemIOWindows::GetFileAttributes(string)` | EQ006 (observed) | reproduced | false positive | P2-247 | P2-260 | P2-237 confirmed it by its replay and a `probe` (legacy ArgumentException, modern FileNotFoundException); the member passes `PrefixWithUNC(path)`, a `\\?\` path, and run outside equiv as Duplicati's hosts run (.NET Framework 4.8, target 4.7.1, `UseLegacyPathHandling=false`) against a net5.0 build on 5.0.17, both sides give the same outcome on every input (FileNotFoundException, IOException, or the attributes); the legacy ArgumentException is the driver's legacy path handling (P2-259), and this run's `reproduced` replay is the same |
| Duplicati | `Duplicati.Library.Common.IO.SystemIOWindows::PathGetFileName(string)` | EQ006 (observed) | reproduced | undetermined | P2-247 |  | the named path-character row cannot fire on the `\\?\` path `PrefixWithUNC` makes; the replay shows another returned-value difference on a relative name (device-path root parsing) under the harness's legacy path handling (P2-259), not checked under the solution's configuration |
| Duplicati | `Duplicati.Library.Common.IO.SystemIOWindows::PathGetFullPath(string)` | EQ006 (observed) | reproduced | false positive | P2-247 | P2-260 | the row does not apply: every path reaches Path.GetFullPath as an \\?\ extended path, which .NET Framework with UseLegacyPathHandling=false returns unvalidated (measured on .NET Framework 4.8), so after StripUNCPrefix both sides return the same string (modern D19-cbar); the legacy ArgumentException in the replay and probes is the harness: the legacy driver runs .NET Framework's legacy path handling (an app.config sku and no TargetFrameworkAttribute), which rejects every \\?\ path with ArgumentException, even a legal one (probes D19-cctl, D15-nctl, D16-nctl, D17-nctl); Duplicati's hosts target 4.7.1 and set UseLegacyPathHandling=false |
| Duplicati | `Duplicati.Library.Snapshots.MSSQLDB::GetHashCode()` | EQ006 (observed) | reproduced | confirmed | P2-245 |  | replay reproduced on an uninitialized receiver a public constructor reaches (P2-245 Finding 1) |
| OpenRA | `OpenRA.Mods.Common.Commands.ChatCommands::OnChat(string,string)` | EQ006 | not-applicable | confirmed | P2-247 |  | probe P12-a (and P12-b), a message starting with a character ICU ignores and NLS does not (NUL, U+200B) before the slash, receiver by its public constructor: legacy StartsWith was false and OnChat returned, modern StartsWith was true and took the command branch, which threw NullReferenceException in Game.Debug (no OrderManager in the driver); control P12-ctl equal |
| OpenRA | `OpenRA.Platform::get_RuntimeVersion()` | EQ006 (observed) | reproduced | confirmed | P2-237 |  | 22-version: legacy returned, modern returned a different string (each reports its own runtime version) |
| Git Extensions 9860 | `BugReporter.Program::Main()` | EQ002 | not-constructible | confirmed | P2-212 |  | P2-212 (kept from P2-124) |
| Git Extensions 9860 | `GitExtensions.Plugins.Gource.GourcePlugin::DownloadFile(string,string)` | EQ002 | not-constructible | confirmed | P2-212 |  | P2-212 (kept from P2-124) |
| Git Extensions 9860 | `GitExtensions.Plugins.Gource.GourcePlugin::SearchForGourceUrl()` | EQ002 | not-constructible | confirmed | P2-212 |  | P2-212 (kept from P2-124) |
| Git Extensions 9860 | `GitExtensions.Program::Main()` | EQ002 | not-constructible | confirmed | P2-212 |  | P2-212 (kept from P2-124) |
| Git Extensions 9860 | `ResourceManager.Xliff.TranslationSerializer::Serialize(ResourceManager.Xliff.TranslationFile,string)` | EQ002 | not-constructible | confirmed | P2-212 |  | P2-212 (kept from P2-124) |
| Jellyfin | `Jellyfin.Server.Helpers.StartupHelpers::PerformStaticInitialization()` | EQ002 | not-constructible | confirmed | P2-212 |  | P2-212 (kept from P2-124, P2-066) |

## Appendix B: P2-237's 71

| Pair | # | Procedure | Sampled | Now | P2-237 obstacle class | Classification | Decided by | Cause | Oracle or trace |
|---|---|---|---|---|---|---|---|---|---|
| Git Extensions 8522 | 0 | `EasyHook.HookAccessControl::SetInclusiveACL(int[])` | EQ002 | EQ002 | not callable by a driver | confirmed | P2-250 |  | real: the modern copy compiles its native calls out (SUPPORT_THEMES undefined); P2-245 probe: legacy threw DllNotFoundException, modern returned |
| Git Extensions 8522 | 3 | `EasyHook.LocalHook::IsThreadIntercepted(int)` | EQ002 | EQ002 | not callable by a driver | confirmed | P2-250 |  | real: the modern copy compiles its native calls out (SUPPORT_THEMES undefined), so modern returns false without asking |
| Git Extensions 8522 | 6 | `GitCommands.EnvironmentPathsProvider::IsValidPath(string)` | EQ002 | EQ002 | `probe` equal on every input | undetermined | P2-247 |  | probe ran with equal outcomes on every input tried (wildcard characters, a plain relative path, a path through an existing file); a difference needs a path whose attribute query fails with an access or I/O error other than not-found (host files) |
| Git Extensions 8522 | 7 | `EasyHook.HookAccessControl::SetExclusiveACL(int[])` | EQ002 | EQ002 | not callable by a driver | confirmed | P2-250 |  | real: the modern copy compiles its native calls out (SUPPORT_THEMES undefined); P2-245 probe: legacy threw DllNotFoundException, modern returned |
| Git Extensions 8522 | 11 | `GitUI.UserControls.RevisionGrid.Graph.RevisionGraphRevision::get_StartSegments()` | EQ002 | EQ002 | not callable by a driver | undetermined | P2-247 |  | result not comparable: the getter's declared type changed from SynchronizedCollection to ConcurrentBag, which the driver does not compare (P2-245 probe 11-a) |
| Git Extensions 8522 | 12 | `GitUIPluginInterfaces.ManagedExtensibility::TryLoadAssembly(System.IO.FileInfo)` | EQ002 | EQ002 | not callable by a driver | undetermined | P2-247 |  | result not comparable: the probe reports an Assembly-typed result as NotComparable on both sides, even for a file that is not an assembly, where both sides return null |
| Git Extensions 8522 | 13 | `GitUI.Theming.ThemeLoader::StyleRuleThemeException(ExCSS.StyleRule,string)` | EQ002 | EQ002 | not callable by a driver | undetermined | P2-247 |  | not callable: ExCSS.StyleRule has no constructor shared by ExCSS 2.0.6 and 4.1.0, and a third-party type cannot be built by fields |
| Git Extensions 8522 | 14 | `BugReporter.Program::Main()` | EQ002 | EQ002 | not callable by a driver | undetermined | P2-247 |  | not safe to run: BugReporter Main runs a WinForms message loop (Application.Run) |
| Git Extensions 8522 | 15 | `Git.hub.Client::setOAuth2Token(string)` | EQ002 | EQ002 | a field written | undetermined | P2-247 |  | void method: the difference is only the authenticator object written to a RestClient field, which probe does not observe |
| Git Extensions 8522 | 16 | `GitUI.OsShellUtil::PickFolder(System.Windows.Forms.IWin32Window,string)` | EQ002 | EQ002 | an argument no driver built | undetermined | P2-247 |  | the IWin32Window parameter is refused (needs a live window) and the body opens a folder dialog |
| Git Extensions 8522 | 18 | `EasyHook.LocalHook::get_HookBypassAddress()` | EQ002 | EQ002 | not callable by a driver | confirmed | P2-250 |  | real: the modern copy compiles its native calls out (SUPPORT_THEMES undefined), so modern returns IntPtr.Zero without asking |
| Git Extensions 8522 | 19 | `CommonTestUtils.MockExecutable.MockProcess::Verify()` | EQ002 | EQ002 | not callable by a driver | false positive | P2-245 | P2-269 | the added CanRead guard only matters for a closed stream still held by StandardInput; the solution closes input only through StreamWriter.Close, which on .NET Framework nulls BaseStream (legacy skips) and on .NET 5 leaves CanRead false (modern skips), so both pass; probe 19-b (receiver by the nested type constructor MockProcess()): both returned |
| Git Extensions 8522 | 20 | `GitUI.Theming.ThemeModule::ResetGdiCaches()` | EQ002 | EQ002 | not callable by a driver | false positive | P2-245 | P2-269 | probe 20-a: both returned; on .NET Framework 4.8 the reflected System.Drawing members exist with the expected types, so the InvalidCastException the model claims for legacy does not happen, and the modern body is compiled out (SUPPORT_THEMES undefined) |
| Git Extensions 8522 | 22 | `GitUI.CommitInfo.CommitInfo.BranchComparer::Compare(string,string)` | EQ002 | EQ002 | not callable by a driver | confirmed | P2-253 |  | probe on Microsoft.WindowsDesktop.App 5.0: legacy and modern returned different values (Comparer<string>.Default against StringComparer.Ordinal) |
| Git Extensions 8522 | 23 | `ConEmu.WinForms.AnsiStreamChunkEventArgs::GetText(System.Text.Encoding)` | EQ006 | EQ006 | not callable by a driver | confirmed | P2-245 |  | probes 23-a, 23-c, 23-d (receiver by constructor, encoding by UTF8Encoding()): both returned, different strings (a different count of U+FFFD for ill-formed UTF-8); 23-b equal; receiver needs no trace |
| Git Extensions 8522 | 24 | `GitCommands.GitRefName::GetRemoteName(string)` | EQ006 | EQ007 | not safe to run, the host, or a result not comparable | undetermined | P2-247 |  | the culture-sensitive StartsWith only feeds a Debug.Assert, so the returned value is the same; the outcome differs only by an assertion failure in a Debug build, which ends the probe server (P2-258) |
| Git Extensions 8522 | 25 | `GitUI.NBugReports.BugReportInvoker::Report(System.Exception,bool)` | EQ006 | EQ007 | an argument no driver built | undetermined | P2-247 |  | not safe to run: the body shows a TaskDialog or starts BugReporter.exe (Process.Start) |
| Git Extensions 8522 | 30 | `GitCommands.Settings.ConfigFileSettings::CreateSystemWide(bool)` | EQ006 | EQ007 | not safe to run, the host, or a result not comparable | undetermined | P2-247 |  | depends on the host's settings: needs the stored GitBinDir setting to hold an invalid path character; the result ConfigFileSettings is not comparable |
| Git Extensions 8522 | 31 | `ICSharpCode.TextEditor.Document.DocumentFactory::CreateFromFile(string)` | EQ006 | EQ007 | not safe to run, the host, or a result not comparable | undetermined | P2-247 |  | result not comparable (IDocument) and needs an existing file with ANSI-encoded bytes |
| Git Extensions 8522 | 33 | `GitCommands.ExternalLinks.ExternalLink::GetHashCode()` | EQ006 | EQ007 | not callable by a driver | confirmed | P2-245 |  | probe 33-a (receiver by public constructor ExternalLink(string?, string)): both returned, different values (string.GetHashCode is randomized per process on .NET 5); the run's replay also reproduced on uninitialized ExternalLink.Uri, a state that constructor reaches |
| Git Extensions 8522 | 34 | `GitCommandsTests.Config.ConfigFileTest::Cleanup()` | EQ006 | EQ007 | not safe to run, the host, or a result not comparable | undetermined | P2-247 |  | needs a config file the process cannot write (host access rights), and the body deletes it |
| Git Extensions 8522 | 37 | `CommonTestUtils.GitModuleTestHelper::EnsureCreatedInTempFolder(string)` | EQ006 | EQ007 | not callable by a driver | confirmed | P2-245 |  | probes 37-a and 37-b (receiver by fields TemporaryPath): legacy threw ArgumentException, modern returned (an ignorable NUL or zero-width space under ICU, not under NLS); trace: the GitModuleTestHelper(string) constructor sets TemporaryPath to Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()), which is the probed value when TEMP is C:\Temp, and public CreateFile passes its parentDir argument straight through |
| Git Extensions 8522 | 39 | `CommonTestUtils.GitModuleTestHelper::CreateRepoFile(string,string,string)` | EQ006 | EQ007 | not callable by a driver | confirmed | P2-245 |  | probe 39-a (receiver by fields TemporaryPath, Module.WorkingDir): legacy threw ArgumentException (Path.IsPathRooted rejects '\\|' on .NET Framework), modern threw IOException (directory creation fails later); trace: the constructor sets TemporaryPath under the host temp folder and Module = new GitModule(Path.Combine(TemporaryPath, repositoryName)), whose WorkingDir is that path with a trailing separator |
| Git Extensions 8522 | 40 | `GitCommands.FileInfoExtensions::MakeFileTemporaryWritable(string,System.Action<string>)` | EQ006 | EQ007 | an argument no driver built | undetermined | P2-247 |  | needs an existing file whose attributes the process cannot write (host files outside the temp folder); P2-245 probe 40-a returned on both sides |
| Git Extensions 8522 | 41 | `GitUI.CommandsDialogs.FormVerify::GuessFileTypeWithContent(string)` | EQ006 | EQ006 | not callable by a driver | confirmed | P2-253 |  | probe on Microsoft.WindowsDesktop.App 5.0: legacy and modern returned different file names (ICU ignores a leading NUL or zero-width space) |
| Git Extensions 8522 | 44 | `ConEmu.WinForms.AnsiStreamChunkEventArgs::GetMbcsText()` | EQ006 | EQ007 | not callable by a driver | confirmed | P2-245 |  | probes 44-a and 44-b (receiver by constructor): both returned, different strings; Encoding.Default is the ANSI code page on .NET Framework and UTF-8 on .NET 5 |
| Git Extensions 8522 | 45 | `GitExtensions.Plugins.Gource.GourceStart::linkLabel1_LinkClicked(object,System.Windows.Forms.LinkLabelLinkClickedEventArgs)` | EQ006 | EQ007 | not callable by a driver | undetermined | P2-247 |  | an instance member of the GourceStart Form (window receiver refused) and it starts a URL through the shell |
| Git Extensions 8522 | 47 | `ICSharpCode.TextEditor.Document.FileSyntaxModeProvider::GetSyntaxModeFile(ICSharpCode.TextEditor.Document.SyntaxMode)` | EQ006 | EQ007 | not callable by a driver | confirmed | P2-245 |  | probe 47-a (receiver and SyntaxMode by constructor): legacy threw ArgumentException (Path.Combine rejects '\\|' on .NET Framework), modern threw HighlightingDefinitionInvalidException |
| Git Extensions 8522 | 49 | `GitUI.Interops.DwmApi.DwmApi::IsWindows10BuildOrGreater(int)` | EQ006 | EQ007 | not callable by a driver | undetermined | P2-247 |  | depends on the host executable's manifest: P2-245 probe 49-a differed only because the driver host has no Windows 10 compatibility manifest, while GitExtensions.exe declares one, under which .NET Framework 4.8 reports the real version too |
| Git Extensions 8522 | 51 | `ICSharpCode.TextEditor.Util.FileReader::AutoDetect(System.IO.Stream,byte,byte,System.Text.Encoding)` | EQ006 | EQ007 | not callable by a driver | undetermined | P2-247 |  | result not comparable: the claimed difference is the StreamReader's encoding (Encoding.Default), which the driver does not compare (P2-245 probe 51-a) |
| Duplicati | 0 | `Duplicati.GUI.TrayIcon.Program::GetWinformsInstance()` | EQ002 | EQ002 | not callable by a driver | undetermined | P2-247 |  | probe P00-plain: both sides returned a TrayIconBase that probe does not compare; the difference is only the runner type constructed (WinFormsRunner against Win32Runner) |
| Duplicati | 3 | `Duplicati.Library.Backend.S3IAM::CreateUnprivilegedUser(string,string,string)` | EQ002 | EQ002 | not callable by a driver | undetermined | P2-247 |  | not safe to run: CreateUser, PutUserPolicy and CreateAccessKey call a live AWS IAM service before the outcomes split |
| Duplicati | 6 | `Duplicati.Library.AutoUpdater.UpdaterManager::RunFromMostRecent(System.Reflection.MethodInfo,string[],Duplicati.Library.AutoUpdater.AutoUpdateStrategy)` | EQ002 | EQ002 | an argument no driver built | undetermined | P2-247 |  | not safe to run: both sides spawn a process; the difference also needs the AUTOUPDATER_USE_APPDOMAIN environment variable |
| Duplicati | 8 | `Duplicati.Library.Utility.Power.WindowsPowerSupplyState::GetSource()` | EQ002 | EQ002 | `probe` differed for another reason (P2-240) | undetermined | P2-247 |  | probe P08-plain ran with equal outcomes (both returned the same source; P2-240's FileNotFoundException is gone); the difference depends on the host's battery hardware |
| Duplicati | 14 | `Duplicati.Library.Backend.AzureBlob.AzureBlobWrapper::AddFileStream(string,System.IO.Stream,System.Threading.CancellationToken)` | EQ002 | EQ002 | not callable by a driver | confirmed | P2-252 |  | probe with an awaited task: legacy faulted with ArgumentNullException, modern NullReferenceException (the new source.Length read) |
| Duplicati | 22 | `Duplicati.GUI.TrayIcon.Program::TryGetWinforms()` | EQ002 | EQ002 | not callable by a driver | false positive | P2-245 | P2-269 | differs only when Platform.IsClientWindows is false, a non-Windows host, where .NET Framework 4.8 does not run; probe 22-plain: both returned true on Windows |
| Duplicati | 24 | `Duplicati.GUI.TrayIcon.HttpServerConnection::Close()` | EQ006 | EQ007 | not callable by a driver | undetermined | P2-247 |  | not callable in a reachable state: the constructor needs a live Duplicati server and starts the poll threads; a receiver built without it has unstarted threads, a state no constructor leaves (P2-245's probe) |
| Duplicati | 29 | `Duplicati.Library.AutoUpdater.UpdaterManager::GetBestUpdateVersion(bool)` | EQ006 | EQ006 | not callable by a driver | undetermined | P2-247 |  | the difference is only in the call trace: the Path.Combine of the current file's content sits in a catch-all whose only effect is the OnError event; an INSTALLDIR holding a rejected character fails the type's static constructor on both sides first; the return also differs by the edited ApplicationBase key, not the named change |
| Duplicati | 30 | `Duplicati.Library.Backend.Mega.MegaBackend::PutAsync(string,string,System.Threading.CancellationToken)` | EQ006 | EQ007 | an argument no driver built | confirmed | P2-245 |  | probe 30-pipe: a filename holding a character .NET Framework rejects: legacy threw ArgumentException, modern IOException, from File.OpenRead before any network call |
| Duplicati | 32 | `Duplicati.Library.Compression.FileArchiveDirectory::CreateFile(string,Duplicati.Library.Interface.CompressionHint,System.DateTime)` | EQ006 | EQ007 | not callable by a driver | confirmed | P2-245 |  | probe 32-pipe-ctor: legacy threw ArgumentException (Path.Combine), modern IOException (File.Create); receiver by its constructor, DateTime by its constructor |
| Duplicati | 33 | `Duplicati.Library.Compression.FileArchiveDirectory::FileExists(string)` | EQ006 | EQ007 | not callable by a driver | confirmed | P2-245 |  | probe 33-pipe: legacy threw ArgumentException (Path.Combine), modern returned false; receiver by its constructor |
| Duplicati | 34 | `Duplicati.Library.Compression.FileArchiveDirectory::ListFiles(string)` | EQ006 | EQ007 | not callable by a driver | confirmed | P2-245 |  | probe 34-pipe: legacy threw ArgumentException (Path.Combine), modern returned an empty array; receiver by its constructor |
| Duplicati | 36 | `Duplicati.Library.Backend.WEBDAV::PutAsync(string,string,System.Threading.CancellationToken)` | EQ006 | EQ007 | an argument no driver built | confirmed | P2-245 |  | probe 36-pipe: a filename holding a character .NET Framework rejects: legacy threw ArgumentException, modern IOException, from File.OpenRead before any network call |
| Duplicati | 42 | `Duplicati.Library.Compression.FileArchiveDirectory::OpenRead(string)` | EQ006 | EQ007 | not callable by a driver | confirmed | P2-245 |  | probe 42-pipe: legacy threw ArgumentException (Path.Combine), modern IOException (File.OpenRead); receiver by its constructor |
| Duplicati | 44 | `Duplicati.CommandLine.Commands.PeriodicOutput::Dispose()` | EQ006 | EQ007 | not callable by a driver | confirmed | P2-245 |  | probes 44-ctor2 and 44-ctor3: legacy returned, modern threw PlatformNotSupportedException (Thread.Abort on the live writer thread); receiver by its constructor, which starts only a background thread blocked on an event (safe) |
| Duplicati | 45 | `Duplicati.Library.Backend.Dropbox::PutAsync(string,string,System.Threading.CancellationToken)` | EQ006 | EQ007 | an argument no driver built | confirmed | P2-245 |  | probe 45-pipe: a filename holding a character .NET Framework rejects: legacy threw ArgumentException, modern IOException, from File.OpenRead before any network call |
| Duplicati | 47 | `Duplicati.Library.Backend.SharePointBackend::uploadFileSlicePerSlice(Microsoft.SharePoint.Client.ClientContext,Microsoft.SharePoint.Client.Folder,System.IO.Stream,string,System.Threading.CancellationToken)` | EQ006 | EQ006 | not callable by a driver | confirmed | P2-252 |  | probe with an awaited task: legacy faulted with ArgumentException (Path.GetFileName), modern NullReferenceException |
| Duplicati | 48 | `Duplicati.Library.Backend.GoogleCloudStorage.GoogleCloudStorage::PutAsync(string,string,System.Threading.CancellationToken)` | EQ006 | EQ007 | an argument no driver built | confirmed | P2-245 |  | probe 48-pipe: a filename holding a character .NET Framework rejects: legacy threw ArgumentException, modern IOException, from File.OpenRead before any network call |
| Duplicati | 50 | `Duplicati.Library.Snapshots.SnapshotBase::GetAttributes(string)` | EQ006 | EQ007 | not callable by a driver | confirmed | P2-245 |  | probe 50-pipe: legacy threw ArgumentException (Path.GetFullPath in NoSnapshotLinux.ConvertToSnapshotPath, the same path-character check), modern IOException (File.GetAttributes); NoSnapshotLinux has no fields, and BackupHandler creates it with its parameterless constructor |
| Duplicati | 51 | `Duplicati.Library.UsageReporter.ReportSet::DoInitCLR()` | EQ006 | EQ007 | not callable by a driver | undetermined | P2-247 |  | the difference is only a static field (Cached_CLRVersion) written by a private static void member, which no outcome observes |
| OpenRA | 0 | `OpenRA.Mods.D2k.UtilityCommands.D2kMapImporter::.ctor(string,string,OpenRA.Ruleset)` | EQ006 | EQ007 | an OpenRA mod project under `probe` (P2-241) | false positive | P2-247 | P2-223 | File.OpenRead's exception is caught by a catch-all that only writes it to the console, and the finally's Close on the still-null stream then throws NullReferenceException on both sides (probe P00-a and P00-ctl: both threw NullReferenceException) |
| OpenRA | 2 | `OpenRA.Mods.Common.Traits.RotationPaletteEffect::StartsWithAny(string,System.Collections.Generic.HashSet<string>)` | EQ006 | EQ007 | an OpenRA mod project under `probe` (P2-241) | undetermined | P2-247 |  | not callable with a witness: probe cannot build a non-empty HashSet<string> argument (its element-taking constructor takes an IEnumerable<string>, which probe cannot build), and with an empty set StartsWith is never called |
| OpenRA | 4 | `OpenRA.Mods.Common.Traits.PlayerStatistics::ResolveOrder(OpenRA.Actor,OpenRA.Order)` | EQ006 | EQ007 | not callable by a driver | undetermined | P2-247 |  | the culture-sensitive StartsWith only decides whether ResolveOrder (void) increments the field OrderCount; the difference is a field written by a void member (P2-245 probe 4-a) |
| OpenRA | 5 | `OpenRA.Download::CancelAsync()` | EQ006 | EQ007 | not callable by a driver | undetermined | P2-247 |  | not safe to run: the WebClient.CancelAsync difference needs a download in progress (network) and is a timing difference in the call trace; with no client both sides return alike |
| OpenRA | 6 | `OpenRA.FileSystem.ZipFileLoader.ReadWriteZipFile::.ctor(string,bool)` | EQ006 | EQ007 | not callable by a driver | confirmed | P2-245 |  | probe 6-c and 6-d (the constructor called directly, create false): legacy threw ArgumentException, modern threw IOException (File.ReadAllBytes); 21-c shows the same through its caller Folder.OpenPackage |
| OpenRA | 7 | `OpenRA.CryptoUtil::DecryptString(System.Security.Cryptography.RSAParameters,string)` | EQ006 | EQ007 | an argument no driver built | confirmed | P2-245 |  | probe 7-a (a generated key, data decrypting to an ill-formed UTF-8 sequence): both returned, different strings (Encoding.UTF8.GetString replaces the ill-formed bytes with a different number of replacement characters); the run's replay also built and called it (not-constructible: the call traces differ) |
| OpenRA | 8 | `OpenRA.FileSystem.Folder::.ctor(string)` | EQ006 | EQ007 | a constructor or a struct (P2-242) | confirmed | P2-247 |  | probe P08-a, a path holding a character .NET Framework rejects (relative): legacy threw ArgumentException from Directory.CreateDirectory, modern threw IOException; control P08-ctl constructed on both |
| OpenRA | 11 | `OpenRA.float2::Max(OpenRA.float2,OpenRA.float2)` | EQ006 | EQ007 | a constructor or a struct (P2-242) | confirmed | P2-247 |  | probe P11-a, positive zero against negative zero in both components: legacy Math.Max returned negative zero, modern returned positive zero; control P11-ctl equal |
| OpenRA | 12 | `OpenRA.Mods.Common.Commands.ChatCommands::OnChat(string,string)` | EQ006 | EQ006 | an OpenRA mod project under `probe` (P2-241) | confirmed | P2-247 |  | probe P12-a (and P12-b), a message starting with a character ICU ignores and NLS does not (NUL, U+200B) before the slash, receiver by its public constructor: legacy StartsWith was false and OnChat returned, modern StartsWith was true and took the command branch, which threw NullReferenceException in Game.Debug (no OrderManager in the driver); control P12-ctl equal |
| OpenRA | 14 | `OpenRA.Mods.Cnc.FileFormats.VxlReader::Load(string)` | EQ006 | EQ007 | an OpenRA mod project under `probe` (P2-241) | confirmed | P2-247 |  | probe P14-a, a path holding a character .NET Framework rejects (relative): legacy File.OpenRead threw ArgumentException, modern threw IOException; control P14-ctl both FileNotFoundException |
| OpenRA | 17 | `OpenRA.Mods.Common.Widgets.ScrollPanelWidget::Scroll(int,bool)` | EQ006 | EQ007 | an OpenRA mod project under `probe` (P2-241) | undetermined | P2-247 |  | the Math.Min/Max result only becomes the field targetListOffset written through SetListOffset by Scroll (void); the difference is a field written by a void member, and Scroll also needs Game.Settings |
| OpenRA | 19 | `OpenRA.float2::FromAngle(float)` | EQ006 | EQ007 | a constructor or a struct (P2-242) | confirmed | P2-247 |  | probe P19-b (and P19-c), a very large angle: legacy Math.Sin and Math.Cos returned the argument unreduced (both components equal the input), modern returned reduced values in [-1, 1]; moderate angles P19-a and P19-d equal |
| OpenRA | 20 | `OpenRA.StreamExts::ReadString(System.IO.Stream,System.Text.Encoding,int)` | EQ006 | EQ007 | an argument no driver built | confirmed | P2-245 |  | probe 20-c (a length-prefixed ill-formed UTF-8 sequence): both returned, different strings (Encoding.GetString replaces the ill-formed bytes with a different number of replacement characters) |
| OpenRA | 21 | `OpenRA.FileSystem.Folder::OpenPackage(string,OpenRA.FileSystem.FileSystem)` | EQ006 | EQ007 | not callable by a driver | confirmed | P2-245 |  | probe 21-a: legacy threw ArgumentException (Path.Combine rejects the character), modern returned (an IReadOnlyPackage-typed value, not compared); 21-b equal, 21-c differs through File.ReadAllBytes |
| OpenRA | 23 | `OpenRA.FileSystem.Folder::Delete(string)` | EQ006 | EQ007 | not callable by a driver | confirmed | P2-245 |  | probe 23-a: legacy threw ArgumentException (Path.Combine), modern returned |
| OpenRA | 24 | `OpenRA.Graphics.ImmutablePalette::.ctor(string,int[])` | EQ006 | EQ007 | a constructor or a struct (P2-242) | confirmed | P2-247 |  | probe P24-a, a path holding a character .NET Framework rejects (relative): legacy File.OpenRead threw ArgumentException, modern threw IOException; control P24-ctl both FileNotFoundException |
| OpenRA | 25 | `OpenRA.FileSystem.Folder::Update(string,byte[])` | EQ006 | EQ007 | not callable by a driver | confirmed | P2-245 |  | probe 25-a: legacy threw ArgumentException (Path.Combine), modern threw IOException (File.Create) |
| OpenRA | 26 | `OpenRA.Mods.Cnc.FileFormats.HvaReader::Load(string)` | EQ006 | EQ007 | an OpenRA mod project under `probe` (P2-241) | confirmed | P2-247 |  | probe P26-a, a path holding a character .NET Framework rejects (relative): legacy File.OpenRead threw ArgumentException, modern threw IOException (before Path.GetFileName runs); control P26-ctl both FileNotFoundException |
| OpenRA | 27 | `OpenRA.Mods.Common.Widgets.Logic.ObserverStatsLogic::AverageOrdersPerMinute(double)` | EQ006 | EQ007 | an OpenRA mod project under `probe` (P2-241) | confirmed | P2-247 |  | probe P27-a, a double beyond 15 significant digits, and P27-b, negative zero, with an uninitialized receiver whose world has a nonzero WorldTick (the constructor stores the World, whose WorldTick counts ticks): legacy ToString(a one-decimal fixed-point format) returned the rounded and the unsigned text, modern the exact and the signed text; control P27-ctl equal |
| OpenRA | 28 | `OpenRA.Mods.Common.Widgets.Logic.GameSaveBrowserLogic::IsLoadPanelEnabled(OpenRA.Manifest)` | EQ006 | EQ007 | an OpenRA mod project under `probe` (P2-241) | confirmed | P2-247 |  | probe P28-a, a Manifest whose Metadata.Version holds a character .NET Framework rejects in a path (the constructor loads Metadata from mod.yaml by FieldLoader, so any version string is reachable), uninitialized: legacy Path.Combine threw ArgumentException, modern returned false; control P28-ctl both returned false |
| OpenRA | 29 | `OpenRA.float2::Min(OpenRA.float2,OpenRA.float2)` | EQ006 | EQ007 | a constructor or a struct (P2-242) | confirmed | P2-247 |  | probe P29-a, negative zero against positive zero in both components: legacy Math.Min returned positive zero, modern returned negative zero; control P29-ctl equal |

## Appendix C: the exposure sample (seed 247)

| Pair | Key | Procedure | Witness | Runtime row | Replay | Classification | Cause | Oracle or trace |
|---|---|---|---|---|---|---|---|---|
| Git Extensions 8522 | W00 | `GitUI.Editor.Diff.DiffLineNumAnalyzer::IsMinusLineInCombinedDiff(string)` | model | `System.String::StartsWith(` | not-constructible | confirmed |  | row witness: probe W00-a/W00-b, a line led by a character ICU ignores and NLS does not (NUL, U+200B) under culture-sensitive String.StartsWith: legacy returned false, modern returned true; control W00-c equal |
| Git Extensions 8522 | W01 | `GitExtensions.Plugins.GitFlow.GitFlowForm::lnkGitFlow_LinkClicked(object,System.Windows.Forms.LinkLabelLinkClickedEventArgs)` | model | `System.Diagnostics.Process::Start(` | not-constructible | undetermined |  | not safe to run: the body calls Process.Start on a URL (starts a browser process), on a GitFlowForm (window) receiver |
| Git Extensions 8522 | W02 | `GitCommands.Utils.EnvUtils::IsNet4FullOrHigher()` | model | `System.Environment::get_Version(` | not-constructible | false positive | P2-264 | Environment.Version sends the legacy side to the registry check of the .NET 4 Full key, whose Install value is 1 wherever a net461 app can run, so both sides return true (probe W02-a: both returned true) |
| Git Extensions 8522 | W03 | `GitUI.CommandsDialogs.BrowseDialog.GitStatusMonitor::GitDirWatcherEnableRaisingEvents()` | model | `System.String::StartsWith(` | not-constructible | confirmed |  | row witness: probe W03-a2, on an uninitialized GitStatusMonitor whose watchers hold existing folders (one name holding U+200B, which ICU ignores and NLS does not) under culture-sensitive StartsWith: legacy returned true, modern returned false; control W03-b2 equal; StartWatchingChanges sets these watcher paths from the module's working folder and git folder, so a constructed monitor reaches the state (W03-a/b were a malformed constructor form) |
| Git Extensions 8522 | W04 | `CommonTestUtils.GitModuleTestHelper::Dispose()` | model | `System.IO.File::GetAttributes(string)` | not-constructible | undetermined |  | the difference is only in which files the void Dispose deletes (call trace); a catch-all makes the return equal, and running it deletes files |
| Git Extensions 8522 | W05 | `GitUI.FontUtil::.cctor()` | model | `System.Drawing.Font::FromLogFont(object)` | not-constructible | false positive | P2-268 | Font.FromLogFont's argument is a boxed LOGFONT struct filled by GetThemeFont's out parameter, never the null the row's difference needs |
| Git Extensions 8522 | W06 | `GitExtensions.Plugins.GitStatistics.PieChart.PieChart3D::GetSliceDisplacement(float,float)` | model | `System.Math::Cos(double)` | not-constructible | undetermined |  | not probed: the driver fails a Debug.Assert, which ends the probe server (P2-258) |
| Git Extensions 8522 | W07 | `GitCommands.GitModule::GetSubmoduleFullPath(string)` | model | `System.IO.Path::Combine(string,string)` | not-constructible | confirmed |  | row witness: probe W07-a/W07-b, a submodule path holding a character .NET Framework rejects: legacy threw ArgumentException from Path.Combine, modern returned a path; control W07-c returned on both |
| Git Extensions 8522 | W08 | `GitCommands.AppSettings::get_SettingsFilePath()` | model | `System.IO.Path::Combine(string,string)` | not-constructible | false positive | P2-261 | Path.Combine joins ApplicationDataPath, a runtime-made path (Application.UserAppDataPath, or the executable's folder when portable), with the constant SettingsFileName, so it never sees a character .NET Framework rejects (and running it would write the user's AppData through the AppSettings static constructor) |
| Git Extensions 8522 | W09 | `GitUI.CommandsDialogs.BrowseDialog.FormGitCommandLog::mnuSaveToFile_Click(object,System.EventArgs)` | model | `System.String::EndsWith(` | not-constructible | undetermined |  | not safe to run: opens a SaveFileDialog on a FormGitCommandLog (window) receiver |
| Git Extensions 8522 | W10 | `ICSharpCode.TextEditor.Document.StringTextBufferStrategy::CreateTextBufferFromFile(string)` | model | `System.Text.Encoding::get_Default(` | not-constructible | undetermined |  | the result is not comparable: CreateTextBufferFromFile returns an ITextBufferStrategy, NotComparable on both sides for an ANSI-encoded file |
| Git Extensions 8522 | W11 | `TeamCityIntegration.Settings.TeamCityBuildChooser::ReselectPreviouslySelectedBuild()` | model | `System.Windows.Forms.TreeNodeCollection::Find(` | not-constructible | undetermined |  | not callable: the receiver is a TeamCityBuildChooser Form (window), and the void member only sets a TreeView's selected node |
| Git Extensions 8522 | W12 | `GitCommands.Git.GitBranchNameNormaliser::Rule06(string)` | model | `System.String::StartsWith(` | not-constructible | confirmed |  | row witness: probe W12-a/W12-b, a branch name with a character ICU ignores and NLS does not (NUL, U+200B) beside the slash under culture-sensitive StartsWith/EndsWith: both returned, different strings; control W12-c equal |
| Git Extensions 8522 | W13 | `GitCommands.EnvironmentAbstraction::GetFolderPath(System.Environment.SpecialFolder)` | model | `System.Environment::GetFolderPath(System.Environment.SpecialFolder)` | not-constructible | confirmed |  | row witness: probe W13-c, the special folder the row measured (PrinterShortcuts): legacy returned its path, modern returned an empty string; ApplicationData control W13-d equal (two other absent folders returned empty on both) |
| Git Extensions 8522 | W14 | `GitUI.Editor.Diff.DiffLineNumAnalyzer::IsPlusLineInCombinedDiff(string)` | model | `System.String::StartsWith(` | not-constructible | confirmed |  | row witness: probe W14-a/W14-b, a line led by a character ICU ignores and NLS does not (NUL, U+200B) under culture-sensitive String.StartsWith: legacy returned false, modern returned true |
| Git Extensions 8522 | U00 | `ICSharpCode.TextEditor.Document.HighlightColor::.ctor(System.Xml.XmlElement,ICSharpCode.TextEditor.Document.HighlightColor)` | none (timeout) | `System.String::StartsWith(` | none | undetermined |  | not callable: the XmlElement parameter cannot be built (no public constructor) |
| Git Extensions 8522 | U01 | `GitUI.PathFormatter.NativeMethods::PathCompactPathEx(System.Text.StringBuilder,string,int,int)` | none (opaque) | `extern` | none | undetermined |  | not callable: the probe refuses an extern (DllImport) member |
| Git Extensions 8522 | U02 | `GitUI.ControlHotkeyExtensions::HandleKeyDown(object,System.Windows.Forms.KeyEventArgs)` | none (opaque) | `System.Char::IsLetterOrDigit(char)` | none | undetermined |  | needs a window: the sender must be a TextBoxBase or ComboBox, and the void handler's effect is only the control's text |
| Git Extensions 8522 | U03 | `GitUI.CommandsDialogs.FormBrowse::FillTerminalTab()` | none (abstraction) | `System.IO.Path::GetDirectoryName(string)` | none | false positive | P2-267 | Path.GetDirectoryName runs only inside the CommitInfoTabControl.Selecting handler that FillTerminalTab subscribes and never invokes |
| Git Extensions 8522 | U04 | `GitExtUtils.GitUI.HighDpiMouseCursors::Enable()` | none (opaque) | `System.Reflection.FieldInfo::SetValue(` | none | false positive | P2-266 | FieldInfo.SetValue's change needs a static readonly field; the Cursors fields set (hand, hSplit, vSplit) are plain statics on .NET Framework and absent by that name on .NET 5 (GetField returns null), and a catch-all wraps the body |
| Git Extensions 8522 | U05 | `GitUI.CommandsDialogs.FormPull::BranchesDropDown(object,System.EventArgs)` | none (timeout) | `System.String::StartsWith(` | none | undetermined |  | not callable: the receiver is a FormPull Form (window) |
| Git Extensions 8522 | U06 | `GitUI.UserControls.RevisionGrid.FormQuickItemSelector::InitializeComponent()` | none (timeout) | `System.Windows.Forms.ListBox.ObjectCollection::AddRange(` | none | false positive | P2-268 | ListBox.ObjectCollection.AddRange is called with an array literal, never the null argument the row's exception-type change needs |
| Git Extensions 8522 | U07 | `GitUI.BranchTreePanel.RepoObjectsTree::DoSearch()` | none (opaque) | `System.String::IndexOf(` | none | undetermined |  | not callable: the receiver is a RepoObjectsTree UserControl (window), and the void member only selects a tree node |
| Git Extensions 8522 | U08 | `GitCommands.AppSettings::.cctor()` | none (timeout) | `System.IO.Path::Combine(string,string)` | none | false positive | P2-261 | Path.Combine and Directory.CreateDirectory get runtime-made folders (Environment.GetFolderPath, Application.UserAppDataPath, the executable's folder) joined with constants, never a character .NET Framework rejects (and the static constructor writes the user's AppData and registry, so it was not run) |
| Git Extensions 8522 | U09 | `ResourceManager.Translator::GetTranslation(string)` | none (timeout) | `System.IO.Path::GetFileNameWithoutExtension(string)` | none | false positive | P2-229 | Path.GetFileNameWithoutExtension reads a name yielded by Directory.EnumerateFiles, which the file system made and which holds no character .NET Framework rejects |
| Git Extensions 8522 | U10 | `GitUI.Avatars.InitialsAvatarProvider::GetInitialsFromNames(string[])` | none (abstraction) | `System.Char::IsLetter(` | none | confirmed |  | row witness: probe U10-a/U10-b, a name led by a code point the two runtimes' Unicode tables classify differently (letters added after Unicode 8): legacy returned null, modern returned initials; control U10-c equal |
| Git Extensions 8522 | U11 | `GitExtensions.Plugins.GitHub3.GitHub3Plugin::ConfigureContextMenu(System.Windows.Forms.ContextMenuStrip)` | none (abstraction) | `System.Diagnostics.Process::Start(` | none | false positive | P2-267 | Process.Start runs only inside the menu items' Click handlers that ConfigureContextMenu subscribes and never invokes |
| Git Extensions 8522 | U12 | `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_second_nested_module_with_first_nested_module_precommit()` | none (timeout) | `System.IO.File::Delete(string)` | none | false positive | P2-261 | File.Delete gets a submodule working folder under Path.GetTempPath plus a git-made relative path, joined with a constant file name, never a character .NET Framework rejects (the test also runs git processes, so it was not run) |
| Git Extensions 8522 | U13 | `GitUI.CommandsDialogs.FormFormatPatch::SendMail(string)` | none (timeout) | `System.Net.Mail.Attachment::.ctor(string)` | none | false positive | P2-229 | the Attachment constructor gets a file name from Directory.GetFiles, which the file system made and which holds no character .NET Framework rejects (the member also opens dialogs and sends mail, so it was not run) |
| Git Extensions 8522 | U14 | `GitCommands.Git.GitBranchNameNormaliser::Rule01(string,GitCommands.Git.GitBranchNameOptions)` | none (abstraction) | `System.String::StartsWith(` | none | confirmed |  | row witness: probe U14-a/U14-b, a path component led by a character ICU ignores and NLS does not (NUL, U+200B) before the dot under culture-sensitive StartsWith: both returned, modern prefixed the replacement token and legacy did not |
| Duplicati | W00 | `Duplicati.Library.Backend.Jottacloud::PutAsync(string,string,System.Threading.CancellationToken)` | model | `System.IO.File::OpenRead(string)` | not-constructible | confirmed |  | row witness (the model's inputs are uninterpreted sort elements no probe can build; replay not-constructible): probe W00-bar, a file name holding a character .NET Framework rejects: legacy threw ArgumentException from File.OpenRead, modern threw IOException; control W00-ctl (a missing valid name) FileNotFoundException on both (legacy ArgumentException on an ordinary path rests on .NET Framework's own character check) |
| Duplicati | W01 | `Duplicati.Library.Backend.OpenStack.OpenStackStorage::PutAsync(string,string,System.Threading.CancellationToken)` | model | `System.IO.File::OpenRead(string)` | not-constructible | confirmed |  | row witness (the model's inputs are uninterpreted sort elements no probe can build; replay not-constructible): probe W01-bar, a file name holding a character .NET Framework rejects: legacy threw ArgumentException from File.OpenRead, modern threw IOException; control W01-ctl FileNotFoundException on both (legacy ArgumentException on an ordinary path rests on .NET Framework's own character check) |
| Duplicati | W02 | `Duplicati.Library.Main.Utility::get_SuppressDonationMessages()` | model | `System.IO.Path::Combine(string,string)` | not-constructible | undetermined |  | depends on the host's files: the only witness route is an OEM app-name override file in the working directory holding a rejected character (the embedded app name and the ApplicationData folder cannot hold one); a probe cannot place that file before the static constructor reads it |
| Duplicati | W03 | `Duplicati.Library.Backend.OpenStack.OpenStackStorage::Get(string,string)` | model | `System.IO.File::Create(string)` | not-constructible | confirmed |  | row witness (the model's inputs are uninterpreted sort elements no probe can build; replay not-constructible): probe W03-bar, a file name holding a character .NET Framework rejects: legacy threw ArgumentException from File.Create, modern threw IOException; control W03-ctl (a missing folder) DirectoryNotFoundException on both (legacy ArgumentException on an ordinary path rests on .NET Framework's own character check) |
| Duplicati | W04 | `Duplicati.Library.Backend.TahoeBackend::Get(string,string)` | model | `System.IO.File::Create(string)` | not-constructible | confirmed |  | row witness (the model's inputs are uninterpreted sort elements no probe can build; replay not-constructible): probe W04-bar, a file name holding a character .NET Framework rejects: legacy threw ArgumentException from File.Create, modern threw IOException; control W04-ctl DirectoryNotFoundException on both (legacy ArgumentException on an ordinary path rests on .NET Framework's own character check) |
| Duplicati | W05 | `Duplicati.Library.Utility.TempFile::CreateInFolder(string,bool)` | model | `System.IO.Directory::CreateDirectory(string)` | not-constructible | confirmed |  | row witness (the model's inputs are uninterpreted sort elements no probe can build; replay not-constructible): probe W05-bar, a folder holding a character .NET Framework rejects: legacy threw ArgumentException from Directory.CreateDirectory, modern threw IOException; W05-barnf (no autocreate) legacy ArgumentException from Path.Combine, modern IOException from File.Create; control W05-ctl DirectoryNotFoundException on both (legacy ArgumentException on an ordinary path rests on .NET Framework's own character check) |
| Duplicati | W06 | `Duplicati.Library.AutoUpdater.AutoUpdateSettings::get_SignKey()` | model | `System.Security.Cryptography.RSA::FromXmlString(` | not-constructible | false positive | P2-219 | the row does not apply on net5.0: RSA.FromXmlString threw PlatformNotSupportedException only on .NET Core before 3.0 and is implemented on 5.0; probe W06-plain returned a key object on both sides (NotComparable, neither null), so FromXmlString did not throw on modern |
| Duplicati | W07 | `Duplicati.Library.Utility.TempFolder::.ctor(string)` | model | `System.IO.Directory::CreateDirectory(string)` | not-constructible | confirmed |  | row witness (the model's inputs are uninterpreted sort elements no probe can build; replay not-constructible): probe W07-bar, a folder holding a character .NET Framework rejects: legacy threw ArgumentException from Directory.CreateDirectory, modern threw IOException (legacy ArgumentException on an ordinary path rests on .NET Framework's own character check) |
| Duplicati | W08 | `Duplicati.UnitTest.TestUtils::IndexOf(System.Collections.Generic.List<string>,string)` | model | `System.String::Equals(string,System.StringComparison)` | not-constructible | false positive | P2-224 | the comparison is ordinal: ClientFilenameStringComparison is Ordinal or OrdinalIgnoreCase, which the ICU culture-sensitive change does not reach (and a List<string> with elements cannot be built) |
| Duplicati | W09 | `Duplicati.Library.Snapshots.SnapshotBase::GetLastWriteTimeUtc(string)` | model | `System.IO.File::GetLastWriteTimeUtc(string)` | not-constructible | confirmed |  | row witness (the model's inputs are uninterpreted sort elements no probe can build; replay not-constructible): probe W09-bar, receiver NoSnapshotLinux by its constructor (the only type that does not override the member), a path holding a character .NET Framework rejects: legacy threw ArgumentException from File.GetLastWriteTimeUtc, modern threw IOException; control W09-ctl returned on both (legacy ArgumentException on an ordinary path rests on .NET Framework's own character check); SystemIOLinux/NoSnapshotLinux is selected on POSIX hosts only, so this is confirmed on the pair's detected runtimes only (P2-237's decision) |
| Duplicati | W10 | `Duplicati.Server.WebServer.RESTMethods.Acknowledgements::GET(string,Duplicati.Server.WebServer.RESTMethods.RequestInfo)` | model | `System.IO.File::ReadAllText(string)` | not-constructible | false positive | P2-261 | File.ReadAllText's path is the executing assembly's directory (a Windows directory name, which cannot hold a character .NET Framework rejects) and a constant file name, so the row's witness cannot occur |
| Duplicati | W11 | `Duplicati.Library.Common.IO.SystemIOLinux::FileCreate(string)` | model | `System.IO.File::Create(string)` | not-constructible | confirmed |  | row witness (the model's inputs are uninterpreted sort elements no probe can build; replay not-constructible): probe W11-bar, a path holding a character .NET Framework rejects (SystemIOLinux adds no extended-path prefix): legacy threw ArgumentException from File.Create, modern threw IOException; control W11-ctl DirectoryNotFoundException on both (legacy ArgumentException on an ordinary path rests on .NET Framework's own character check); SystemIOLinux/NoSnapshotLinux is selected on POSIX hosts only, so this is confirmed on the pair's detected runtimes only (P2-237's decision) |
| Duplicati | W12 | `Duplicati.Library.Main.Controller::Abort()` | model | `System.Threading.Thread::Abort(` | not-constructible | confirmed |  | row witness (the model's inputs are uninterpreted sort elements no probe can build; replay not-constructible): probe W12-thread, receiver created without a constructor holding the current task (a DeleteResults by its constructor) and a task thread: legacy returned, modern threw PlatformNotSupportedException from Thread.Abort; control W12-ctl (no thread) returned on both; reachable: the constructor and a running operation (RunAction) set both fields, and Abort from another thread then aborts a live thread, which .NET Framework requests asynchronously and returns from, as here |
| Duplicati | W13 | `Duplicati.Library.Compression.FileArchiveDirectory::GetLastWriteTime(string)` | model | `System.IO.File::GetLastWriteTime(string)` | not-constructible | confirmed |  | row witness (the model's inputs are uninterpreted sort elements no probe can build; replay not-constructible): probe W13-bar, receiver by its constructor, a file name holding a character .NET Framework rejects: legacy threw ArgumentException from Path.Combine, modern returned a DateTime (one side throwing); control W13-ctl returned on both (legacy ArgumentException on an ordinary path rests on .NET Framework's own character check) |
| Duplicati | W14 | `Duplicati.Library.Encryption.EncryptionBase::Decrypt(string,string)` | model | `System.IO.File::OpenRead(string)` | not-constructible | confirmed |  | row witness (the model's inputs are uninterpreted sort elements no probe can build; replay not-constructible): probe W14-bar, receiver AESEncryption by its constructor (no override of the member), an input file name holding a character .NET Framework rejects: legacy threw ArgumentException from File.OpenRead, modern threw IOException; control W14-ctl FileNotFoundException on both (legacy ArgumentException on an ordinary path rests on .NET Framework's own character check) |
| Duplicati | U00 | `Duplicati.Library.Snapshots.Program::Main(string[])` | none (timeout) | `System.IO.Directory::CreateDirectory(string)` | none | undetermined |  | the difference is only in the console output: Main is void and catches every exception (the CreateDirectory/Path.Combine failure differs only in the exception written), so no outcome differs |
| Duplicati | U01 | `Duplicati.Server.Runner::Run(Duplicati.Server.Runner.IRunnerData,bool)` | none (timeout) | `System.String::Equals(string,string,System.StringComparison)` | none | false positive | P2-263 | File.Delete is guarded by File.Exists, which returns false for a path holding a character .NET Framework rejects on both runtimes, so the delete is never reached with one; the String.Equals calls use ClientFilenameStringComparison or OrdinalIgnoreCase, ordinal comparisons the ICU change does not reach |
| Duplicati | U02 | `Duplicati.Library.Main.Operation.Backup.SpillCollectorProcess::Run(Duplicati.Library.Main.Options,Duplicati.Library.Main.Operation.Backup.BackupDatabase,Duplicati.Library.Main.Operation.Common.ITaskReader)` | none (abstraction) | `System.IO.File::Delete(string)` | none | false positive | P2-261 | File.Delete's path is the spilled block volume's local file, a TempFile the volume writer created on disk (in the system temp folder or the asynchronous-upload folder), so it cannot hold a character .NET Framework rejects |
| Duplicati | U03 | `Duplicati.Library.Utility.ThrottledStream::DelayIfRequired(ref long,int,ref System.DateTime,ref long,ref double)` | none (abstraction) | `System.Math::Min(double,double)` | none | false positive | P2-262 | Math.Min is reached only when the target delay exceeds a threshold, so its operands are a positive finite TimeSpan's milliseconds and the positive constant; no -0, +0 pair or NaN can occur, and the result is cast to int |
| Duplicati | U04 | `Duplicati.Library.AutoUpdater.UpdaterManager::.cctor()` | none (timeout) | `System.IO.Path::Combine(string,string)` | none | undetermined |  | not safe to run: the static constructor probes and writes into the user's and machine's application-data folders; the witness also needs the AUTOUPDATER install-root environment variable (depends on the host's settings) |
| Duplicati | U05 | `Duplicati.Library.SQLiteHelper.PInvoke::LoadLibraryEx(string,System.IntPtr,uint)` | none (opaque) | `extern` | none | false positive | P2-214 | the extern's marshalling is the same on both runtimes: one string as an ANSI LPStr with the same A-suffix lookup in kernel32 and blittable IntPtr/uint, so the native call and its result are identical (probe U05-miss refused: the probe does not call an extern member) |
| Duplicati | U06 | `Duplicati.Server.SpecialFolders::TryAdd(System.Collections.Generic.List<global::Duplicati.Server.Serializable.TreeNode>,string,string,string)` | none (abstraction) | `System.IO.Path::IsPathRooted(string)` | none | false positive | P2-223 | a folder holding a character .NET Framework rejects makes legacy IsPathRooted throw ArgumentException, which the catch-all swallows, while modern then fails Directory.Exists; neither side adds a node, and the member is void |
| Duplicati | U07 | `Duplicati.Library.Main.Operation.Backup.BackendUploader::DoPut(Duplicati.Library.Main.Operation.Common.BackendHandler.FileEntryItem,Duplicati.Library.Interface.IBackend,System.Threading.CancellationToken)` | none (timeout) | `System.IO.File::OpenRead(string)` | none | false positive | P2-261 | File.OpenRead's path is the upload item's local file, a TempFile the volume writer or the Encrypt step created, which UpdateHashAndSize already opened before DoPut, so it cannot hold a character .NET Framework rejects |
| Duplicati | U08 | `Duplicati.UnitTest.RunScriptTests::RunScriptParsedResult(int)` | none (timeout) | `System.IO.File::ReadAllLines(string)` | none | undetermined |  | not safe to run: a unit test that runs a backup with run-script-before/after, starting processes |
| Duplicati | U09 | `Duplicati.Library.Common.IO.SystemIOLinux::NormalizePath(string)` | none (abstraction) | `System.IO.Path::GetFullPath(string)` | none | confirmed |  | row witness: probe U09-bar, a path holding a character .NET Framework rejects: legacy threw ArgumentException from Path.GetFullPath, modern returned the full path; control U09-ctl (an absolute valid path) returned the same string on both (legacy ArgumentException on an ordinary path rests on .NET Framework's own character check); SystemIOLinux/NoSnapshotLinux is selected on POSIX hosts only, so this is confirmed on the pair's detected runtimes only (P2-237's decision) |
| Duplicati | U10 | `Duplicati.Library.Snapshots.HyperVUtility::.ctor()` | none (timeout) | `System.Environment::get_OSVersion(` | none | false positive | P2-264 | the only use is Major > 6 or (Major 6 and Minor >= 2); .NET Framework's version capped at 6.2 and the real version give the same answer to that test on every Windows, so the WMI namespace chosen is the same |
| Duplicati | U11 | `Duplicati.Library.Backend.Tardigrade.Tardigrade::LoadLibrary(string)` | none (opaque) | `extern` | none | false positive | P2-214 | the extern's marshalling is the same on both runtimes: one string as an ANSI LPStr with the same A-suffix lookup in kernel32 and an IntPtr result, so the native call and its result are identical (probe U11-miss refused: the probe does not call an extern member) |
| Duplicati | U12 | `Duplicati.Server.WebServer.Server::CreateServer(System.Collections.Generic.IDictionary<string, string>)` | none (timeout) | `System.IO.Path::Combine(string,string)` | none | undetermined |  | not safe to run: reading UpdaterManager.InstalledBaseDir runs its static constructor, which writes into the host's application-data folders; the witness also needs a webroot option in an IDictionary argument the probe cannot build |
| Duplicati | U13 | `Duplicati.Library.Backend.File::GetDrive()` | none (abstraction) | `System.Char::IsLetter(` | none | false positive | P2-265 | Char.IsLetter answers the same for every Latin-1 character on both runtimes (the reclassified ordinal indicators stay letters), and for any root not starting with an ASCII letter DriveInfo throws ArgumentException, which is caught, so taking or skipping the branch both return null |
| Duplicati | U14 | `Duplicati.Server.Scheduler::Runner()` | none (timeout) | `System.Math::Min(double,double)` | none | false positive | P2-262 | Math.Min's operands are a TimeSpan's milliseconds checked non-negative just before and the positive constant, so no -0, +0 pair or NaN can occur, and the result is cast to int |
| OpenRA | W00 | `OpenRA.Network.ReplayRecorder::StartSavingReplay(byte[])` | model | `System.IO.Path::Combine(string,string,string,string)` | not-constructible | undetermined |  | not callable to the path: the static Game.ModData a probe cannot set is read before Path.Combine, and the body writes under the user's support dir |
| OpenRA | W01 | `OpenRA.Mods.Common.Widgets.ProductionTabsWidget::Scroll(int)` | model | `System.Math::Min(float,float)` | not-constructible | false positive | P2-262 | listOffset starts at +0 and is only ever a sum (never -0 from +0) and the other Max operand is an int converted to float, so Math.Min/Max never see the -0/+0 pair where the runtimes differ (NaN orders the same); also a void member whose difference would only be the field |
| OpenRA | W02 | `OpenRA.FileSystem.Folder::GetStream(string)` | model | `System.IO.File::OpenRead(string)` | not-constructible | undetermined |  | the result is a Stream, not comparable; an invalid-character path returns null on both (catch-all), but a colon/stream path .NET Framework rejects could open a stream on .NET 5 only |
| OpenRA | W03 | `OpenRA.Platforms.Default.OpenAlSoundEngine::QueryDevices(string,int)` | model | `System.Text.Encoding::GetString(` | not-constructible | undetermined |  | depends on the host's native OpenAL library and its device names; no ill-formed UTF-8 can be supplied to the native list |
| OpenRA | W04 | `OpenRA.Mods.Common.Widgets.Logic.MainMenuLogic::LoadAndDisplayNews(OpenRA.Mods.Common.WebServices,OpenRA.Widgets.Widget)` | model | `System.IO.Path::Combine(string,string)` | not-constructible | undetermined |  | not safe to run: starts a network download; also reads the static Game.Settings a probe cannot set |
| OpenRA | U00 | `OpenRA.Network.ReplayConnection::.ctor(string)` | none (timeout) | `System.IO.File::OpenRead(string)` | none | false positive | P2-223 | ReplayMetadata.Read opens the same path with a FileStream first, under the same runtime validation, and its failure (caught, null) ends the constructor the same way on both sides before File.OpenRead; probes threw the same exception type on both |
| OpenRA | U01 | `OpenRA.MapPreview::UpdateRemoteSearch(OpenRA.MapStatus,OpenRA.MiniYaml,System.Action<global::OpenRA.MapPreview>)` | none (timeout) | `System.Text.Encoding::GetString(` | none | undetermined |  | the difference is only in a field written by a void member (innerData.Players); needs ModData and the map cache |
| OpenRA | U02 | `OpenRA.Mods.Common.Widgets.Logic.ReplayBrowserLogic::.ctor(OpenRA.Widgets.Widget,OpenRA.ModData,System.Action,System.Action)` | none (timeout) | `System.IO.Path::Combine(string,string,string,string)` | none | undetermined |  | not safe to run: lists replays under the user's support dir; needs a loaded widget tree and ModData |
| OpenRA | U03 | `OpenRA.Map::ComputeUID(OpenRA.FileSystem.IReadOnlyPackage)` | none (timeout) | `System.String::EndsWith(` | none | confirmed |  | probe U03-a/U03-b, a package entry name ending in a character ICU ignores and NLS does not (a C0 control, U+200B) under the culture-sensitive EndsWith: legacy and modern returned different hashes (modern hashes the entry); control U03-ctl equal |
| OpenRA | U04 | `OpenRA.Mods.Common.Graphics.UIModelRenderable.FinalizedUIModelRenderable::Screen3DBounds(OpenRA.Graphics.WorldRenderer)` | none (opaque) | `System.Math::Min(float,float)` | none | false positive | P2-262 | minZ starts at float.MaxValue and every candidate is pxPos.Z + screen[2] where pxPos.Z is +0 or nonzero (an int2 converted with Z +0 plus a scaled int), so the sum is never -0 and Math.Min/Max never see the -0/+0 pair (NaN orders the same) |
| OpenRA | U05 | `OpenRA.Mods.Common.Widgets.Logic.ServerListLogic::GetStateLabel(OpenRA.Network.GameServer)` | none (abstraction) | `System.Math::Ceiling(double)` | none | false positive | P2-262 | the PlayTime > 0 guard keeps Math.Ceiling's argument positive, so it never returns the signed zero the row names; probes returned equal labels |
| OpenRA | U06 | `OpenRA.Mods.Common.Widgets.Logic.GameSaveBrowserLogic::.ctor(OpenRA.Widgets.Widget,OpenRA.ModData,System.Action,System.Action,bool,OpenRA.World)` | none (timeout) | `System.IO.Path::Combine(string,string,string,string)` | none | undetermined |  | not safe to run: lists and renames saves under the user's support dir; needs a loaded widget tree and ModData |
| OpenRA | U07 | `OpenRA.Mods.Common.SpriteLoaders.PngSheetLoader::TryParseSprite(System.IO.Stream,out OpenRA.Graphics.ISpriteFrame[],out OpenRA.Primitives.TypeDictionary)` | none (timeout) | `System.String::StartsWith(` | none | undetermined |  | not callable: frames and metadata are out parameters, which a driver cannot pass by reference |
| OpenRA | U08 | `OpenRA.TerrainTemplateInfo::LoadTileInfo(OpenRA.TileSet,OpenRA.MiniYaml)` | none (abstraction) | `System.Reflection.FieldInfo::SetValue(` | none | false positive | P2-266 | every FieldInfo.SetValue targets an instance field of TerrainTileInfo (which has no static fields); the row applies only to static readonly fields |
| OpenRA | U09 | `OpenRA.Platform::InitializeSupportDir()` | none (timeout) | `System.Environment::GetFolderPath(System.Environment.SpecialFolder)` | none | undetermined |  | a void member that writes only static fields, and the difference needs a special folder absent on the host |
| OpenRA | U10 | `OpenRA.Mods.Cnc.UtilityCommands.ImportTiberianDawnLegacyMapCommand::ReadPacks(OpenRA.Mods.Common.FileFormats.IniFile,string)` | none (abstraction) | `System.IO.File::OpenRead(string)` | none | confirmed |  | probe U10-a/U10-b, a path holding a character .NET Framework rejects: legacy threw ArgumentException, modern threw IOException; control U10-ctl threw FileNotFoundException on both |
| OpenRA | U11 | `OpenRA.Traits.ScreenShaker::GetScrollOffset()` | none (abstraction) | `System.Math::Sin(double)` | none | undetermined |  | receiver needs a List<ShakeEffect> and a WorldRenderer, which a probe cannot build (Math.Sin/Cos of these angles do differ in low bits between the runtimes, measured outside the member) |
| OpenRA | U12 | `OpenRA.Platforms.Default.FreeType::FT_Done_Face(System.IntPtr)` | none (opaque) | `extern` | none | undetermined |  | extern member: a driver never calls native code |
| OpenRA | U13 | `OpenRA.Platforms.Default.Sdl2PlatformWindow::SetProcessDPIAware()` | none (opaque) | `extern` | none | undetermined |  | extern member: a driver never calls native code (and it changes the process DPI setting) |
| OpenRA | U14 | `OpenRA.Platform::OverrideEngineDir(string)` | none (abstraction) | `System.IO.Path::IsPathRooted(string)` | none | confirmed |  | probe U14-a/U14-b, a path holding a character .NET Framework rejects (a pipe, a NUL): legacy threw ArgumentException from IsPathRooted, modern threw DirectoryNotFoundException; control U14-ctl equal |
