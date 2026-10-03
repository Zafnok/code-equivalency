# P2-066 verdict: two .NET-to-.NET version upgrades (2026-10-03)

`jellyfin-13023` (net8.0 to net9.0, a pure bump) and `gitextensions-9860` (net5.0 to net6.0, a bump
with fixes), each run in `full` mode at equiv `8e0ed3c`. Per-pair detail is in
`docs/runs/2026-10-03-full-<slug>/SUMMARY.md`. Nothing under `src/` was changed.

**These pairs take no part in ADR 0028's rule table.** No threshold is applied to them, no agent
median includes them, and this file has no continue, re-scope or stop line.
`docs/runs/2026-09-30-full-verdict.md` remains the verdict.

## Outcome in one line

**`equiv` loads and compares both upgrades, and every runtime rule it applied was allowed by the
pair's interval, but its Divergent results on an upgrade are mostly wrong: 5 confirmed against 126
false positives.** On the pure bump it proves none of the 573 pairs that are not congruent.

## The table

Shares are over ADR 0034's changed pairs. An Unknown that is `unmatched-overload` is not a matched
pair and is left out.

| Pair | Shape | Runtimes detected (legacy, modern) | Matched pairs | Congruent pairs | Changed pairs | Proved Equivalent | Unknown | Divergent |
|---|---|---|---|---|---|---|---|---|
| jellyfin-13023 | pure bump | net8.0 (37 projects), net9.0 (37) | 14503 | 13927 (96.0%) | 573 | 0 | 462 (80.6%) | 111 (19.4%): EQ002 9, EQ006 102 |
| gitextensions-9860 | bump with fixes | net5.0 (43 projects), net6.0 (43) | 14020 | 13295 (94.8%) | 722 | 239 (33.1%): bounded 227, lockstep-induction 12 | 421 (58.3%) | 62 (8.6%): EQ002 3, EQ006 59 |

Unknown by reason, all Unknown results:

| Pair | opaque | timeout | abstraction | unaligned-loop | recursion | unmatched-overload |
|---|---|---|---|---|---|---|
| jellyfin-13023 | 243 | 138 | 52 | 29 | 0 | 63 |
| gitextensions-9860 | 253 | 98 | 52 | 16 | 2 | 20 |

Every detected runtime came from the project's target framework attribute. Both pairs load 100% of
their projects on both sides.

### Why unedited code is not congruent

The two pull requests edit 14 and 16 `.cs` files. Most changed pairs are in files they did not touch:

| Pair | Changed pairs in edited files | In byte-identical files | Main cause in the byte-identical files (text scan of the bodies) |
|---|---|---|---|
| jellyfin-13023 | 49 | 524 | 353 hold a `String.Equals` call with a `StringComparison`, whose row has no change point (P2-113). 63 hold a call that binds to a new .NET 9 overload (P2-070). |
| gitextensions-9860 | 25 | 697 | 417 hold an interpolated string, which binds to `string.Format` on net5.0 and to `DefaultInterpolatedStringHandler` on net6.0. The solver proves 223 of them. |

So the two upgrades fail to be congruent for different reasons, and only one of the two reasons is
recovered by the solver.

## EQ006 by runtime-change row (criteria 4 and 5)

A pair's interval is (older runtime, newer runtime]. A row with `changedIn: null` has no known change
point and ADR 0040 decision 2 applies it whenever the runtimes differ.

### jellyfin-13023, interval (net8.0, net9.0]

| Row (member) | `changedIn` | EQ006 |
|---|---|---|
| `System.String::Equals(string,System.StringComparison)` and `System.String::Equals(string,string,System.StringComparison)` | null | 92 |
| `System.String::Split(char[],System.StringSplitOptions)` and the `string[]` form | null | 5 |
| `System.String::GetHashCode(` | null | 2 |
| `System.Char::IsLetterOrDigit(char)` | null | 1 |
| `System.IO.File::ReadAllText(string,System.Text.Encoding)` | null | 1 |
| `System.IO.File::WriteAllText(string,string,System.Text.Encoding)` | null | 1 |

