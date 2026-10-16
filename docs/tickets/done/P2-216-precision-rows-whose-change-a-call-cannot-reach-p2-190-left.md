# P2-216 Precision: five more runtime rows fire on calls their change cannot reach
Status: done (PR #520)
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
P2-073, P2-075, P2-189 and P2-190 (done) gave rows a precondition or narrowed them to the calls their change
affects. P2-212's adjudication (`docs/runs/2026-10-15-precision-verdict.md`) found 5 false EQ006 in its fresh
samples on rows they did not cover, each a call whose arguments or receiver cannot reach the row's change:
- `System.Diagnostics.Process::get_StartInfo(` on a `Process` the method itself creates with `new Process { ... }`:
  only a process this code did not start (`GetProcesses`, `GetProcessById`) reaches the change; a new one gets a
  fresh `ProcessStartInfo` on both runtimes (`gitextensions-8522`, `ResourceManager.LinkFactory::ExecuteLink(...)`);
- the UTF-8 decoding rows (`utf8Decoding`) on a local `byte[]` the method fills element by element with constants
  that form well-formed UTF-8: only ill-formed input decodes differently (`gitextensions-8522`,
  `GitCommandsTests.Git.EncodingHelperTest::DecodeStringTestUtf8()`);
- `System.Windows.Forms.TreeNode::set_ImageIndex(` and `set_SelectedImageIndex(`, with no precondition, on
  `ImageList.Images.Count - 1`, which is never below -1: only a value below -1 reaches the out-of-range change
  (`gitextensions-8522`, `SettingsTreeViewUserControl::AddPage(...)`);
- the `Environment.OSVersion` row on `.Version.Major` compared with the constant 6, a threshold the compatibility
  shim never crosses: P2-190's `shimmedVersionRead` reads `.Version` compared with a `Version` and not `.Major`
  compared with an `int` (`gitextensions-8522`, `GitUI.UserControls.ExListView::.ctor()`);
- the ICU `System.String::Equals(string,System.StringComparison)` row with `ordinalUnaffected`, where the comparison
  is a solution getter whose every return is `Ordinal` or `OrdinalIgnoreCase`: the exemption reads a constant only
  (`duplicati-3124`, `Duplicati.Library.Snapshots.UsnJournalService::IsPathEnumerated(string)`).

When done, each of these rows matches only calls that can reach its change.

## Spec references
`src/Equiv.Core/RuntimeChanges/runtime-changes.json` (`precondition`, `shimmedVersionRead`, `ordinalUnaffected`),
`docs/tickets/done/P2-190-*.md` and `P2-189-*.md` (how a row is narrowed and its soundness argued), ADR 0040.

## Acceptance criteria (all must hold; nothing beyond them)
1. Each of the five shapes gets a precondition or a narrowing, each with its soundness argument in Notes (what
   every call it exempts cannot reach), or `equiv-decide` records why one is not taken.
2. A sample pair per shape taken, EQ006 before and Equivalent after, and one where the change is reachable that
   stays EQ006.
3. `gitextensions-8522` and `duplicati-3124` rerun in compare mode quick; Notes give the five procedures' new
   results and every other result that moved.

## Tests
The runtime-change table tests; the samples.

## Size guard
Data flow beyond one method for any shape: take the shapes that need none and file the rest.

## Out of scope
Path rows (P2-207, P2-208, P2-217, P2-218).

## Notes
- Filed 2026-10-15 by P2-212 (criterion 6). Procedures and fingerprints are in the verdict's appendix.
- Decision: each shape taken -> a `requires` fact (P2-114's `RowPrecondition`, read by `RowPreconditions.Suppress`), two
  new (`foreignProcess`, `belowNoneIndex`) and two widened (`utf8Decoding`, `shimmedVersionRead`). Alternatives: a
  per-call `precondition` (P2-190's first decision says why not: a receiver's history and a setter's value are no
  operand of the call site the lowering hands `CallIdentityFactory`). Rule: 1.
- Decision: the ordinal-comparison shape (`UsnJournalService::IsPathEnumerated`) -> not taken, filed as P2-224. Its
  `StringComparison` is `Utility.ClientFilenameStringComparison`, a getter of another class whose body is
  `IsFSCaseSensitive ? Ordinal : OrdinalIgnoreCase`: reading it is data flow beyond one method, the size guard.
  Alternatives: P2-200's callee reading now (the size guard says file it). Rule: 4.
- Decision: the image index threshold -> -2 (`ImageList.Indexer.NoneIndex`), not the -1 the Goal says. Source:
  dotnet/winforms `TreeNode.cs`, whose .NET 5 setters throw `ArgumentOutOfRangeException` for `value < NoneIndex` and
  return early or store otherwise (main still has `ThrowIfLessThan(value, NoneIndex)`); release/3.1, the .NET Framework
  code, validates nothing and stores the value. Alternatives: -1 as written (coarser, no source). Rule: 1.
- Decision: a never-negative length -> an array's or a string's `Length`, or the `Count` of a type of a `System`
  namespace that implements `ICollection`, `ICollection<T>` or `IReadOnlyCollection<T>`, neither virtual nor abstract
  nor an override; a length less a constant 0 to 2 is at least -2. Alternatives: any `Count` or `Length` (a solution
  type's can be negative), any metadata member (a third-party override can). Rule: 4.
- Decision: the UTF8Encoding decoding rows also require `utf8Decoding`. Their receiver is never `Encoding.ASCII`, and
  well-formed bytes decode the same on them as on the `Encoding` rows; without it a `new UTF8Encoding().GetString(a, 0,
  n)` of well-formed constants would still fire. Alternatives: only the `Encoding` rows (the shape in the run binds
  there). Rule: 1.
- Decision: the bytes -> `ConstantUtf8`, which builds the array from a new `byte[]` of constants (up to 4096 bytes) and
  the element writes `a[i] = b;` with constants in earlier statements of the blocks the call runs in, and checks the
  bytes read (all, or a constant index and count) with `System.Text.Unicode.Utf8.IsValid`. Alternatives: a symbolic
  byte model (no other row needs one), the method's whole data flow (the size guard). Rule: 4.
- Decision: `LocalValues.Writes` counts an increment or a decrement as a write of an unknown value. No earlier fact read
  a value type that can be incremented (strings, `FieldInfo`, `Encoding`, `ProcessStartInfo`); an image index is one,
  and `var i = Count - 1; i--;` must not hold `Count - 1`. Alternatives: a check in `belowNoneIndex` alone (the next
  numeric fact would miss it). Rule: 4.
- Criterion 1, the soundness argument of each shape taken (what every call it exempts cannot reach):
  - `Process::get_StartInfo(` (`foreignProcess`): .NET's getter (dotnet/runtime `Process.cs`) throws only when
    `_startInfo` is null and the object is associated with a process (`_haveProcessId || _haveProcessHandle`). A
    `Process` made with `new` is associated only by `Start`, whose body runs `Close()` then reads `StartInfo` (which
    creates `_startInfo`) before it starts anything; `Close` and `Dispose` never clear `_startInfo`. So a read on an
    object the method shows came from `new Process(...)` (the object a `new Process { ... }` initializer sets members
    of, a new one read at once, or a local only ever assigned one) never throws; .NET Framework never throws. A process
    a caller passed in, `GetProcessById`'s, or `this` in a subclass, still fires.
  - `utf8Decoding`: the documented change is how .NET replaces ill-formed UTF-8 (U+FFFD per the Unicode best practice);
    a well-formed sequence decodes to the same text on both. `ConstantUtf8` exempts a call only when the method shows
    every byte read: no loop, labelled block, lambda or local function lies between the array's declaration and the
    call, so each earlier statement of those blocks ran once and in order and no later one has; a use of the array
    anywhere else before the call (an alias, a call that could fill it, a write with a non-constant, a write in a
    branch) or in the call's own statement keeps the row. Out-of-range or ill-formed bytes keep it.
  - `TreeNode::set_ImageIndex(`, `set_SelectedImageIndex(` (`belowNoneIndex`): .NET throws only below -2, and stores
    every other value as .NET Framework does (above). A value at least -2 cannot reach the change: a constant, or a
    framework length (never negative) less at most 2. A compound assignment, an increment and a deconstruction set a
    value not followed, so they fire; a read runs only the getter, which neither row names.
  - `Environment::get_OSVersion(` (`shimmedVersionRead`): .NET Framework's shim reports the real version up to 6.2.9200
    and 6.2.9200 above it, and every Windows above it has a major of 6 or more. So `.Version.Major` is the same on both
    up to 6.2 and, above it, 6 against a real major of at least 6. An operator comparison with a constant `c` is
    exempt when it gives the same answer at 6 as at every greater major; as a comparison of integers changes its answer
    only at `c` and `c + 1`, those (where they are above 6) and 6 are the majors tried. `>= 6`, `> 5`, `< 6` and
    `== 5` are exempt; `> 6`, `== 6`, `>= 10` fire.
- Criterion 2, the samples: `samples/runtime-row-call-cannot-reach` (.NET Framework 4.8 against .NET 10, Windows Forms,
  `Calls.cs` the same text on both sides). At this ticket's baseline all eight methods are EQ006 (run with `main`'s
  build, `e48ae839`). After: `NewProcessFile`, `Katakana`, `ShowLastImage`, `IsVistaOrNewer` Equivalent by congruence;
  `ProcessFile` (a process passed in), `Truncated` (two bytes that end mid-sequence), `ShowImage` (an index passed
  in), `IsWindows10OrNewer` (`>= 10`) EQ006, each `notCongruent` naming its row. `SamplesEndToEndTests` asserts both
  sets and the snapshot. Unit tests: `RowCallPreconditionsTests` (each fact, both ways, in the lowering and the
  fingerprint), `RuntimeChangeTableTests` (the JSON names and the rows that require each fact).
- Criterion 3, the reruns: 2026-10-15, compare mode quick, default `--jobs` (as P2-212), one after the other on
  P2-212's checkouts, while another session's thorough run on `gitextensions-9860` shared the box. Baseline at `main`'s
  head `e48ae839`, after at this branch's fix `cb4c16ec`: `gitextensions-8522` 380 s and 315 s, `duplicati-3124` 278 s
  and 263 s, each exit 1. The five procedures, baseline then after:

  | Pair | Procedure | Row | Baseline | After |
  |---|---|---|---|---|
  | gitextensions-8522 | `ResourceManager.LinkFactory::ExecuteLink(...)` | `Process::get_StartInfo(` | EQ006 | EQ006, on `Process::Start(` (P2-225) |
  | gitextensions-8522 | `GitCommandsTests.Git.EncodingHelperTest::DecodeStringTestUtf8()` | `Encoding::GetString(` | EQ006 | EQ001 (congruence) |
  | gitextensions-8522 | `SettingsTreeViewUserControl::AddPage(...)` | `TreeNode::set_ImageIndex(`, `set_SelectedImageIndex(` | EQ006 | EQ001 (congruence) |
  | gitextensions-8522 | `GitUI.UserControls.ExListView::.ctor()` | `Environment::get_OSVersion(` | EQ006 | EQ001 (congruence) |
  | duplicati-3124 | `UsnJournalService::IsPathEnumerated(string)` | `String::Equals(string,StringComparison)` | EQ006 | EQ006 (not taken, P2-224) |

  `ExecuteLink` no longer stops on the `StartInfo` row: the legacy side's `process.Start()` reads `UseShellExecute`'s
  .NET Framework default, `true`, which the modern side sets explicitly, so the pair is equivalent but the `Process.Start`
  row's change is reachable on the legacy call. No ticket owned that, so P2-225 is filed for it.
- Criterion 3, every other result that moved. `duplicati-3124`: none (EQ006 117, EQ002 23, EQ003 322 on both).
  `gitextensions-8522`: EQ006 137 to 132, EQ002 23 to 24, EQ003 575 to 574, EQ001 12,876 to 12,881. Besides the three
  above, each read by hand:
  - `GitCommandsTests.Git.EncodingHelperTest::GetStringTestUtf8()`, EQ006 to EQ001 (congruence): the same three
    well-formed bytes in a `new byte[4]`, decoded by `new UTF8Encoding().GetString(byte[])`;
  - `GitUI.CommandsDialogs.RevisionFileTreeController::LoadChildren(...)`, EQ003 (timeout) to EQ001 (congruence): its
    only image index sets are the constants `TreeNodeImages.Folder` (1) and `Submodule` (2);
  - `GitExtensions.Program::Main()`, EQ006 to EQ002: its `Environment.OSVersion.Version.Major >= 6` is now exempt, and
    the counterexample left is the generated `ThisAssembly.Git::Sha` constant its fingerprint already named, which
    differs between the two commits; ADR 0046 keeps it an ordinary constant, so the difference is real, not this ticket's.
- The IL lowering's sample stubs (`IlSamples`) gain the Windows Forms members the new sample uses
  (`TreeNode.ImageIndex`, `SelectedImageIndex`, `ImageList.Images.Count`).
