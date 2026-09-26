# M3-032 `Equiv.Execute` and `tools/runtime-diff`: call a BCL member on .NET Framework 4.8 and on .NET 10, and compare
Status: done (PR #210)
Effort: L
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: M2-007; ADR 0035 accepted

## Goal
Build the first use of ADR 0035's second oracle. Its target is the BCL only, so no user code
runs yet. Given a BCL member identity, the tool:
- generates a driver program;
- compiles it for .NET Framework 4.8 and for .NET 10;
- runs both with generated arguments under a fixed set of cultures, each input twice per side;
- reports every input whose canonical outcome differs.

The engine parts land in `src/` under the 100% gates, and `tools/runtime-diff` is a thin console
over them. M3-033 runs it on the members the corpus calls. M4-009 reuses the runner for user code.

## Spec references
ADR 0035 (decision, "mechanism" paragraph); ADR 0031 (why this is Windows-only);
VERIFICATION-MODEL.md section 3 (runtime-changed APIs); ARCHITECTURE.md (component rules).

## Design
- **Core contract** (`src/Equiv.Core/Execution/`):
  - `ExecutionRequest(CallIdentity Member, IReadOnlyList<ExecutionInput> Inputs, IReadOnlyList<string> Cultures)`;
  - `ExecutionOutcome(ExecutionInput Input, string Culture, OutcomeKind Kind, string Canonical)`, where
    `OutcomeKind` is `Returned`, `Threw`, `NotComparable` or `NotConstructible`;
  - `IExecutionDriverFactory`, which turns a request into two runnable drivers.

  Records only, no process code.
- **Frontend** (`src/Equiv.Frontend.CSharp/Execution/DriverFactory.cs`): resolves the member with
  Roslyn against each runtime's reference assemblies and emits a C# `Main` that:
  - reads inputs as one JSON line per case on stdin;
  - sets `CultureInfo.CurrentCulture` and `CurrentUICulture`;
  - calls the member;
  - writes one canonical JSON line per case on stdout.

  Compile with Roslyn: `OutputKind.ConsoleApplication`, one compilation per runtime. The .NET
  Framework 4.8 compilation uses the installed .NET Framework 4.8 targeting pack (never
  `.corpus/`, which holds only third-party checkouts), and gets an `app.config` with
  `supportedRuntime v4.0`. Canonical form, written by the driver's own code and never by a
  runtime's `ToString` (ADR 0035): integers in decimal; `float` and `double` as their IEEE bit
  pattern in hex (.NET Core 3.0 changed default `double` formatting); `decimal` as its four
  `GetBits` integers; strings as JSON; `char` as a code point; arrays and `List<T>`
  of those element-wise; `null`; an exception as its type's full name only. Anything else is
  `NotComparable`.
- **Runner** (`src/Equiv.Execute/`): starts each driver as a child process (the `net48` exe
  directly, the `net10` driver through `dotnet`), with a per-case timeout and a per-process
  memory limit. It runs every input twice per side:
  - an outcome that differs between two runs of the same side is marked `Nondeterministic` on
    that side;
  - nondeterminism on one side only is itself a finding (for example string `GetHashCode`);
  - nondeterminism on both sides is excluded and counted.
- **Generators** (`src/Equiv.Execute/Inputs/`): type-directed, deterministic from a seed, no
  CsCheck in `src/`:
  - integers and floats: edge values, then random values;
  - `char`: ASCII, Latin-1, Turkish dotted/dotless i, combining marks, surrogate halves;
  - `string`: a fixed corpus of culture-sensitive strings (`"i"`, `"I"`, `"ß"`, `"ss"`,
    `"\u0000"`, a soft hyphen, `"æ"`, `"ae"`), empty, `null`, and random strings;
  - enums: every defined value plus one undefined;
  - `bool`, and `null` for every reference type.

  Parameter types outside this set make the member `NotConstructible`.
- **Cultures:** invariant, `en-US`, `tr-TR`, `de-DE`, `ja-JP`.
- **Tool** (`tools/runtime-diff/`): `runtime-diff --member "<CallIdentity prefix or exact>" [--seed n]
  [--cases n] --out report.json`. Every overload that matches a prefix is run. The report lists,
  per overload: cases run, divergent cases with the first 5 witnesses (input, culture, both
  outcomes), nondeterminism per side, and not-constructible parameters. Exits 0 with no
  divergence, 1 with one, 3 on usage errors.

## Acceptance criteria (all must hold; nothing beyond them)
1. `src/Equiv.Execute` exists and references `Equiv.Core` only. A new ArchUnitNET rule in
   `Equiv.Tests.Architecture` enforces this. ARCHITECTURE.md gains the component and the edge,
   with ADR 0035 cited.
2. The Core contract types and `IExecutionDriverFactory` exist as in Design. They reference no
   Roslyn, no Z3 and no `System.Diagnostics.Process`.
3. `DriverFactory` produces drivers that compile for `net48` and `net10.0`. On Windows,
   `String::ToUpper()` under `tr-TR` with input `"i"` returns the same on both runtimes. The
   case exists to prove the plumbing, and its expected value is recorded.
   `String::IndexOf(String)` with inputs `("ss", "ß")` under `en-US` differs: NLS expands `ß` to
   `ss` and ICU does not (0 on .NET Framework, -1 on .NET 10). The first is
   `ToUpper_TurkishI_Agrees`, the second `IndexOf_SharpSInSs_Diverges`, both integration tests.
   (Corrected in the PR: Microsoft's documented example, `("\r\n", "\n")`, returns 1 on both
   runtimes on current Windows ICU; see Notes.)
4. A member whose outcome differs between two runs of the .NET 10 side only is reported as
   `Nondeterministic` on the modern side, not as a divergence. The test uses
   `String::GetHashCode()`.
5. A member with an unsupported parameter type is reported `NotConstructible` with the type's
   name, and the tool exits 0.
6. On a non-Windows OS the tool exits 3 with `runtime-diff needs Windows and .NET Framework 4.8
   (ADR 0035)`.
7. `tools/runtime-diff` has a README with the command line and the report format.
8. 100% line and branch coverage on `Equiv.Execute`. The process-running seam is behind an
   interface, so unit tests use a fake. The real processes run only in the Windows integration
   tests.
9. ADR 0002 gains no row, because no package is added.

## Files
`src/Equiv.Core/Execution/*.cs`, `src/Equiv.Frontend.CSharp/Execution/*.cs`,
`src/Equiv.Execute/**` (new project), `Equiv.slnx`, `tools/runtime-diff/**` (new),
`tests/Equiv.Execute.Tests/**` (new), `tests/Equiv.Tests.Integration/RuntimeDiffTests.cs`,
`tests/Equiv.Tests.Architecture/*` (one rule), `docs/ARCHITECTURE.md`, `docs/QUALITY-GATES.md`
(new project under the coverage gate), `build.ps1` only if new projects are not picked up
automatically.

## Tests
`ToUpper_TurkishI_Agrees`, `IndexOf_SharpSInSs_Diverges`, `GetHashCode_IsNondeterministicOnModernOnly`,
`UnsupportedParameter_IsNotConstructible`, `NonWindows_ExitsThree`, `Runner_TimesOutACase`,
`Runner_RunsEachInputTwicePerSide`, `Canonical_FormatsEveryOutcomeKind` (unit, with a fake
process), `Execute_ReferencesOnlyCore` (architecture).

## Size guard
More than about 30 new source files, or any change to `Equiv.Verify.Z3` or `CompareCommand`,
means you are building M4-009. Stop.

## Out of scope
User assemblies. `--execute` in `equiv compare`. Writing `runtime-changes.json` rows (M3-033).
Coverage-guided fuzzing. Linux.

## Notes
- Deviation: criterion 3's divergent example. On Windows 11 build 26200 with .NET 10.0.12,
  `"\r\n".IndexOf("\n")` returns 1 under both `en-US` and the invariant culture, as .NET Framework
  4.8 does, so the documented ICU vs NLS example no longer diverges. ICU is active in the same
  driver: `"ss".IndexOf("ß")` is 0 on .NET Framework and -1 on .NET 10. The integration test uses
  that pair and is renamed `IndexOf_SharpSInSs_Diverges`; criterion 3 and the Tests list are
  corrected. The ADRs and the spec are unchanged.
- Decision: `IExecutionDriverFactory` also has `Resolve(member)`, which returns an
  `ExecutionSignature` per overload present on both runtimes. The input generators live in
  `Equiv.Execute`, which cannot see Roslyn, so they need the parameter types in Core's terms:
  `ExecutionParameter` and `ExecutionTypeKind`. `ExecutionDrivers` holds the two driver paths.
  All of them are records, an enum or the interface, as the Design asks.
- Decision: `ExecutionTypeKind` members are named `Signed32`, `Binary64`, `Text` and so on,
  because CA1720 rejects members named after the types (`Int32`, `Double`, `String`).
- Decision: an overload present on one runtime only is not run and is not listed. Its behaviour
  has nothing to be compared with.
- Decision: an enum's defined values are the union of both runtimes' values, so a member added
  on one side is still generated.
- Decision: the invariant culture is spelled `invariant` on the wire and in the report, since
  `""` reads as a missing value.
- Decision: a case that gets no answer is `NotComparable` with the value `"no answer"`. That
  covers the per-case timeout (10 s), the memory limit (1 GiB of private bytes, polled every
  20 ms, since .NET Framework has no `GCHeapHardLimit`) and a crashed driver. The process is
  then dropped and the next case starts a new one.
- Decision: nondeterminism on one side is reported, but it does not change the exit code. Only
  a divergence gives exit 1, per the Design's exit codes. No member matching `--member` is a
  usage error (exit 3).
- Decision: the legacy driver is C# 7.3, the newest language version a .NET Framework 4.8
  project defaults to. The modern driver uses the latest version.
- Decision: the legacy references are the targeting pack's DLLs that
  `RedistList/FrameworkList.xml` names, without `Facades`. The `v4.8` folder also holds native
  DLLs (`System.EnterpriseServices.Thunk.dll`, `System.EnterpriseServices.Wrapper.dll`), and
  Roslyn rejects them with CS0009 and CS1509. The Facades only forward types.
- Decision: `DriverFactory.Create` writes each driver's source beside it as `EquivDriver.cs`, so
  whoever reads a witness can see what ran. The unit tests also read it.
- Decision: the generators use SplitMix64, not `System.Random`. A seed then gives the same
  inputs on every .NET version, and CA5394 does not fire.
- Decision: `.github/workflows/mutation.yml` gains the `Equiv.Execute` leg, although it is not
  in the Files list. QUALITY-GATES.md requires a leg and a required check for every new `src/`
  project, and adding it as a required check in the ruleset is the user's action.
- Decision: `Equiv.Tests.Architecture` also gains `ExecutionContractStartsNoProcesses`, the proof
  for criterion 2's "no `System.Diagnostics.Process`". Core may no longer depend on
  `Equiv.Execute` either.
- Observed on the first local runs (the corpus measurement is M3-033):
  - `String::IndexOf(string)` with 100 cases: 65 of 500 divergent. The first witness is
    `("ss", "ß")`.
  - `String::ToUpper(CultureInfo)` with a null culture throws `ArgumentNullException` on .NET
    Framework. .NET 10 uses the current culture instead.
  - `String::GetHashCode()`: 45 of 50 cases were nondeterministic on the modern side only. The 5
    null receivers throw on both sides.