None of the 14 rows whose change point is net9.0 produced an EQ006.

### gitextensions-9860, interval (net5.0, net6.0]

| Row (member) | `changedIn` | EQ006 |
|---|---|---|
| `System.String::Equals(string,System.StringComparison)` and the static form | null | 12 |
| `System.String::GetHashCode(` | null | 7 |
| `System.Environment::GetFolderPath(System.Environment.SpecialFolder)` | null | 7 |
| `System.Text.Encoding::get_Default(` | null | 6 |
| `System.Char::IsLetterOrDigit(char)` | null | 5 |
| `System.String::Split(char[],System.StringSplitOptions)` and the `string[]` form | null | 4 |
| `System.Net.WebUtility::HtmlEncode(string)` | null | 4 |
| `System.IO.File::ReadAllText(string,System.Text.Encoding)` | null | 2 |
| `System.Net.Http.Headers.MediaTypeWithQualityHeaderValue::.ctor(string)` | null | 2 |
| `System.Net.Http.Headers.AuthenticationHeaderValue::.ctor(string,string)` | null | 2 |
| `System.IO.IsolatedStorage.IsolatedStorageFileStream::.ctor(string,System.IO.FileMode,System.IO.FileAccess,System.IO.FileShare)` | null | 1 |
| `System.IO.File::WriteAllBytes(string,byte[])` | null | 1 |
| `System.Environment::GetEnvironmentVariable(string,System.EnvironmentVariableTarget)` | null | 1 |
| `System.Math::Min(float,float)` | null | 1 |
| `System.IO.File::WriteAllText(string,string,System.Text.Encoding)` | null | 1 |
| `System.Windows.Forms.ListViewGroupCollection::` | net6.0 | 1 |
| `System.Windows.Forms.TreeNodeCollection::get_Item(` | net6.0 | 1 |
| `System.IO.FileStream::get_Position(` | net6.0 | 1 |

### Criterion 5

**Holds. No EQ006 cites a row whose change point lies outside its pair's interval**, so P2-055
applied no rule it should not have, and no ticket is filed against it. Of 161 EQ006, 3 cite a row
whose `changedIn` is the pair's newer runtime, and 158 cite a row with no change point.

That second number is the finding. The 32 rows without a change point each record a difference
between .NET Framework 4.8 and .NET 10, and on a .NET-to-.NET pair they are almost the only thing
that fires. Giving them a change point is P2-113.

## Adjudication of every EQ002 and EQ006 (criterion 6)

