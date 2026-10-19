# P2-250: framework `static readonly` fields on `gitextensions-8522` (2026-10-19)

`gitextensions-8522` in `full` mode with `--execute`, compare mode quick (ADR 0052), `--jobs 4`, twice from one Release
build of branch `P2-250-framework-static-fields` at `75effa8c`, on the checkouts and restores P2-237 made:

- **P2-250**: the branch as it is. Wall-clock 337 s, exit 1.
- **Baseline**: the same build with `FrameworkStatics.IsFrozen` made false, so no field is frozen and none has a known
  value, which is `main` (`75258e60`) as far as this ticket goes. Wall-clock 329 s, exit 1.

The baseline is there because P2-245's run (`docs/runs/2026-10-19-execution-reach.md`, equiv `bceeea62`) predates P2-253
and P2-254, which `main` has since merged: on this host, which has no `Microsoft.WindowsDesktop.App` 5.0, P2-253 makes
every replay of a Windows Desktop project `not-constructible` ("runtime net5.0-windows not installed", 45 replays,
among them P2-245's five `reproduced` EQ006 on `AppSettings` and `GetHashCode`, which are EQ007 again by ADR 0060
decision 4). Against P2-245's run those changes and this ticket's would be mixed; against the baseline only this
ticket's are left. The census is the same in all three runs (13,592 matched pairs, 12,755 congruent).

## Outcome in one line

**No replay reason names a framework static field any more (9 in the baseline), and two Unknowns become confirmed
Divergents. The four results the ticket was filed for stay EQ002, correctly: they are real behaviour changes, not
false positives, so P2-245's migration Divergent precision is 55.8% (43 of 77), not 50.6%.**

## Results

| Rule | P2-245 (`bceeea62`) | Baseline | P2-250 |
|---|---|---|---|
| EQ001 | 12,910 | 12,910 | 12,910 |
| EQ002 | 24 | 24 | 26 |
| EQ003 | 225 | 225 | 223 |
| EQ004 | 15 | 15 | 15 |
| EQ005 | 192 | 192 | 192 |
| EQ006 | 17 | 12 | 12 |
| EQ007 | 435 | 440 | 440 |

Every result whose rule, Unknown reason or witness changed between the baseline and P2-250:

| Procedure | Baseline | P2-250 | Adjudication |
|---|---|---|---|
| `GitUI.CommandsDialogs.FormCleanupRepository::AddPath_Click(object,System.EventArgs)` | EQ003 `timeout` | EQ002 | confirmed by hand trace: legacy opens one dialog type and keeps every selected folder that passes the filter, modern opens another type and takes its one selected path; the calls differ |
| `GitUI.CommitInfo.RefsFormatter::FilterAndFormatBranches(System.Collections.Generic.IEnumerable<string>,bool,bool)` | EQ003 `abstraction` | EQ002 | confirmed by hand trace: modern replaces a null branch with `String.Empty` before formatting it, legacy formats the null, so the link factory or the HTML encoder gets another argument. Before P2-250 `String.Empty` was a free input, which the solver could not tell apart from null |
| `GitCommands.GitRefName::GetRemoteName(string,System.Collections.Generic.IEnumerable<string>)` | EQ007 unwitnessed (`abstraction`) | EQ007 witnessed | a runtime exposure with a model now; replay `not-constructible` (the model constrains a solution static field) |
| `GitExtensions.Plugins.Gource.GourcePlugin::UnZipFiles(string,string,bool)` | EQ007 unwitnessed (`opaque`) | EQ007 unwitnessed (`abstraction`) | the reason only |
| `GitUI.CommandsDialogs.FormCommit::CommitMessageToolStripMenuItemDropDownOpening(object,System.EventArgs)` | EQ003 `timeout` | EQ003 `abstraction` | the reason only |

Neither new EQ002 is in P2-212's sample, so neither enters the precision below.

## The four results the ticket was filed for (criterion 2)

| Row (P2-212) | Procedure | P2-250 | Replay reason, baseline → P2-250 |
|---|---|---|---|
| 0 | `EasyHook.HookAccessControl::SetInclusiveACL(int[])` | EQ002 | the model constrains `field.System.IntPtr.Zero` → no `System.IntPtr` can be built for the receiver's handle field (P2-256) |
| 3 | `EasyHook.LocalHook::IsThreadIntercepted(int)` | EQ002 | the divergence is in the call trace → the model constrains `typeof.EasyHook.LocalHook` |
| 7 | `EasyHook.HookAccessControl::SetExclusiveACL(int[])` | EQ002 | the model constrains `field.System.IntPtr.Zero` → no `System.IntPtr` can be built for the receiver's handle field (P2-256) |
| 18 | `EasyHook.LocalHook::get_HookBypassAddress()` | EQ002 | the model constrains `field.System.IntPtr.Zero`, `typeof.EasyHook.LocalHook` → the model constrains `typeof.EasyHook.LocalHook` |

