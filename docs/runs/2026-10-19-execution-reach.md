# P2-245: execution reach on the three migration pairs, P2-237's 51 uncallable results re-adjudicated (2026-10-19)

`gitextensions-8522`, `duplicati-3124` and `openra-17989`, each run in `full` mode with `--execute`, compare mode quick
(ADR 0052), `--jobs 4`, at equiv `bceeea62` (branch `P2-245-drivers-hidden-members-receivers`, PR #551), one after the
other from one Release build, on the checkouts and restores P2-237 made, with the committed pair configs for Duplicati
and OpenRA. Per-pair detail is in `docs/runs/2026-10-19-full-execute-<slug>/SUMMARY.md`. ADR 0062 is what changed:
replay and `probe` drivers reach hidden members, build receivers and objects from the model's fields, and build
`CancellationToken`, array, delegate, object and struct arguments; each input says how it was built
(`properties.replayInputs`).

Every number here stands next to P2-237's (`docs/runs/2026-10-17-precision-execute-verdict.md`, equiv `de93b9c7`).
This file adjudicates the 51 results P2-237 left undetermined because no driver could call them (41) or build their
argument (10); P2-237's other 20 undetermined, its 17 confirmed and its 31 false positives keep its classification.
ADR 0060 (P2-238) reports an unedited body a runtime change reaches as EQ007 and makes it EQ006 again when its replay
is `reproduced` (decision 4), so an EQ006 of P2-237's is the same result whether it is EQ006 or EQ007 here.

**These pairs take no part in ADR 0028's rule table here.** This file measures and adjudicates; it has no continue,
re-scope or stop line.

## Outcome in one line

**ADR 0062's reach makes 37 of P2-237's 41 uncallable results and 9 of its 10 unbuilt-argument results callable.
Re-adjudicated, the 51 give 22 confirmed, 7 false positives and 22 still undetermined, so migration Divergent
precision is 50.6% (39 of 77) over P2-237's 48 with these 29 added, from 35.4%, and the ceiling 68.1% (81 of 119),
from 73.9%.** Replay is `not-constructible` 256 times, from 277; "not public" and "no public parameterless
constructor" are gone, and two EQ002 replays reproduce, from none. Six `reproduced` replays come from the harness,
not the runtime (the two drivers' working directories, P2-254; a missing Windows Desktop runtime, P2-253), none in
P2-212's sample. No replay is `not-reproduced`.

## Finding 1: replay (criterion 7)

Every EQ002, EQ006 and witnessed EQ007 carries `properties.replay`; an unwitnessed EQ007 has no model and no replay.

| Pair | EQ002 | EQ002 replay | EQ006 | EQ006 replay | Witnessed EQ007 | EQ007 replay |
|---|---|---|---|---|---|---|
| gitextensions-8522 | 24 | `not-constructible` 24 | 18 | `reproduced` 8, `not-constructible` 10 | 103 | `not-applicable` 10, `not-constructible` 93 |
| duplicati-3124 | 13 | `reproduced` 2, `not-constructible` 11 | 24 | `reproduced` 10, `not-constructible` 14 | 81 | `not-applicable` 8, `not-constructible` 73 |
| openra-17989 | 0 | | 2 | `reproduced` 1, `not-applicable` 1 | 32 | `not-applicable` 1, `not-constructible` 31 |
| **All three** | **37** | `reproduced` 2, `not-constructible` 35 | **44** | `reproduced` 19, `not-applicable` 1, `not-constructible` 24 | **216** | `not-applicable` 19, `not-constructible` 197 |

Against P2-237's Replay table: its 260 EQ006 are the 44 EQ006 and 216 witnessed EQ007 here (ADR 0060 renamed the 235
on `runtime-rule` pairs, and the 19 of them whose replay is `reproduced` are EQ006 again). Over the 297, `reproduced` 21
from 10, `not-applicable` 20 from 10, `not-constructible` 256 from 277. EQ002 replays confirm two results, from none.
No replay is `not-reproduced`, so the soundness alarm (ADR 0035 decision 2) files nothing.

Why a replay is not constructible, over the 256, next to P2-237's 277:

| Reason | P2-237 | Here | |
|---|---|---|---|
| the divergence is only in the call trace, which a driver does not observe | 115 | 113 | |
| the method is not public | 59 | 0 | ADR 0062 decision 1 |
| the model constrains a field, a cast or an array | 49 | 0 | decision 2: every instance field, array, length, cast and type test is built |
| the model constrains a static or undeclared field | (in the 49) | 25 | not ADR 0062's: 11 framework statics (`String.Empty` 5, `IntPtr.Zero` 4, `SizeF.Empty` 1, `Registry.LocalMachine` 1: P2-250), 14 solution statics or a field one side lacks |
| the model constrains `typeof`, `thrown` or `caller` only | 0 | 7 | not ADR 0062's: maps a driver does not set |
| the receiver type has no public parameterless constructor | 27 | 0 | decision 2 |
| a framework or third-party object with no public parameterless constructor, or with model fields | 0 | 13 | left out by decision 2 (`Encoding` 2, `Stream` 2, `FileInfo`, `CultureInfo`, `PropertyInfo`, `MatchCollection`, `ManualResetEvent`, `IFileSystem`, `ExCSS.StyleRule`, `CloudBlobContainer`, `ClientContext`) |
| an abstract, interface or generic type no model type names | 0 | 10 | left out by decision 2 and the ticket's Out of scope |
| the call traces split before the outcome | 9 | 52 | reached now that the call is built: the model's outcomes rest on call answers chosen after the traces split |
| a side gave no comparable outcome | 7 | 16 | reached now: `DateTime` 4, `TempFile` 2, `ConfigFileSettings` 2, `FileStream`, `Stream`, `RSACryptoServiceProvider`, `SynchronizedCollection<>` (not comparable); `FileNotFoundException` 3 (no `Microsoft.WindowsDesktop.App`: P2-253), `TypeInitializationException` 1 |
| a window or the desktop session | 0 | 7 | ADR 0035's 2026-09-29 clarification, kept by decision 3 |
| an argument the driver cannot build | 5 | 6 | five enums (P2-251) and one `System.Drawing.Color`; P2-237's delegate and object of the five are built now |
| both sides threw where the model does not | 2 | 4 | |
| not a method, a property getter or a constructor | 3 | 2 | the two left are static constructors |
| a by-reference parameter | 1 | 1 | the ticket's Out of scope |

"Not public" (59) and "no public parameterless constructor" (27) are gone; what stood behind them is now either a
replay that runs or one of the reasons ADR 0062 leaves out, each counted above. The instance-field, array and cast part
of "the model constrains" is gone; what is left names a static or undeclared field, `typeof`, `thrown` or `caller`.
"An argument the driver cannot build" keeps the three enums P2-237 had and gains two enums and a framework struct its
earlier obstacles hid: an enum is a sort element in the model, which replay never built (ADR 0062 says "as replay
builds it today"), so P2-251 (new) builds it.

The two EQ002 replays that are now constructible are both `reproduced`, on Duplicati:
`SystemIOWindows::SetAccessControlFile(string,FileSecurity)` and `::SetAccessControlDir(string,DirectorySecurity)`, the
receiver by `SystemIOWindows()`, the security object by its public parameterless constructor, the path as the model
gives it. The two runtimes' outcomes differ on those inputs; the model has the legacy side throw from
`File.SetAccessControl`/`Directory.SetAccessControl` and the modern side return through
`FileSystemAclExtensions.SetAccessControl`. Neither is in P2-212's sample.

The `reproduced` replays with an `uninitialized` input (ADR 0062 decision 5), each with its reachability trace:

| Pair | Rule | Procedure | `uninitialized` input | Reached? | Trace |
|---|---|---|---|---|---|
| gitextensions-8522 | EQ006 | `GitUI.AutoCompletion.AutoCompleteWord::GetHashCode()` | `this`: `Word` | yes | `AutoCompleteWord(string word)` sets `Word = word` for any non-null string; `string.GetHashCode` is randomized per process on .NET 5 |
| gitextensions-8522 | EQ006 | `GitCommands.ExternalLinks.ExternalLink::GetHashCode()` | `this`: `Uri` | yes | `ExternalLink(string? caption, string uri)` sets `Uri = uri`; P2-212 sample row 33, confirmed by `probe` through that constructor |
| gitextensions-8522 | EQ006 | `Git.hub.Organization::GetHashCode()` | `this`: `Login` | yes | `Client` creates `new Organization { Login = organization }` from its caller's string |
| gitextensions-8522 | EQ006 | `Git.hub.User::GetHashCode()` | `this`: `Login` | yes | `RepositoryV2` creates a `User` with `Login = Owner` |
| duplicati-3124 | EQ006 | `Duplicati.Library.Snapshots.MSSQLDB::GetHashCode()` | `this`: `ID` | yes | `MSSQLDB(string Name, string ID, List<string> DataPaths)` sets `ID` directly |
| gitextensions-8522 | EQ006 | `GitCommands.GitModule::GetSubmoduleFullPath(string)` | `this`: `WorkingDir` | no | `GitModule(string? workingDir)` always normalizes the path and appends a separator, which the model's `s<n>` lacks; and the difference is not the runtime's: a `probe` with the replay's strings returns `<root>\cwd-<a>\s1\s2\` against `<root>\cwd-<b>\s1\s2\`, the two drivers' own working directories (P2-254, new) |

Five reach their state through a public constructor or the solution's own initializer, so their `reproduced` confirms the
Divergent (none but `ExternalLink` is in P2-212's sample). `GetSubmoduleFullPath` confirms nothing.

Checking those by `probe` found five more `reproduced` replays, without `uninitialized` inputs, that come from the
harness, not the runtime. `SystemIOLinux::PathGetFullPath(string)` and `Utility.Uri::UriBuilder(string,string,NameValueCollection)`
on Duplicati return the drivers' working directories (P2-254). `AppSettings::GetDictionaryDir()`,
`::GetGitExtensionsDirectory()` and `::ReadBoolRegKey(string,bool)` on Git Extensions are `reproduced` because the modern
`AppSettings` static constructor reads `System.Windows.Forms.Application`, which a replay driver without
`Microsoft.WindowsDesktop.App` cannot load, so the modern side throws `TypeInitializationException` (P2-253, new);
P2-237's two Git Extensions `reproduced` were the first two of these members. None is in P2-212's sample, so no adjudication
changes. The other `reproduced` replays checked (`SystemIOWindows::PathGetFullPath`, `::GetFileAttributes`,
`::PathGetFileName`) differ in a way that holds no working directory: a throw against a return, or a different file name.

## Finding 2: the 51 (criterion 6)

P2-237's method, unchanged: **confirmed** needs an executed oracle (a replay `reproduced`, or a `probe` run on both
runtimes whose outcomes differ for the claimed reason); **false positive** is a hand trace, with an execution beside it
where one runs, showing the claimed difference cannot happen on the pair's runtimes; anything else stays
**undetermined**, with its obstacle. A result is **callable** when a driver is built and the member called, by replay
with the model's inputs or by `probe` with inputs a trace chose; one found not safe to run on this host (a message loop,
a process, a live service, update folders outside the temp folder) is callable but not run, and says so. Each row was
matched to these runs by procedure identity and rule (an EQ006 that is EQ007 here is the same result).

- Decision: a difference on an `uninitialized` input confirms only with a hand trace that a constructor and calls of the
  solution reach that state (ADR 0062 decision 5); where `probe` could give the receiver by a constructor instead, it
  did, and the row says so. Duplicati row 24's difference is on a state no trace reaches (two threads never started),
  so it stays undetermined.
- Decision: Duplicati row 50 (`SnapshotBase::GetAttributes(string)`, abstract) is called on `NoSnapshotLinux`, the
  concrete type `BackupHandler` creates with its parameterless constructor; it has no instance fields, so its
  `uninitialized` instance is that constructor's state. Its legacy throw comes from `Path.GetFullPath` in the override
  it calls, the same invalid-path check as the claimed `File.GetAttributes` row, before that call is reached; it is
  counted confirmed.
- Decision: Git Extensions rows 22 and 41 are not callable: the modern driver cannot load a Windows Forms type, because
  a replay or `probe` driver runs on `Microsoft.NETCore.App` alone and this host has no `Microsoft.WindowsDesktop.App`
  5.0 (P2-253, new). Installing a runtime changes the machine, so it was not done here.
- The tracing and probes were done by three sub-agents, one per pair, under one written brief, in two rounds: the
  second, after this ticket's fixes for what the first found (a static member of a window type, hidden constructors as
  targets and makers, framework types by name in `probe`, structs by constructor), re-probed the rows those fixes made
  callable. Their tables were read in full and checked against `probe`'s output.