Classified as P2-047 does: **confirmed**, **false positive** (a hand trace shows the claimed
difference cannot be reached on the pair's runtimes) or **undetermined** (with the obstacle).
Replay gave nothing: neither pair's runtimes are installed, so no result has `replay` or
`proofMethod: observed`.

- Decision: with no runtime for either side, an EQ002 is confirmed by the pull request's own diff
  plus, where the difference is in the code and not in the runtime, a test under `.corpus/audit/P2-066/`
  that runs both bodies on net10.0. P2-047 compiled each side for its real runtime; here that is not
  possible, and the two tests below exercise APIs whose behaviour is the same on every .NET version.
- Decision: an EQ006 on a row with no change point is a false positive when the difference the row
  records is documented to predate the pair's older runtime: ICU replaced NLS in .NET 5
  (`String.Equals`), `Encoding.Default` is UTF-8 on every .NET (Core), `StringSplitOptions.TrimEntries`
  exists from .NET 5, and `Math.Min` follows IEEE 754-2019 from .NET Core 3.0. Where no document places
  the change, it is undetermined: "change point unknown, and no runtime to measure it".
- Decision: `String.GetHashCode` stays undetermined, as in P2-047.
- Decision: an EQ002 whose only difference is an added effect-free BCL call is a false positive, as
  P2-047 counted them (P2-071).

| Pair | Rule | Confirmed | False positive | Undetermined | Total | Precision |
|---|---|---|---|---|---|---|
| jellyfin-13023 | EQ002 | 2 | 3 | 4 | 9 | 40.0% (2 of 5) |
| jellyfin-13023 | EQ006 | 0 | 97 | 5 | 102 | 0% (0 of 97) |
| gitextensions-9860 | EQ002 | 3 | 0 | 0 | 3 | 100% (3 of 3) |
| gitextensions-9860 | EQ006 | 0 | 26 | 33 | 59 | 0% (0 of 26) |
| **Both** | **all** | **5** | **126** | **42** | **173** | **3.8% (5 of 131)** |

### Confirmed (5)

| Pair | Procedure | What changed | Basis |
|---|---|---|---|
| jellyfin-13023 | `Jellyfin.Server.Helpers.StartupHelpers::PerformStaticInitialization()` | No longer sets the two `ServicePointManager` properties | The pull request deletes the two statements |
| jellyfin-13023 | `Emby.Server.Implementations.ApplicationHost::GetCertificate(string,string)` | Loads the certificate with the PKCS#12 loader in place of the constructor that accepts any format | Test: on a DER file the legacy call loads and the modern call throws, so the method logs a different error; on a PFX file both load |
| gitextensions-9860 | `GitExtensions.Program::Main()` | Calls `Application.SetHighDpiMode` | The pull request adds the call and removes the same setting from the application manifest, so the application's DPI mode is unchanged |
| gitextensions-9860 | `ResourceManager.Xliff.TranslationSerializer::Serialize(ResourceManager.Xliff.TranslationFile,string)` | Writes through an `XmlWriter` in place of a `StreamWriter` | Test: the modern body's file starts with a byte-order mark and the legacy body's does not |
| gitextensions-9860 | `BugReporter.Program::Main()` | Passes a different commit hash, from a generated `ThisAssembly` constant | Source is byte-identical; the two commits build different constants. Not a change the upgrade made (P2-098) |

### False positives (126)

| Cause | Results | Ticket |
|---|---|---|
| A row with no change point fires for a difference that predates the pair's older runtime: `String.Equals` 104, `String.Split` 9, `Encoding.Default` 6, `Math.Min` 1 | 120 EQ006 | P2-113 |
| A row matches a member its change does not touch: enumerating `ListView.Groups`; reading `TreeNodeCollection`'s indexer where only assigning a null node reaches the change | 2 EQ006 | P2-075 (Notes line added) |
| `FileStream.Position` read on a stream with no asynchronous read or write | 1 EQ006 | P2-114 |
| `Enum.GetValues<T>()` and `Enum.IsDefined<T>(v)` against the `Type`-taking calls they replace | 2 EQ002 | P2-117 |
| An added `ConfigureAwait(false)` | 1 EQ002 | P2-071 (merged after this build, 8bf3aa1) |

### Undetermined (42)

| Obstacle | Results |
|---|---|
| Row with no change point, and no document places the change; neither runtime is installed to measure it: `Environment.GetFolderPath` 7, `Char.IsLetterOrDigit` 6, `WebUtility.HtmlEncode` 4, `File.ReadAllText` 3, `File.WriteAllText` 2, the two `System.Net.Http.Headers` constructors 4, `IsolatedStorageFileStream` 1, `File.WriteAllBytes` 1, `Environment.GetEnvironmentVariable` 1 | 29 EQ006 |
| `String.GetHashCode`: the value differs between any two processes; whether a caller relies on it is a ranking question | 9 EQ006 |
| Matcher code the regex source generator emits differently for net8.0 and net9.0; the two matchers were not traced | 4 EQ002 (P2-118) |

## What the pull requests changed, and what `equiv` said

No behaviour change either pull request made is reported Equivalent.

| Pair | Real behaviour changes in the diff | Reported |
|---|---|---|
| jellyfin-13023 | 4: `StartupHelpers::PerformStaticInitialization()`, `ApplicationHost::GetCertificate(string,string)`, `PluginManager::TryGetPluginDlls(...)` (a null check that now checks its argument), `ProbeResultNormalizer::GetMpegTimestamp(string)` (`Read` to `ReadExactly`) | EQ002, EQ002, Unknown (`timeout`), Unknown (`abstraction`) |
| gitextensions-9860 | 2 outside test code: `Program::Main()`, `TranslationSerializer::Serialize(...)` | EQ002, EQ002 |

Jellyfin's other edits are behaviour-preserving (`Enum.Parse<T>`, `Activator.CreateInstance<T>`, a
removed null check on a value type). None of them is proved: they are Unknown or falsely Divergent.

