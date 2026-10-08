# P2-106 A body whose only diagnostics are warnings promoted to errors is not `unbound`
Status: in-progress
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-085, P2-058

## Goal
On `powershell-19687`, 82 pairs are Unknown(`unbound`) on both sides. The projects set
`TreatWarningsAsErrors`. The commit pins a .NET 8 preview, and against the released net8.0
reference pack it reports SYSLIB0051, CS0672 and SYSLIB0050, the obsoletion of formatter-based
serialization. Those are warnings. The bodies bind: every symbol resolves and every operation has
a type. A warning the project chose to promote says nothing about whether `equiv` can read the
body. Treat a body as `unbound` only when it has a diagnostic whose default severity is error.

## Spec references
ADR 0029 and its 2026-10-01 clarification (what `unbound` means); P2-085;
`docs/runs/2026-10-02-cleanup-powershell-19687/SUMMARY.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Apply `equiv-adr`'s bar test first, because this narrows ADR 0029's clarification. Record the
   outcome in Notes.
2. First confirm the cause on the pair: Notes list the diagnostic ids behind the 82 results. If
   any of them has default severity error, say so and keep that body `unbound`.
3. A fixture project with `TreatWarningsAsErrors` and a body that calls an obsolete member lowers
   that body. A body with a real binding error (CS0103) in the same project stays `unbound`.
4. The same rule decides whether a project is skipped at load.
5. A rerun of `powershell-19687` reports its `unbound` count in Notes. It was 82.

## Tests
- `CSharpFrontendTests.AWarningPromotedToAnErrorDoesNotUnbindABody`
- `CSharpFrontendTests.ARealBindingErrorStillUnbindsABody`

## Out of scope
Fixing the corpus checkout. Analyzer diagnostics, which the loader does not run.

## Notes
- Criterion 1, the bar test (`equiv-adr`): its first row fits. ADR 0029 decision 2 already decides that a body with an
  "error diagnostic" is `unbound`, and a warning the project promotes is a case it did not spell out. So the vehicle
  is a dated bullet under ADR 0029's Clarifications, written in this PR (2026-10-07). It is not a new ADR: what
  `Unknown(Unbound)` means, erroneous code, does not change, no rule id or SARIF shape changes, and no gate is weakened.
- Decision: what an error is -> `Diagnostic.DefaultSeverity == Error`, in one method,
  `CompilationDiagnosticClassifier.IsError`, which the lowerer and all three loaders call (the loaders through
  `CompilationDiagnosticClassifier.Errors`, which replaces three copies of the same query). The ticket names default
  severity; one method is what makes criterion 4's "the same rule" true by construction.
- Decision: criterion 3's fixture project -> a compilation whose general diagnostic option is error, which is what
  `TreatWarningsAsErrors` sets, in the two named `CSharpFrontendTests`; plus a real project file with
  `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` through the bare loader, and a workspace project through the
  MSBuild loader, for criterion 4. Not a new `samples/` folder: a sample is a paired fixture with an expected SARIF,
  and the rule is the frontend's alone.
- Decision: no `Release:` footer. A fix that turns Unknowns into compared pairs is a patch under `equiv-release`.
- Criterion 4, what it changes: at load the errors only pick which projects are skipped, and the ids that skip
  (CS0006, CS0518, CS1705, CS8032, and on the legacy side CS0012, CS0234, CS0246, CS0400) are all errors by default
  except CS8032. So no project that loaded before is skipped now or the reverse, unless it promoted a CS8032, which
  `Compilation.GetDiagnostics` does not report. The visible change is that a promoted warning is no longer listed
  among a loaded project's diagnostics.
- Criterion 2, measured 2026-10-07 on `powershell-19687` before the fix (compare mode quick, `equiv` at `c59fa0fc`,
  463 seconds, exit 1): 82 `unbound` results, the same 82 as the 2026-10-02 run, with 132 causes on the legacy side.
  The SARIF records a cause's span and not its id, so the ids come from joining each legacy cause's file, line and
  column to the errors of a hand build of the legacy `System.Management.Automation` (`dotnet build`, same
  environment). Only that project was built by hand, so the causes in the four projects that depend on it are
  classified by the construct at the span.
  - 65 results have only promoted warnings. Joined by position, in `System.Management.Automation`: 54 results, 23
    with SYSLIB0051 alone and 31 with SYSLIB0051 and CS0672. One more has SYSLIB0050 alone. In
    `Microsoft.PowerShell.Commands.Management`, `Commands.Utility`, `ConsoleHost` and `Security`: 10 results, each a
    serialization constructor's `: base(info, context)` or a `GetObjectData` override and its base call, which are
    the same two diagnostics; not joined, because those projects were not built by hand.
  - 17 results have a diagnostic whose default severity is error, and stay `unbound`. One is the CS0103 the 2026-10-02
    summary saw, in `System.Management.Automation.PowerShellAssemblyLoadContext::.ctor(string)`. The other 16 read
    `GitCommitId`, `ProductVersion` or a `Version_*` constant of `PSVersionInfo`, members the repository's own source
    generator declares. The hand build runs the generator and reports nothing there; the loaded compilation has no
    generator output, so the names do not exist. Their ids were not read from the compiler: by the construct they are
    "name does not exist" errors (CS0103 inside `PSVersionInfo`, CS0117 outside it). That is a fault of the checkout
    as loaded, which is out of scope here.
  - The hand build also reports analyzer diagnostics (IDE0031, CA1822, CA2249, IDE0100, SYSLIB1051). None is at a
    cause's position: the loader does not run analyzers.
- Criterion 5, the rerun (same pair, same mode, this branch, 287 seconds, exit 1): **`unbound` 17, from 82.** The 65
  are all EQ001 by congruence now. By rule: EQ001 33,809 (was 33,744), EQ002 6, EQ003 74 (was 139). No other
  result changed its rule or its reason, and no result is newly `unbound`. There are 33,889 results in both
  runs.
- Deviation: the PR also changes `SsaBuilder`, which the ticket does not name. The first two reruns ended in a native
  stack overflow (exit -1073741571, no SARIF) in `SsaBuilder.Walk`, a depth-first walk with one stack frame per
  block, while lowering `System.Management.Automation.Runspaces.TypeTable::Process_Types_Ps1Xml(string,System.Collections.Concurrent.ConcurrentBag<string>)`,
  a generated method thousands of blocks long. The run before the fix lowered the same method without overflowing;
  why the fix moves it over the edge was not established (the method itself was never `unbound`). Without the change
  criterion 5 cannot hold and the fix would turn a pair that completes into one that does not, so the walk now keeps
  its own stack and visits blocks in the same order (`SsaBuilderTests.AChainOfThousandsOfBlocksIsOrderedWithoutAFramePerBlock`,
  which ends the test process against the old walk). `SsaBuilder.ReadVariableRecursive` still recurses along a chain
  of single-predecessor blocks and did not overflow here.
- Surprising: a hand build of a checkout changes what the next run loads. After it the run had 33,853 pairs to lower, against 33,889 before. Both checkouts were deleted and fetched again before the rerun above, so the two runs
  load the same thing.
- Not changed: `ProjectEmitter` and the replay drivers still refuse to emit on any diagnostic reported as an error,
  promoted or not, because the compiler's own emit fails on them. `--execute` on a project that promotes warnings is
  a separate question.