### Callable

| Group | Rows | Callable | Of them not run (unsafe) | Not callable, and why |
|---|---|---|---|---|
| Not callable in P2-237 | 41 | **37** (at least 36 needed) | 5: Git Extensions 14 (message loop), Duplicati 3 (live AWS), 29 and 51 (update folders), OpenRA 5 (network download) | Git Extensions 13 (`ExCSS.StyleRule`: ExCSS 2.0.6 and 4.1.0 share no constructor, so no one input exists for both sides, and a third-party type is never built by fields), 22 and 41 (no `Microsoft.WindowsDesktop.App` 5.0: P2-253), 45 (a window receiver, ADR 0035's desktop rule) |
| Argument not built in P2-237 | 10 | **9** (at least 7 needed) | 1: Duplicati 6 (spawns a process) | Git Extensions 16 (an `IWin32Window` argument, ADR 0035's desktop rule) |

No row is left for "private", "no public parameterless constructor" or an argument kind criterion 3 builds. Against
the ticket's expectations: Git Extensions 14 is callable (it is not run), Duplicati 50 is callable through its concrete
type, Duplicati 6 (`MethodInfo`, `null` here) and OpenRA 20 (`Stream` and `Encoding`, by `MemoryStream(byte[])` and
`UTF8Encoding()`) are callable; Git Extensions 13, 22 and 41 are the "at most two more" and one, for the reasons above.

### Re-adjudication

| Pair | Rule | Rows | Confirmed | False positive | Still undetermined |
|---|---|---|---|---|---|
| gitextensions-8522 | EQ002 | 12 | 0 | 6 | 6 |
| | EQ006 | 12 | 6 | 0 | 6 |
| duplicati-3124 | EQ002 | 5 | 0 | 1 | 4 |
| | EQ006 | 14 | 10 | 0 | 4 |
| openra-17989 | EQ006 | 8 | 6 | 0 | 2 |
| **All three** | **all** | **51** | **22** | **7** | **22** |

The 22 confirmed are each a difference at the outcome on the claimed runtime change: a path holding a character .NET
Framework rejects (16), ill-formed UTF-8 decoded to a different number of replacement characters (3: Git Extensions 23,
OpenRA 7 and 20), `string.GetHashCode` randomized per process on .NET 5 (Git Extensions 33), `Encoding.Default` being the
ANSI code page against UTF-8 (Git Extensions 44), `Thread.Abort` unsupported on .NET 5 (Duplicati 44). The 7 false
positives: Git Extensions 0, 3, 7 and 18, identical bodies whose solver difference rests on `IntPtr.Zero` read as a
free input on one side (P2-250, new; the `probe` difference on 0 and 7 is the native EasyHook DLL loading on one driver
host only, which confirms nothing); Git Extensions 19 (the added `CanRead` guard is unreachable for a stream the solution
closes), 20 (the reflected `System.Drawing` members exist on .NET Framework 4.8); Duplicati 22 (differs only on a
non-Windows host, where .NET Framework 4.8 does not run).

The 22 still undetermined, by obstacle: not callable 5 (above); callable but not safe to run 7 (the six above, and Git
Extensions 25, whose replay ran and failed to start `BugReporter.exe`, on inputs the replay says are not the model's;
its dialog path is not safe); a result no driver compares 4 (Git Extensions 11 `SynchronizedCollection`/`ConcurrentBag`,
12 `Assembly`, 51 `StreamReader`, Duplicati 0 `TrayIconBase`); an `async` result no driver awaits 2 (Duplicati 14 and 47:
P2-252, new); the host, not the solution, decides 2 (Git Extensions 40, an existing host file; 49, the host executable's
compatibility manifest); a difference in a field written by a `void` member 1 (OpenRA 4); a state no trace reaches 1
(Duplicati 24).

### Precision

Precision is confirmed / (confirmed + false positive), undetermined left out, over P2-237's 48 still-Divergent sampled
results (17 confirmed, 31 false positive) with these 29 decided results added; all 51 are still Divergent in these runs.

| Pair | Rule | P2-237, still Divergent | Here |
|---|---|---|---|
| gitextensions-8522 | EQ002 | 22.2% (2 of 9) | 13.3% (2 of 15) |
| | EQ006 | 30.0% (3 of 10) | 56.3% (9 of 16) |
| | **all** | **26.3% (5 of 19)** | **35.5% (11 of 31)** |
| duplicati-3124 | EQ002 | 0% (0 of 7) | 0% (0 of 8) |
| | EQ006 | 76.9% (10 of 13) | 87.0% (20 of 23) |
| | **all** | **50.0% (10 of 20)** | **64.5% (20 of 31)** |
| openra-17989 | EQ006 | **22.2% (2 of 9)** | **53.3% (8 of 15)** |
| **Migrations** | EQ002 | 12.5% (2 of 16) | 8.7% (2 of 23) |
| | EQ006 | 46.9% (15 of 32) | 68.5% (37 of 54) |
| | **all** | **35.4% (17 of 48)** | **50.6% (39 of 77)** |

If every result still undetermined were real (P2-237's other 20 and these 22, 42 in all), migration precision would be
at most 68.1% (81 of 119), against P2-237's 73.9% (88 of 119): the ceiling falls because 7 of the 51 are false
positives. EQ002's precision falls: six of the seven false positives are EQ002, and none of the 51's EQ002 is
confirmed; four of the six rest on one modelling gap (P2-250).

- Decision: the scoreboard is not changed here. Its migration row is P2-237's last column, a measurement over the
  results its runs reported; this file re-adjudicates part of the same sample on later runs, and P2-247 is the second
  execution re-adjudication that refreshes the scoreboard (ROADMAP). The 50.6% stands here beside the 35.4%.

### Every re-adjudicated result

| Pair | # | Group | Procedure | How it was called | Outcome | Now | Basis |
|---|---|---|---|---|---|---|---|
| Git Extensions | 0 | call | `EasyHook.HookAccessControl::SetInclusiveACL(int[])` | probe: legacy: this=uninitialized; InACL=value \| modern: this=uninitialized; InACL=value | false positive | EQ002 | bodies are identical on both sides and make the same EasyHook P/Invoke; the solver difference comes from IntPtr.Zero being modelled on one side only. Probe 0-a: legacy threw DllNotFoundException, modern returned, which is the native EasyHook DLL being found by one driver host only, not the code or the runtime |
| Git Extensions | 3 | call | `EasyHook.LocalHook::IsThreadIntercepted(int)` | probe: legacy: this=uninitialized; InThreadID=value \| modern: this=uninitialized; InThreadID=value | false positive | EQ002 | identical bodies; the claimed difference is only in the calls made (both return the same bool), from IntPtr.Zero modelled on one side; probe 3-a on the disposed state (Dispose resets m_Handle): both threw ObjectDisposedException |
| Git Extensions | 7 | call | `EasyHook.HookAccessControl::SetExclusiveACL(int[])` | probe: legacy: this=uninitialized; InACL=value \| modern: this=uninitialized; InACL=value | false positive | EQ002 | same as row 0 (identical bodies, same P/Invoke; IntPtr.Zero modelled on one side). Probe 7-a: legacy threw DllNotFoundException, modern returned, a native DLL deployment difference between driver hosts |
| Git Extensions | 11 | call | `GitUI.UserControls.RevisionGrid.Graph.RevisionGraphRevision::get_StartSegments()` | probe: legacy: this=constructor GitUI.UserControls.RevisionGrid.Graph.RevisionGraphRevision.RevisionGraphRevision(GitUIPluginInterfaces.ObjectId, int) \| modern: this=constructor GitUI.UserControls.RevisionGrid.Graph.RevisionGraphRevision.RevisionGraphRevision(GitUIPluginInterfaces.ObjectId, int) | undetermined (other: the result is not comparable (SynchronizedCollection against ConcurrentBag)) | EQ002 | probe 11-a (receiver by constructor): both sides NotComparable; the getter declared type changed from SynchronizedCollection to ConcurrentBag, and the driver cannot compare either collection |
| Git Extensions | 12 | call | `GitUIPluginInterfaces.ManagedExtensibility::TryLoadAssembly(System.IO.FileInfo)` | probe: legacy: file=constructor System.IO.FileInfo.FileInfo(string) \| modern: file=constructor System.IO.FileInfo.FileInfo(string) | undetermined (other: the result (Assembly) is not comparable; the probed assembly was found by name on legacy too) | EQ002 | probe 12-a: both returned an Assembly (NotComparable on both sides); the legacy null case needs an assembly outside the legacy probing path, and the result is not comparable anyway |
| Git Extensions | 13 | call | `GitUI.Theming.ThemeLoader::StyleRuleThemeException(ExCSS.StyleRule,string)` | not callable | undetermined (an argument kind: ExCSS.StyleRule, no constructor common to both sides) | EQ002 | ExCSS.StyleRule has no constructor signature shared by ExCSS 2.0.6 (ctor(), ctor(StyleDeclaration)) and 4.1.0 (ctor(StylesheetParser)), and a third-party type cannot be built by fields |
| Git Extensions | 14 | call | `BugReporter.Program::Main()` | not run (unsafe) | undetermined (unsafe: message loop) | EQ002 | runs a WinForms message loop (Application.Run) |
| Git Extensions | 16 | argument | `GitUI.OsShellUtil::PickFolder(System.Windows.Forms.IWin32Window,string)` | not callable | undetermined (window/desktop (IWin32Window argument); unsafe (dialog)) | EQ002 | the IWin32Window parameter is refused by the driver (needs a live window handle), and the body opens a folder dialog |
| Git Extensions | 18 | call | `EasyHook.LocalHook::get_HookBypassAddress()` | probe: legacy: this=uninitialized \| modern: this=uninitialized | false positive | EQ002 | identical bodies making the same P/Invoke; the differing returned IntPtr comes from IntPtr.Zero modelled on one side; probe 18-a on the disposed state: both threw ObjectDisposedException |
| Git Extensions | 19 | call | `CommonTestUtils.MockExecutable.MockProcess::Verify()` | probe: legacy: this=constructor CommonTestUtils.MockExecutable.MockProcess.MockProcess() \| modern: this=constructor CommonTestUtils.MockExecutable.MockProcess.MockProcess() | false positive | EQ002 | the added CanRead guard only matters for a closed stream still held by StandardInput; the solution closes input only through StreamWriter.Close, which on .NET Framework nulls BaseStream (legacy skips) and on .NET 5 leaves CanRead false (modern skips), so both pass; probe 19-b (receiver by the nested type constructor MockProcess()): both returned |
| Git Extensions | 20 | call | `GitUI.Theming.ThemeModule::ResetGdiCaches()` | probe: legacy:  \| modern: | false positive | EQ002 | probe 20-a: both returned; on .NET Framework 4.8 the reflected System.Drawing members exist with the expected types, so the InvalidCastException the model claims for legacy does not happen, and the modern body is compiled out (SUPPORT_THEMES undefined) |
| Git Extensions | 22 | call | `GitUI.CommitInfo.CommitInfo.BranchComparer::Compare(string,string)` | not callable | undetermined (other: modern driver cannot load the containing type (no WindowsDesktop 5.0 runtime on this host)) | EQ002 | probes 22-a and 22-b: legacy returned, modern NotConstructible FileNotFoundException; BranchComparer is nested in the CommitInfo UserControl and the host has no Microsoft.WindowsDesktop.App 5.0, so the modern driver cannot load System.Windows.Forms. By hand the difference is real (Comparer<string>.Default against StringComparer.Ordinal) |
| Git Extensions | 23 | call | `ConEmu.WinForms.AnsiStreamChunkEventArgs::GetText(System.Text.Encoding)` | probe: legacy: this=constructor ConEmu.WinForms.AnsiStreamChunkEventArgs.AnsiStreamChunkEventArgs(byte[]); encoding=constructor System.Text.UTF8Encoding.UTF8Encoding() \| modern: this=constructor ConEmu.WinForms.AnsiStreamChunkEventArgs.AnsiStreamChunkEventArgs(byte[]); encoding=constructor System.Text.UTF8Encoding.UTF8Encoding() | confirmed | EQ006 | probes 23-a, 23-c, 23-d (receiver by constructor, encoding by UTF8Encoding()): both returned, different strings (a different count of U+FFFD for ill-formed UTF-8); 23-b equal; receiver needs no trace |
| Git Extensions | 25 | argument | `GitUI.NBugReports.BugReportInvoker::Report(System.Exception,bool)` | replay: this=static; exception=uninitialized; isTerminating=value (replay) | undetermined (unsafe: process start or dialog) | EQ006 → EQ007 | this run's replay called it: both threw Win32Exception (Process.Start of BugReporter.exe, not found), on inputs the replay says are not the model's; the dialog path is not safe to run |
| Git Extensions | 33 | call | `GitCommands.ExternalLinks.ExternalLink::GetHashCode()` | probe: legacy: this=constructor GitCommands.ExternalLinks.ExternalLink.ExternalLink(string?, string) \| modern: this=constructor GitCommands.ExternalLinks.ExternalLink.ExternalLink(string?, string) | confirmed | EQ006 | probe 33-a (receiver by public constructor ExternalLink(string?, string)): both returned, different values (string.GetHashCode is randomized per process on .NET 5); the run's replay also reproduced on uninitialized ExternalLink.Uri, a state that constructor reaches |
| Git Extensions | 37 | call | `CommonTestUtils.GitModuleTestHelper::EnsureCreatedInTempFolder(string)` | probe: legacy: this=uninitialized GitModuleTestHelper.TemporaryPath; path=value \| modern: this=uninitialized GitModuleTestHelper.TemporaryPath; path=value | confirmed | EQ006 → EQ007 | probes 37-a and 37-b (receiver by fields TemporaryPath): legacy threw ArgumentException, modern returned (an ignorable NUL or zero-width space under ICU, not under NLS); trace: the GitModuleTestHelper(string) constructor sets TemporaryPath to Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()), which is the probed value when TEMP is C:\Temp, and public CreateFile passes its parentDir argument straight through |
| Git Extensions | 39 | call | `CommonTestUtils.GitModuleTestHelper::CreateRepoFile(string,string,string)` | probe: legacy: this=uninitialized GitModuleTestHelper.TemporaryPath, GitModule.WorkingDir, GitModuleTestHelper.Module; fileRelativePath=value; fileName=value; fileContent=value \| modern: this=uninitialized GitModuleTestHelper.TemporaryPath, GitModule.WorkingDir, GitModuleTestHelper.Module; fileRelativePath=value; fileName=value; fileContent=value | confirmed | EQ006 → EQ007 | probe 39-a (receiver by fields TemporaryPath, Module.WorkingDir): legacy threw ArgumentException (Path.IsPathRooted rejects '\|' on .NET Framework), modern threw IOException (directory creation fails later); trace: the constructor sets TemporaryPath under the host temp folder and Module = new GitModule(Path.Combine(TemporaryPath, repositoryName)), whose WorkingDir is that path with a trailing separator |
| Git Extensions | 40 | argument | `GitCommands.FileInfoExtensions::MakeFileTemporaryWritable(string,System.Action<string>)` | probe: legacy: fileName=value; writableAction=delegate \| modern: fileName=value; writableAction=delegate | undetermined (other: needs an existing host file) | EQ006 → EQ007 | probe 40-a (delegate argument): both returned; this run's replay (not-applicable): both threw NullReferenceException; a differing outcome needs an existing host file, and the fresh temp folder has none the probe can name |
| Git Extensions | 41 | call | `GitUI.CommandsDialogs.FormVerify::GuessFileTypeWithContent(string)` | not callable | undetermined (other: modern driver cannot load the containing Form type (no WindowsDesktop 5.0 runtime on this host)) | EQ006 | probes 41-c, 41-d, 41-e: the static member is now accepted; legacy returned, modern NotConstructible FileNotFoundException: FormVerify is a Form and the host has no Microsoft.WindowsDesktop.App 5.0 (as row 22, ticket P2-253). By hand ICU ignores the leading NUL or zero-width space, so modern would pick another file name |
| Git Extensions | 44 | call | `ConEmu.WinForms.AnsiStreamChunkEventArgs::GetMbcsText()` | probe: legacy: this=constructor ConEmu.WinForms.AnsiStreamChunkEventArgs.AnsiStreamChunkEventArgs(byte[]) \| modern: this=constructor ConEmu.WinForms.AnsiStreamChunkEventArgs.AnsiStreamChunkEventArgs(byte[]) | confirmed | EQ006 → EQ007 | probes 44-a and 44-b (receiver by constructor): both returned, different strings; Encoding.Default is the ANSI code page on .NET Framework and UTF-8 on .NET 5 |
| Git Extensions | 45 | call | `GitExtensions.Plugins.Gource.GourceStart::linkLabel1_LinkClicked(object,System.Windows.Forms.LinkLabelLinkClickedEventArgs)` | not callable | undetermined (window/desktop; unsafe (URL start)) | EQ006 → EQ007 | an instance member of the GourceStart Form (needs a window receiver) and it starts a URL |
| Git Extensions | 47 | call | `ICSharpCode.TextEditor.Document.FileSyntaxModeProvider::GetSyntaxModeFile(ICSharpCode.TextEditor.Document.SyntaxMode)` | probe: legacy: this=constructor ICSharpCode.TextEditor.Document.FileSyntaxModeProvider.FileSyntaxModeProvider(string); syntaxMode=constructor ICSharpCode.TextEditor.Document.SyntaxMode.SyntaxMode(string, string, string) \| modern: this=constructor ICSharpCode.TextEditor.Document.FileSyntaxModeProvider.FileSyntaxModeProvider(string); syntaxMode=constructor ICSharpCode.TextEditor.Document.SyntaxMode.SyntaxMode(string, string, string) | confirmed | EQ006 → EQ007 | probe 47-a (receiver and SyntaxMode by constructor): legacy threw ArgumentException (Path.Combine rejects '\|' on .NET Framework), modern threw HighlightingDefinitionInvalidException |
| Git Extensions | 49 | call | `GitUI.Interops.DwmApi.DwmApi::IsWindows10BuildOrGreater(int)` | probe: legacy: build=value \| modern: build=value | undetermined (other: outcome depends on the host executable's manifest, not a state the solution's own host reaches) | EQ006 → EQ007 | probe 49-a: legacy returned false, modern true, but only because the driver host has no Windows 10 compatibility manifest; GitExtensions.exe declares one, under which .NET Framework 4.8 reports the real OS version too |
| Git Extensions | 51 | call | `ICSharpCode.TextEditor.Util.FileReader::AutoDetect(System.IO.Stream,byte,byte,System.Text.Encoding)` | probe: legacy: fs=constructor System.IO.MemoryStream.MemoryStream(byte[]); firstByte=value; secondByte=value; defaultEncoding=constructor System.Text.UTF8Encoding.UTF8Encoding() \| modern: fs=constructor System.IO.MemoryStream.MemoryStream(byte[]); firstByte=value; secondByte=value; defaultEncoding=constructor System.Text.UTF8Encoding.UTF8Encoding() | undetermined (other: the result (StreamReader) is not comparable) | EQ006 → EQ007 | probe 51-a (MemoryStream and UTF8Encoding by constructor): both NotComparable (StreamReader); the claimed difference is the reader's encoding (Encoding.Default), which the driver does not compare |
| Duplicati | 0 | call | `Duplicati.GUI.TrayIcon.Program::GetWinformsInstance()` | probe: (no inputs: static, no parameters) | undetermined (other: the result is a TrayIconBase, which probe reports NotComparable on both sides) | EQ002 | probe 0-plain: both sides returned a TrayIconBase that probe does not compare; the difference is only the returned runner type (WinFormsRunner against Win32Runner), which no outcome observes |
| Duplicati | 3 | call | `Duplicati.Library.Backend.S3IAM::CreateUnprivilegedUser(string,string,string)` | not run (unsafe) | undetermined (unsafe: creates an AmazonIdentityManagementServiceClient and calls a live AWS IAM service) | EQ002 | not run: CreateUser, PutUserPolicy and CreateAccessKey go to a live AWS service before the outcomes split |
| Duplicati | 6 | argument | `Duplicati.Library.AutoUpdater.UpdaterManager::RunFromMostRecent(System.Reflection.MethodInfo,string[],Duplicati.Library.AutoUpdater.AutoUpdateStrategy)` | not run (unsafe) | undetermined (unsafe: RunFromMostRecentSpawn starts a process, and UpdaterManager's static constructor probes and writes update folders that may lie outside the temp folder) | EQ002 | not run: both sides spawn a process; the difference also needs the AUTOUPDATER_USE_APPDOMAIN environment variable set |
| Duplicati | 14 | call | `Duplicati.Library.Backend.AzureBlob.AzureBlobWrapper::AddFileStream(string,System.IO.Stream,System.Threading.CancellationToken)` | probe: this=uninitialized; keyName=value; source=value; cancelToken=value | undetermined (other: an async Task is NotComparable to probe, and a non-null _container (CloudBlobContainer) needs a network address (unsafe)) | EQ002 | probe 14-nullcontainer: both sides returned a Task probe does not await or compare; the claimed witness uploads to a live Azure service |
| Duplicati | 22 | call | `Duplicati.GUI.TrayIcon.Program::TryGetWinforms()` | probe: (no inputs: static, no parameters) | false positive | EQ002 | differs only when Platform.IsClientWindows is false, a non-Windows host, where .NET Framework 4.8 does not run; probe 22-plain: both returned true on Windows |
| Duplicati | 24 | call | `Duplicati.GUI.TrayIcon.HttpServerConnection::Close()` | probe: this=uninitialized HttpServerConnection.m_waitLock, HttpServerConnection.m_pollThread, HttpServerConnection.m_requestThread | undetermined (other: probe on an uninitialized state no trace reaches (the constructor calls the live server, then starts both threads)) | EQ006 → EQ007 | probe 24-fields: legacy threw ThreadStateException (Join on an unstarted thread), modern PlatformNotSupportedException (Thread.Abort); the unstarted-thread receiver is a state no trace reaches |
| Duplicati | 29 | call | `Duplicati.Library.AutoUpdater.UpdaterManager::GetBestUpdateVersion(bool)` | not run (unsafe) | undetermined (unsafe: UpdaterManager's static constructor probes and writes update folders that may lie outside the temp folder; the claimed input is the static readonly INSTALLDIR, which no driver sets) | EQ006 | not run; the return value also differs for another reason (the edited ApplicationBase key) |
| Duplicati | 30 | argument | `Duplicati.Library.Backend.Mega.MegaBackend::PutAsync(string,string,System.Threading.CancellationToken)` | probe: this=constructor Duplicati.Library.Backend.Mega.MegaBackend.MegaBackend(); remotename=value; filename=value; cancelToken=value | confirmed | EQ006 → EQ007 | probe 30-pipe: a filename holding a character .NET Framework rejects: legacy threw ArgumentException, modern IOException, from File.OpenRead before any network call |
| Duplicati | 32 | call | `Duplicati.Library.Compression.FileArchiveDirectory::CreateFile(string,Duplicati.Library.Interface.CompressionHint,System.DateTime)` | probe: this=constructor Duplicati.Library.Compression.FileArchiveDirectory.FileArchiveDirectory(string); file=value; hint=value; lastWrite=constructor System.DateTime.DateTime(long) | confirmed | EQ006 → EQ007 | probe 32-pipe-ctor: legacy threw ArgumentException (Path.Combine), modern IOException (File.Create); receiver by its constructor, DateTime by its constructor |
| Duplicati | 33 | call | `Duplicati.Library.Compression.FileArchiveDirectory::FileExists(string)` | probe: this=constructor Duplicati.Library.Compression.FileArchiveDirectory.FileArchiveDirectory(string); file=value | confirmed | EQ006 → EQ007 | probe 33-pipe: legacy threw ArgumentException (Path.Combine), modern returned false; receiver by its constructor |
| Duplicati | 34 | call | `Duplicati.Library.Compression.FileArchiveDirectory::ListFiles(string)` | probe: this=constructor Duplicati.Library.Compression.FileArchiveDirectory.FileArchiveDirectory(string); prefix=value | confirmed | EQ006 → EQ007 | probe 34-pipe: legacy threw ArgumentException (Path.Combine), modern returned an empty array; receiver by its constructor |
| Duplicati | 36 | argument | `Duplicati.Library.Backend.WEBDAV::PutAsync(string,string,System.Threading.CancellationToken)` | probe: this=constructor Duplicati.Library.Backend.WEBDAV.WEBDAV(); remotename=value; filename=value; cancelToken=value | confirmed | EQ006 → EQ007 | probe 36-pipe: a filename holding a character .NET Framework rejects: legacy threw ArgumentException, modern IOException, from File.OpenRead before any network call |
| Duplicati | 42 | call | `Duplicati.Library.Compression.FileArchiveDirectory::OpenRead(string)` | probe: this=constructor Duplicati.Library.Compression.FileArchiveDirectory.FileArchiveDirectory(string); file=value | confirmed | EQ006 → EQ007 | probe 42-pipe: legacy threw ArgumentException (Path.Combine), modern IOException (File.OpenRead); receiver by its constructor |
| Duplicati | 44 | call | `Duplicati.CommandLine.Commands.PeriodicOutput::Dispose()` | probe: this=constructor Duplicati.CommandLine.Commands.PeriodicOutput.PeriodicOutput(Duplicati.CommandLine.ConsoleOutput, System.TimeSpan) | confirmed | EQ006 → EQ007 | probes 44-ctor2 and 44-ctor3: legacy returned, modern threw PlatformNotSupportedException (Thread.Abort on the live writer thread); receiver by its constructor, which starts only a background thread blocked on an event (safe) |
| Duplicati | 45 | argument | `Duplicati.Library.Backend.Dropbox::PutAsync(string,string,System.Threading.CancellationToken)` | probe: this=constructor Duplicati.Library.Backend.Dropbox.Dropbox(); remotename=value; filename=value; cancelToken=value | confirmed | EQ006 → EQ007 | probe 45-pipe: a filename holding a character .NET Framework rejects: legacy threw ArgumentException, modern IOException, from File.OpenRead before any network call |
| Duplicati | 47 | call | `Duplicati.Library.Backend.SharePointBackend::uploadFileSlicePerSlice(Microsoft.SharePoint.Client.ClientContext,Microsoft.SharePoint.Client.Folder,System.IO.Stream,string,System.Threading.CancellationToken)` | probe: this=constructor Duplicati.Library.Backend.SharePointBackend.SharePointBackend(); ctx=value; folder=value; sourceFileStream=value; fileName=value; cancelToken=value | undetermined (other: an async Task<File> is NotComparable to probe, so the faulted task's exception is not observed) | EQ006 | probe 47-pipe: both sides returned a Task<File> that probe does not await; by hand trace legacy faults with ArgumentException at Path.GetFileName |
| Duplicati | 48 | argument | `Duplicati.Library.Backend.GoogleCloudStorage.GoogleCloudStorage::PutAsync(string,string,System.Threading.CancellationToken)` | probe: this=constructor Duplicati.Library.Backend.GoogleCloudStorage.GoogleCloudStorage.GoogleCloudStorage(); remotename=value; filename=value; cancelToken=value | confirmed | EQ006 → EQ007 | probe 48-pipe: a filename holding a character .NET Framework rejects: legacy threw ArgumentException, modern IOException, from File.OpenRead before any network call |
| Duplicati | 50 | call | `Duplicati.Library.Snapshots.SnapshotBase::GetAttributes(string)` | probe: this=uninitialized; localPath=value (NoSnapshotLinux, no instance fields) | confirmed | EQ006 → EQ007 | probe 50-pipe: legacy threw ArgumentException (Path.GetFullPath in NoSnapshotLinux.ConvertToSnapshotPath, the same path-character check), modern IOException (File.GetAttributes); NoSnapshotLinux has no fields, and BackupHandler creates it with its parameterless constructor |
| Duplicati | 51 | call | `Duplicati.Library.UsageReporter.ReportSet::DoInitCLR()` | not run (unsafe) | undetermined (unsafe: ReportSet's static constructor reaches UpdaterManager's, which writes update folders; other: void, and the difference is a static field written) | EQ006 → EQ007 | not run: the only difference is Cached_CLRVersion (Environment.Version), a static field no outcome observes |
| OpenRA | 4 | call | `OpenRA.Mods.Common.Traits.PlayerStatistics::ResolveOrder(OpenRA.Actor,OpenRA.Order)` | probe: this=uninitialized; self=value; order=constructor OpenRA.Order.Order(string, OpenRA.Actor, bool, OpenRA.Actor[], OpenRA.Actor[]) | undetermined | EQ006 → EQ007 | callable (probe 4-a: both returned; the uninitialized receiver, OrderCount at its default, is the state the constructor PlayerStatistics(Actor) leaves), but the culture-sensitive StartsWith only decides whether the field OrderCount is incremented; the method returns void, so the difference is a field write, not observable as an outcome |
| OpenRA | 5 | call | `OpenRA.Download::CancelAsync()` | not run (unsafe) | undetermined (unsafe: a non-null WebClient field is reached only by the Download constructors, which start a network download) | EQ006 → EQ007 | not run: the claimed WebClient.CancelAsync difference needs a download in progress (network, and a timing difference in the call trace); with the field at its default both sides return alike |
| OpenRA | 6 | call | `OpenRA.FileSystem.ZipFileLoader.ReadWriteZipFile::.ctor(string,bool)` | probe: filename=value; create=value | confirmed | EQ006 → EQ007 | probe 6-c and 6-d (the constructor called directly, create false): legacy threw ArgumentException, modern threw IOException (File.ReadAllBytes); 21-c shows the same through its caller Folder.OpenPackage |
| OpenRA | 7 | argument | `OpenRA.CryptoUtil::DecryptString(System.Security.Cryptography.RSAParameters,string)` | probe: parameters=value; data=value | confirmed | EQ006 → EQ007 | probe 7-a (a generated key, data decrypting to an ill-formed UTF-8 sequence): both returned, different strings (Encoding.UTF8.GetString replaces the ill-formed bytes with a different number of replacement characters); the run's replay also built and called it (not-constructible: the call traces differ) |
| OpenRA | 20 | argument | `OpenRA.StreamExts::ReadString(System.IO.Stream,System.Text.Encoding,int)` | probe: s=constructor System.IO.MemoryStream.MemoryStream(byte[]); encoding=constructor System.Text.UTF8Encoding.UTF8Encoding(); maxLength=value | confirmed | EQ006 → EQ007 | probe 20-c (a length-prefixed ill-formed UTF-8 sequence): both returned, different strings (Encoding.GetString replaces the ill-formed bytes with a different number of replacement characters) |
| OpenRA | 21 | call | `OpenRA.FileSystem.Folder::OpenPackage(string,OpenRA.FileSystem.FileSystem)` | probe: this=constructor OpenRA.FileSystem.Folder.Folder(string); filename=value; context=value | confirmed | EQ006 → EQ007 | probe 21-a: legacy threw ArgumentException (Path.Combine rejects the character), modern returned (an IReadOnlyPackage-typed value, not compared); 21-b equal, 21-c differs through File.ReadAllBytes |
| OpenRA | 23 | call | `OpenRA.FileSystem.Folder::Delete(string)` | probe: this=constructor OpenRA.FileSystem.Folder.Folder(string); filename=value | confirmed | EQ006 → EQ007 | probe 23-a: legacy threw ArgumentException (Path.Combine), modern returned |
| OpenRA | 25 | call | `OpenRA.FileSystem.Folder::Update(string,byte[])` | probe: this=constructor OpenRA.FileSystem.Folder.Folder(string); filename=value; contents=value | confirmed | EQ006 → EQ007 | probe 25-a: legacy threw ArgumentException (Path.Combine), modern threw IOException (File.Create) |

## Findings and tickets

Every finding of the runs and the re-adjudication has a ticket, filed in this PR:

- P2-250: a framework `static readonly` field (`IntPtr.Zero`, `String.Empty`) is a free input per side, so four false
  EQ002 on Git Extensions (rows 0, 3, 7, 18) and nine `not-constructible` replays rest on it.
- P2-251: an enum argument or field is a sort element in the model, which replay never builds (5 replays).
- P2-252: a driver never awaits a returned `Task`, so an `async` member's outcome is not compared (Duplicati 14, 47).
- P2-253: a replay or `probe` driver of a Windows Desktop project runs without `Microsoft.WindowsDesktop.App`, which
  blocks Git Extensions 22 and 41 and makes three `AppSettings` replays `reproduced` by a `TypeInitializationException`.
- P2-254: the two drivers of a case start in different working directories, so three replays are `reproduced` on paths
  that hold them.
- P2-249 (filed earlier in this PR, by the size guard): a delegate the model's runs call is built with the model's answers.

Not a ticket: Git Extensions 25's replay called `BugReportInvoker::Report(Exception,bool)`, which starts a process (it
failed only because `BugReporter.exe` was not found). `--execute` runs the solutions' code unsandboxed, as its stderr note
says and ADR 0035 and ADR 0062's Consequences record; reaching more members runs more of it.

## Method notes

- The runs reused P2-237's checkouts and restores: this worktree's `.corpus` is a directory junction to P2-237's
  worktree's, so the runs sit at `.corpus/pairs/<slug>/runs/20261019-0134-full-quick-execute` (Git Extensions, 338 s),
  `20261019-0140-...` (Duplicati, 584 s) and `20261019-0149-...` (OpenRA, 251 s), all exit 1, beside P2-237's.
- A first round of runs (at the ticket's first feature commit) found OpenRA's `float2` replays failing with
  `FormatException` (a replay sent a solution struct's fields while the driver read it positionally), fixed before these
  runs; its numbers are not used.
- The 51's matching, the replay counts and the reasons were computed from the three SARIF files by scripts that were
  not committed; the per-pair summaries give each pair's counts.