They stay EQ002 because they are real. P2-245 matched them against the `Externals/EasyHook` project, whose two copies
are identical, but that project is not in the modern solution: the modern `EasyHook.HookAccessControl` and
`EasyHook.LocalHook` are GitUI's copy (`GitUI/Theming/LocalHook.cs`), which puts every native call these four members
make under a conditional compilation symbol, `SUPPORT_THEMES`, that no project, props or targets file in the modern
checkout defines. So legacy makes the P/Invoke and modern does not: the two ACL setters change no native ACL,
`HookBypassAddress` returns `IntPtr.Zero` without asking, and `IsThreadIntercepted` returns `false`. P2-245's `probe` of
rows 0 and 7 saw exactly this (legacy threw `DllNotFoundException`, modern returned), and read it as a deployment
difference. The ticket's criterion 2 is corrected to say so (its `Deviation:` note).

## Replay reasons naming a framework static field

The baseline has nine (P2-245's run had the same nine), P2-250 none. Each one's reason in P2-250:

| Procedure | Rule | Baseline names | P2-250's reason |
|---|---|---|---|
| `EasyHook.HookAccessControl::SetInclusiveACL(int[])` | EQ002 | `IntPtr.Zero` | no `System.IntPtr` argument can be built (P2-256) |
| `EasyHook.HookAccessControl::SetExclusiveACL(int[])` | EQ002 | `IntPtr.Zero` | no `System.IntPtr` argument can be built (P2-256) |
| `EasyHook.LocalHook::get_HookBypassAddress()` | EQ002 | `IntPtr.Zero`, `typeof.EasyHook.LocalHook` | the model constrains `typeof.EasyHook.LocalHook` |
| `EasyHook.LocalHook::GetProcAddress(string,string)` | EQ002 | `IntPtr.Zero` | the divergence is in the call trace |
| `GitCommands.GitRefName::GetRemoteName(string)` | EQ007 | `String.Empty` and a solution static | the model constrains the solution static only |
| `GitCommands.PathUtil::GetExtension(string)` | EQ007 | `String.Empty` | runtime net5.0-windows not installed (P2-253, this host) |
| `CommonTestUtils.GitModuleTestHelper::CreateFile(string,string,string)` | EQ007 | `String.Empty` | runtime net5.0-windows not installed (P2-253, this host) |
| `TranslationApp.TranslationHelpers::SaveTranslation(...)` | EQ007 | `String.Empty` | an `IDictionary` argument outside the solution with no public parameterless constructor |
| `GitExtensions.Plugins.GitStatistics.PieChart.PieChart3D::GetSliceDisplacement(float,float)` | EQ007 | `SizeF.Empty` | runtime net5.0-windows not installed (P2-253, this host) |

None is `reproduced` or `not-reproduced` yet: each now stops at the next obstacle, three of them this host's missing
desktop runtime, which installing would change the machine for (ADR 0040 decision 3 substitutes none).

## P2-245's precision, corrected

P2-245 counted rows 0, 3, 7 and 18 as false positives. They are confirmed, so its Precision table becomes:

| Pair | Rule | P2-245 | Corrected |
|---|---|---|---|
| gitextensions-8522 | EQ002 | 13.3% (2 of 15) | 40.0% (6 of 15) |
| | **all** | **35.5% (11 of 31)** | **48.4% (15 of 31)** |
| **Migrations** | EQ002 | 8.7% (2 of 23) | 26.1% (6 of 23) |
| | **all** | **50.6% (39 of 77)** | **55.8% (43 of 77)** |

The ceiling, with every undetermined result counted real, is 71.4% (85 of 119), from 68.1%. Duplicati, OpenRA and the
EQ006 rows are unchanged. The scoreboard is not changed here, for P2-245's reason: its migration row is P2-237's
measurement, and P2-247 is the re-adjudication that refreshes it.

## Findings and tickets

- P2-256 (new): replay builds no `IntPtr` from its sort element, so rows 0 and 7 stop there now that
  `IntPtr.Zero` is element 0.
- P2-245's adjudication of rows 0, 3, 7 and 18: corrected here, no ticket.
- 45 replays are `not-constructible` on this host for want of `Microsoft.WindowsDesktop.App` 5.0: P2-253's intended
  behaviour, no ticket.

## Method notes

- This worktree's `.corpus` holds only the pair's `pair.json` (copied from P2-245's) and the sentinel MSBuild files;
  the solutions are P2-237's checkouts and `TargetFrameworkRootPath` is P2-245's `refasm`.
- The baseline was built and run with the change switched off by hand and the source restored before anything was
  committed; it is not a commit.
