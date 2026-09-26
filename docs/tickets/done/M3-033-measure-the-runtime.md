# M3-033 Measure the runtime: every BCL member the corpus calls, run on both runtimes, and measured rows for the ones that differ
Status: in-progress
Effort: M
Model: Sonnet, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M3-032, M3-030

## Goal
The census reports calls to members that are already in the table (`runtimeChangeCalls`, ADR
0034). It says nothing about the members that are not in it. This ticket makes the census list
every BCL member a lowered body calls, then runs `tools/runtime-diff` on the most-called ones
from the corpus pairs. A member that differs becomes a row with `source: measured` and a witness.
A member that agrees is recorded as tested, with its case count, in the run summary only. This
turns "Equivalent by congruence" on a retarget from "no listed member is called" into "no listed
member is called, and the BCL members it calls were measured".

## Spec references
ADR 0035 decision 1; ADR 0034 (census fields); ADR 0028 (corpus rules: only corpus code, nothing
third-party committed); `docs/runs/README.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. `loweringCensus` gains `externalCallees`: for each side, the distinct `CallIdentity` values of
   calls whose target assembly is part of the runtime (the framework reference assemblies the
   project compiled against), each with its call-site count. They are sorted by count, then
   ordinally. `--lower-only` writes them. The census snapshot on `business-layer` shows the field.
2. `tools/corpus/corpus.ps1 -RuntimeDiff <slug> [-Top 200]` reads a pair's census SARIF, takes
   the union of both sides' top N `externalCallees`, runs `runtime-diff` on each, and writes the
   reports under `.corpus/runs/<slug>/runtime-diff/`. Nothing is written outside `.corpus/`.
3. The run covers Git Extensions and the three agent pairs from the 2026-09-24 census. Its
   `docs/runs/<date>-runtime-diff/SUMMARY.md` has:
   - members run, divergent, nondeterministic on one side, and not constructible;
   - per divergent member: its identity, the cultures it diverged under, and whether a curated
     or documented row already covered it.

   It holds no inputs taken from corpus code: witnesses are generated inputs, so they may appear.
4. Every divergent member that no existing row covers becomes a `runtime-changes.json` row with
   `source: measured`, a reason in your own words, and `witness`: the input, culture and both
   canonical outcomes. `docs/runtime-changes-review.md` gains a "Measured" section listing them.
5. A member that diverges only in `Nondeterministic` on the modern side gets a row too, with
   reason `nondeterministic on .NET 10 only`.
6. The PR description states how many congruent Git Extensions pairs lose congruence because of
   the new rows, using M3-030's token-identical proxy. That is the number of former silent
   Equivalents on the human pair.

## Files
`src/Equiv.Cli/LoweringCensus.cs` (and whatever it needs in `src/Equiv.Frontend.CSharp` to know a
call's target assembly), `tools/corpus/corpus.ps1`, `tools/corpus/README.md`,
`src/Equiv.Core/RuntimeChanges/runtime-changes.json`, `docs/runtime-changes-review.md`,
`docs/runs/<date>-runtime-diff/SUMMARY.md`, census snapshot files, tests in the matching projects.

## Tests
`Census_ListsExternalCalleesByCount`, `Census_ExcludesCallsIntoTheSolution`,
`RuntimeChangeTableTests.MeasuredRowHasAWitness`.

## Size guard
Any change to `runtime-diff`'s engine means M3-032 missed something. File it as a P2 ticket and
stop.

## Out of scope
Members with non-constructible parameters (list them, do not extend the generators). Third-party
NuGet packages' members: package drift is ADR 0034's `packageVersionChanges`, and executing
packages is later work.

## Notes

- Decision: `CallIdentity` gains `External` (bool, default false; `src/Equiv.Core/CallIdentity.cs`),
  computed only where a real `IMethodSymbol` and `Compilation` are both available — the general
  `IrLowerer.Identity(method)` path. The API-equivalence-adapted call (`entry.Modern`, a bare string
  identity, `IrLowerer.cs` line ~1457) keeps `External = false`: there is no symbol there to classify,
  and the 14 curated/documented rows already cover the members that path can rewrite to.
- Decision: reused `ProjectEmitter.IsReferenceAssembly`'s `ReferenceAssemblyAttribute` check (M4-009)
  instead of duplicating it, via a new shared `Equiv.Frontend.CSharp.ReferenceAssemblies` internal
  helper. "External" = `compilation.GetMetadataReference(method.ContainingAssembly)` is a
  `PortableExecutableReference` carrying that attribute — a `CompilationReference` (the solution's own
  code) or a `PortableExecutableReference` without it (a NuGet package) are both excluded, matching
  the ticket's Out of scope.
- Decision: `IrText`'s call syntax gained a second, independent suffix marker `@` (round-tripping
  `CallIdentity.External`, parsed after the existing `!` for `RuntimeChanged`), so
  `LoweringCensusTests`'s IR-text fixtures can express an external call without a real Roslyn
  compilation. `@` was added to the parser's symbol-character allowlist.
- Decision: `externalCallees` entries sort by call-site count descending, then member name
  ordinally, per the acceptance criterion ("sorted by count, then ordinally").
- Decision: `-RuntimeDiff <slug>` resolves the pair's most recently modified `*census*` run directory
  under `.corpus/pairs/<slug>/runs/` and reads its `equiv.sarif`, rather than taking a raw SARIF path,
  matching the `-Packages`/`-Unchanged` slug-based convention already in `corpus.ps1`.
- Decision: a generic method's `CallIdentity` carries an equiv-only `<T1,T2>` instantiation suffix
  (`CallIdentityFactory`, for a constructed generic). `-RuntimeDiff` strips it with
  `-replace '<[^>]*>$', ''` before calling `runtime-diff --member`, since `runtime-diff` resolves a
  member against real Roslyn symbols and knows nothing of that suffix. The stripped identity still
  fails to resolve when it names concrete non-BCL type arguments in its *parameter list* (e.g.
  `ConcurrentDictionary<IHub,ILifetimeScope>::TryAdd(IHub,ILifetimeScope)`); `runtime-diff` itself
  reports the corresponding open generic as `not constructible (generic)`. Not a bug: this is
  exactly the class of member the ticket's Out of scope and `tools/runtime-diff/README.md`'s "What
  runs" section already exclude.
- Decision: `RuntimeChange` gains an optional init-only `Witness` (`RuntimeChangeWitness`: `Input`,
  `Culture`, `Legacy`, `Modern`, each the raw JSON text `tools/runtime-diff`'s own report already
  writes), set only for `source: measured` rows. `RuntimeChangeTable.Parse` reads it when present.
- Decision: `System.String::GetHashCode()` diverged only in `nondeterministic.modern` on
  `pmb-tomasjohansson__adapters-shortest-paths-dotnet` (315 of 320 cases) — exactly acceptance
  criterion 5's shape. It already has a curated row (`System.String::GetHashCode(`), so it gets no
  second row: `RuntimeChangeTableTests.EveryRowHasASource` rejects a duplicate `Member` string, and
  criterion 4's "no existing row covers" guard is read as taking precedence over criterion 5's "gets a
  row too" for a member that is already covered. `docs/runs/2026-09-26-runtime-diff/SUMMARY.md` lists
  it under "Divergent members" with "Already covered? Yes" instead.
- Toolchain: the `.corpus/refasm` reused from a sibling worktree (see below) had a stale `refasm/root/.NETFramework/vX`
  + `refasm/obj/` layout from an older `-Prepare` implementation. `corpus.ps1`'s current `-Env` expects
  `$CorpusRoot/refasm/.NETFramework/vX` directly; moved `root/.NETFramework` up a level and deleted
  `obj/` (a stale `-Prepare` scratch project) to fix "the reference assemblies for .NETFramework,Version=vX
  were not found" on `pmb-shiningrush__serviceant` (targets v4.5.2).
- Toolchain: `gitextensions-8522`'s `--lower-only` census threw a `NullReferenceException` while
  lowering `CommonTestUtils.ConfigureJoinableTaskFactoryAttribute::AfterTest` on the first attempt
  (contained by P2-011: exit 5, one `LoweringFailure`, everything else still counted) and completed with
  no lowering failures on an identical retry. This matches the flakiness M3-031's Notes already
  recorded for a different method on the same pair (`ICSharpCode.TextEditor.TextAreaClipboardHandler::Paste`,
  2026-09-24): not reproduced with a fixed cause, not fixed here (Out of scope: this ticket is measurement,
  not the frontend), and the census this run used for `-RuntimeDiff` is from the run that completed
  cleanly.
- Note: reused the corpus already fetched and, for the three agent pairs, already migrated in the
  sibling worktree `real-pair-census-e1bae3` (the same repo, a different git worktree) instead of
  re-cloning and re-running the agent migrations, to avoid redoing that work. Only the absolute paths
  baked into each `pair.json` needed rewriting to this worktree's path; the checkouts, migrated
  sources and reference-assembly cache all worked unmodified. `pmb-chrismckelt__webminder`, prepared
  there but not part of the 2026-09-24 census, was left out.
- Decision: `-RuntimeDiff`'s per-member `dotnet run` calls run under `$ErrorActionPreference =
  'Continue'` (restored in a `finally`), mirroring `Invoke-Git`'s existing guard. Windows PowerShell
  5.1 promotes any stderr line — including `runtime-diff`'s own "no public member matches" usage
  message — to a terminating `NativeCommandError` under the script's `$ErrorActionPreference =
  'Stop'`, which would otherwise abort the whole run partway through a pair's member list.
- Observed, real corpus run (`docs/runs/2026-09-26-runtime-diff/SUMMARY.md`): of 4677/4705 (legacy/modern)
  `externalCallees` on `gitextensions-8522`, 131 of its top 200-per-side union do not resolve on both runtimes —
  127 of those are `System.Windows.Forms.*`/`System.Drawing.*`, because `Equiv.Frontend.CSharp.Execution.DriverFactory`
  (M3-032) targets the BCL only. Git Extensions' most-called external members are therefore mostly
  untestable by `runtime-diff` today. No ticket filed: `DriverFactory`'s referenced-assembly scope is
  M3-032's, and this ticket's Size guard forbids touching its engine.