## Findings

| Ticket | Finding | Pairs |
|---|---|---|
| P2-113 (M) | 32 runtime-change rows have no change point, so they apply to every .NET-to-.NET pair: 158 of 161 EQ006, 120 of them false, and 353 unedited Jellyfin bodies out of congruence | both |
| P2-114 (S) | The `FileStream.Position` row fires where the stream had no asynchronous read or write | gitextensions-9860 |
| P2-115 (S) | A corpus restore inherits this repository's NuGet source mapping and fails for a repository that names nuget.org differently | jellyfin-13023 |
| P2-116 (S) | A string constant holding a lone surrogate crashes lowering: 3 pairs | jellyfin-13023 |
| P2-117 (S) | Generic `Enum` members are Divergent from the `Type`-taking calls they replace: 2 false EQ002 | jellyfin-13023 |
| P2-118 (M) | Code the regex source generator emits is compared as written code: 4 EQ002, 12 Unknown, 30 `no-body` pairs | jellyfin-13023 |
| P2-119 (M) | 29 controller actions with one method each are `unmatched-overload` and never compared | jellyfin-13023 |
| P2-075, existing | Two more false EQ006 from rows that match unaffected members (Notes line) | gitextensions-9860 |
| P2-070, existing (PR #359) | Calls rebound by .NET 9's new overloads: 63 unedited bodies; forms in Jellyfin's SUMMARY | jellyfin-13023 |
| P2-105, existing | The same three lowering crashes as on `gitextensions-11284` | gitextensions-9860 |
| P2-098, existing | The commit-hash constant behind `BugReporter.Program::Main()` | gitextensions-9860 |

Not filed: the interpolated-string binding that takes 417 unedited Git Extensions bodies out of
congruence. The solver proves 223 of them, and no result is wrong. It costs solver time and leaves
the rest Unknown for other reasons.

## Method notes

- **Candidates.** Both pairs loaded, so none was replaced. Looked at and not chosen:
  `gitextensions-11240` (net6.0 to net8.0), `jellyfin-10463` and `jellyfin-15475` (more code edits),
  `btcpayserver-5479`, and `ILSpy-3119`, where the decompiler library stays on `netstandard2.0`, so
  the PR does not move every project.
- **Runs.** A first Jellyfin attempt ended in 3 s (the checkout's `global.json` pinned SDK 8 with
  `latestMinor`; `-Fetch` now rewrites that, P2-058) and is void. The first full runs, at `46e6636`,
  took 6h47m on Git Extensions; Jellyfin's was stopped by the user after 12h17m, 11h02m of it on one
  pair inside `Z3Backend.Inline`. Both pairs were rerun at `8e0ed3c`, after P2-076: 1h11m and 1h32m.
  Git Extensions' two runs agree on the rule, proof method and runtime-change row of every result;
  one Unknown's reason moved from `timeout` to `opaque`.
- **`--execute`.** Not run on either pair (criterion 3 asks for it where both runtimes are
  installed). The box has Microsoft.NETCore.App 6.0.36 and 10.0.x, and no .NET 5, 8 or 9; Git
  Extensions' net6.0 side also needs Microsoft.WindowsDesktop.App 6, which is not installed.
- **Tests.** Neither pair has a `verify_command`; no upstream test was run.
- **A census** (`--lower-only`) of Jellyfin was taken while its first run was stuck. Its numbers
  match the full run's census.
- Source text and model values stay under `.corpus/` (`docs/runs/README.md`).
