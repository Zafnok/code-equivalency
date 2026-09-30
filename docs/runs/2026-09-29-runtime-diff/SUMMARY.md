# runtime-diff run: gitextensions-8522, pmb-lethek__signalr.extras.autofac, pmb-shiningrush__serviceant, pmb-tomasjohansson__adapters-shortest-paths-dotnet

ADR 0035 decision 1 (ticket P2-051): `tools/corpus/corpus.ps1 -RuntimeDiff <slug> -Top all` on the four
pairs of `docs/runs/2026-09-26-runtime-diff/SUMMARY.md`, against the same M3-033 census SARIFs (`externalCallees`).
`equiv`: PR branch for P2-051, where `DriverFactory` resolves Windows Forms and `System.Drawing` members on
both runtimes (the .NET 10 driver targets `net10.0-windows` when it uses one) and the generators build
`System.Drawing.Point`, `Size` and `Rectangle` and their `F` variants. `tools/runtime-diff` seed 0, 64
cases per overload, the fixed culture set (invariant, en-US, tr-TR, de-DE, ja-JP).

## Members, by pair

| Pair | kind | externalCallees (legacy / modern) | members attempted | no matching symbol | ran (cases executed) | not constructible | divergent overloads |
|---|---|---|---|---|---|---|---|
| gitextensions-8522 | human | 4677 / 4705 | 3873 | 1188 | 1571 | 1114 | 86 |
| pmb-lethek__signalr.extras.autofac | agent | 15 / 15 | 13 | 2 | 6 | 5 | 0 |
| pmb-shiningrush__serviceant | agent | 34 / 34 | 34 | 8 | 20 | 6 | 0 |
| pmb-tomasjohansson__adapters-shortest-paths-dotnet | agent | 333 / 332 | 229 | 121 | 79 | 29 | 6 |

"members attempted" is the union of both sides' `externalCallees`, all of them (`-Top all`), with the
equiv-only `<T1,T2>` generic-instantiation suffix stripped. On 2026-09-26 the top 200 per side reached 201
members on Git Extensions; this run reaches 3873.

"no matching symbol" is `runtime-diff`'s "no public member on both runtimes matches". Of Git Extensions'
1188, 918 are generic instantiations in `System.Linq.Enumerable` (504) and `System.Collections.Generic`
(414), whose stripped identity still names concrete type arguments; generic-instantiation resolution is
out of this ticket's scope. 76 are Windows Forms or `System.Drawing` (was 127 of 131): protected members
such as `Control::OnClick(System.EventArgs)` and `Control::Dispose(bool)`, which the driver cannot call and
`DriverFactory` does not list, and members taking `System.IntPtr` (`Graphics::FromHdc(System.IntPtr)`), which
the census spells `System.IntPtr` and .NET 10's symbols spell `nint`. Either way no input can be built for
them.

Combined over all four pairs (distinct member identities):

| | count |
|---|---|
| Distinct members with a symbol on both runtimes, cases executed | 1596 (705 of them Windows Forms or `System.Drawing`) |
| Divergent | 87 |
| Nondeterministic on one side only (no other divergence) | 5 |
| Agreed (ran, no divergence, no one-sided nondeterminism) | 1504 |
| Not constructible | 1118 |

Not-constructible reasons (an overload may have several): needs a live window handle or a message loop 640;
not a method, a property getter or a constructor (setters, event accessors, operators) 604; generic 126;
`System.Drawing.Color` 65; `System.DateTime` 24; `System.Windows.Forms.Padding` 14; `System.TimeSpan` 11; a
type parameter 16; `System.Threading.CancellationToken` 9; `System.Windows.Forms.Message` 5; others under 5
each. No overload is blocked by `System.Drawing.Point`, `Size` or `Rectangle` any more; on 2026-09-26 those
blocked 12.

The 1504 agreed members are not written as `runtime-changes.json` rows, as on 2026-09-26. Their reports are
under `.corpus/runs/<slug>/runtime-diff/` locally, not committed (ADR 0028).

## Divergent members

87 members diverge. 26 are already covered by a row: the ICU rows for `String::Compare(`, `CompareTo(`,
`EndsWith(`, `IndexOf(`, `LastIndexOf(` and `StartsWith(` (13 overloads), `Double::ToString(`,
`Char::IsLetter(`, `Regex::`, `RuntimeInformation::get_FrameworkDescription(`, `DataFormats::GetFormat(string`,
`Bitmap::`, `Icon::`, `Image::`, `Directory::Enumerate`, `Directory::GetFiles(`, and M3-033's three measured rows.

Two are harness artefacts and get no row:

- `System.Environment::GetCommandLineArgs()` returns each driver's own file name (`EquivDriver.exe` against
  `EquivDriver.dll`), which differs by construction.
- `System.Runtime.InteropServices.Marshal::GetLastWin32Error()` reads the last Win32 error that the runtime's
  own start-up left on the thread (2 against 0), not anything the caller did.

The other **59** become `source: measured` rows. Grouped by cause:

