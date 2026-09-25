# M3-032 `Equiv.Execute` and `tools/runtime-diff`: call a BCL member on .NET Framework 4.8 and on .NET 10, and compare
Status: todo
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
  Framework 4.8 compilation uses the reference assemblies under `.corpus/refasm` or the
  targeting pack, and gets an `app.config` with `supportedRuntime v4.0`. Canonical form:
  primitives in invariant format; strings as JSON; `char` as a code point; arrays and `List<T>`
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
   `String::IndexOf(String)` with inputs `("\r\n", "\n")` under `en-US` differs: it is
   Microsoft's documented ICU vs NLS example (1 on .NET Framework, -1 on .NET 5 and later). The
   first is `ToUpper_TurkishI_Agrees`, the second `IndexOf_NewlineInCrLf_Diverges`, both
   integration tests.
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
`ToUpper_TurkishI_Agrees`, `IndexOf_NewlineInCrLf_Diverges`, `GetHashCode_IsNondeterministicOnModernOnly`,
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