| Cause | Rows | Members |
|---|---|---|
| Upfront invalid-path-character validation removed (.NET Framework throws `ArgumentException`; .NET 10 returns, or the file system throws `IOException` or `DirectoryNotFoundException`) | 28 | `Path::ChangeExtension`, `Combine` (3 and 4 strings), `GetExtension`, `GetFileName`, `GetFileNameWithoutExtension`, `GetFullPath`, `GetPathRoot`, `IsPathRooted`; `File::Create`, `Delete`, `GetAttributes`, `GetLastWriteTime`, `GetLastWriteTimeUtc`, `OpenRead`, `ReadAllBytes`, `ReadAllLines`, `ReadAllText(string)`, `ReadLines`; `Directory::CreateDirectory`, `Delete(string,bool)`, `SetCurrentDirectory`; `StreamWriter::.ctor(string)` and `(string,bool)`; `XmlReader::Create(string,XmlReaderSettings)`; `XmlTextWriter::.ctor(string,Encoding)`; `Attachment::.ctor(string)`; `AssemblyName::GetAssemblyName(string)` |
| A different exception type, or a different argument checked first, for a null or empty argument | 8 | `File::ReadAllText(string,Encoding)`, `WriteAllBytes`, `WriteAllLines`, `WriteAllText(string,string,Encoding)`; `StreamReader::.ctor(string,Encoding)`; `Font::FromLogFont(object)`; `AuthenticationHeaderValue::.ctor(string,string)`; `MediaTypeWithQualityHeaderValue::.ctor(string)` |
| IEEE 754 floating point (.NET Core 3.0): `-0` ordering, `-0` from `Ceiling`, large-argument `Sin`/`Cos`, saturating float-to-int conversion | 9 | `Math::Max` and `Min` (`double`, `float`), `Math::Ceiling(double)`, `Math::Sin`, `Math::Cos`, `RectangleF::Union`, `Size::Ceiling(SizeF)` |
| ICU against NLS | 2 | `String::Equals(string,StringComparison)`, `String::Equals(string,string,StringComparison)` |
| Newer Unicode data or lone-surrogate handling | 3 | `Char::IsLetterOrDigit(char)`, `WebUtility::HtmlEncode(string)`, `Uri::EscapeDataString(string)` |
| New enum values or relaxed argument checks on .NET 10 | 4 | `String::Split(char[],StringSplitOptions)` and `(string[],StringSplitOptions)` (`TrimEntries`), `String::ToUpper(CultureInfo)` (null culture), `String::Remove(int)` |
| Other | 5 | `Environment::GetEnvironmentVariable(string,EnvironmentVariableTarget)`, `Environment::GetFolderPath(SpecialFolder)` (a folder missing on disk returns `""`), `Path::GetInvalidPathChars()` (no `"`, `<`, `>`), `IsolatedStorageFileStream::.ctor(string,FileMode,FileAccess,FileShare)`, `ButtonRenderer::DrawButton(Graphics,Rectangle,PushButtonState)` (null `Graphics` unchecked) |

Each row's witness (input, culture and both outcomes) is the first witness of its report, copied into
`src/Equiv.Core/RuntimeChanges/runtime-changes.json`. Two witnesses name a machine-specific value, which
the row writes with a placeholder and says so: `GetFolderPath`'s legacy path (as `%APPDATA%`) and
`GetFullPath`'s modern path (as `<current directory>`). `docs/runtime-changes-review.md`'s Measured section
lists all 59.

One-sided nondeterminism, reported only: `String::GetHashCode()` (modern, already covered),
`File::Open(string,FileMode,FileAccess)` (legacy 41), `MemoryStream::.ctor(int)` (modern 6),
`StringBuilder::.ctor(int)` (modern 6), and `Marshal::AllocCoTaskMem(int)` (both sides, an allocation's
address).

## Congruent pairs that lose congruence (Git Extensions)

A fresh `--lower-only` census of gitextensions-8522 from this branch, once with `main`'s table and once
with the 59 new rows (the lowering has changed since 2026-09-26, so neither side reuses that run's numbers):

| | `pairsCongruent` | `changedPairs` |
|---|---|---|
| Before this run's rows | 12398 | 1143 |
| After adding the 59 measured rows | 12247 | 1294 |

**151 pairs** that were congruent, so `equiv` would have reported them Equivalent without a word, call
one of the new members and now count as changed.

## Findings

- 59 new measured rows; 28 of them are the embedded-NUL and invalid-character path validation that
  M3-033 found on three members, now seen on every `Path`, `File`, `Directory` and stream member Git
  Extensions calls.
- Windows Forms members mostly cannot be run with generated inputs: 640 overloads need a window, and 65
  take `System.Drawing.Color`. Of the 705 Windows Forms and `System.Drawing` members that did run, only
  `ButtonRenderer::DrawButton`, `Font::FromLogFont`, `RectangleF::Union` and `Size::Ceiling` diverge beyond
  rows that already covered them.
- `externalCallees` spells `System.IntPtr` where .NET 10's symbols give `nint`, so such members never match.
  No input can be built for `IntPtr` anyway, so nothing is lost today.
