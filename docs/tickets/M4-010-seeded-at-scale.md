# M4-010 Seeded at scale: mechanical seeds on corpus methods, so seeded recall is measured over hundreds of changes, not a handful
Status: in-progress
Effort: M
Model: Sonnet, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M0-012

## Goal
ADR 0028's seeded-recall criterion must be 100%, and a miss preempts all other work. The seeds in
`tools/corpus/seeds.md` are a short hand-written list, so 100% on them is weak evidence. This
ticket gives the corpus a seeder:
- it applies M0-012's `Changing` and `Preserving` mutation operators to methods of a pair's
  modern side, inside a copy under `.corpus/`;
- it writes a manifest of what it changed.

M4-007's `seeded` mode then reports recall over the mechanical seeds as well as the hand-written
ones. Behaviour-changing seeds can be equivalent mutants, so a seed counts as a miss only when it
is reported Equivalent **and** the repo's own tests, or M4-009's replay, show the behaviour
changed. Every other seed reported Equivalent is listed as "unconfirmed".

## Spec references
ADR 0028 (seeded recall; corpus rules); M0-012 (operators); `.claude/skills/equiv-corpus-run`
(`seeded` mode); `tools/corpus/seeds.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. The mutation operators move from `tests/Equiv.TestSupport/Mutations` into a small tool,
   `tools/corpus/seeder/` (a console project using Roslyn syntax rewriting only). `Equiv.TestSupport`
   references the tool's library, so M0-012 and the seeder share one implementation.
2. `corpus.ps1 -SeedMechanical <slug> [-Count 300] [-Seed n]` copies the modern side to
   `.corpus/pairs/<slug>/seeded-mech/`. It picks methods at random, weighted towards changed pairs
   when the census lists any, applies one operator per method, and writes
   `.corpus/pairs/<slug>/seeded-mech/seeds.json`: method identity, operator, and line.
3. Every seeded copy must build. An operator application that fails to compile is dropped and
   counted, not kept.
4. The `equiv-corpus-run` skill's `seeded` mode runs the mechanical copy too. Its SUMMARY template
   gains:
   - seeds applied, dropped, and reported per verdict;
   - recall = (Divergent + line-scoped Unknown with a cause on the seed's line) / confirmed
     behaviour-changing seeds;
   - Preserving seeds reported Divergent (a precision bug each);
   - the unconfirmed list's size.
5. The skill states that a confirmed miss is a `P2-nnn-soundness-<slug>.md` ticket and is
   reported to the user first, as in M4-007 criterion 3.
6. No seeded source text is committed: only counts and identities, per `docs/runs/README.md`.

## Files
`tools/corpus/seeder/**` (new), `tools/corpus/corpus.ps1`, `tools/corpus/README.md`,
`tests/Equiv.TestSupport/*` (the reference), `.claude/skills/equiv-corpus-run/SKILL.md`,
`docs/runs/README.md` (the template fields).

## Tests
`Seeder_AppliesOneOperatorPerMethod`, `Seeder_DropsUncompilableMutants`,
`Seeder_ManifestListsEverySeed`, `Seeder_IsDeterministicForASeed`.

## Size guard
Any change under `src/` is out of place. The seeder is tooling.

## Out of scope
Running the seeded mode (M4-007 does). New operators beyond M0-012's. Seeding the legacy side.

## Notes
- Decision: the seeder's operators are reimplemented against Roslyn syntax (`Equiv.Corpus.Seeder.SyntaxMutator`),
  not the internal DSL `tests/Equiv.TestSupport/Mutations/PairSyntax.cs` still generates from. The seeder has to
  run on arbitrary real corpus methods, which are ordinary parsed C#, not that DSL's fixed `Oracle.M(...)` shape.
  `PairGen.Pair` now renders its generated method to text once, reparses it, and calls the same
  `SyntaxMutator.Sites`/`Apply` the seeder uses, so M0-012 and M4-010 share one implementation exactly as
  criterion 1 asks; `PairSyntax`'s AST and renderer are untouched (still M0-012's generator), only the mutation
  step was rerouted.
- Decision: `MutationOperator` and `SyntaxMutator` are `public`, not `internal` with `InternalsVisibleTo`.
  Granting `Equiv.TestSupport` (and, transitively, every project that references it) access to the seeder's
  internals also exposed its top-level-statement `Program` class, which collided with `Equiv.TestSupport`'s own
  unrelated `IrGenAst.Program` (an existing internal record) and broke unqualified lookups in `IrGen.cs`. Kept
  `CA1515` off for just those two files (`.editorconfig`) rather than restructure `Program.cs`.
- Decision: `tools/corpus/seeder.Tests/` sits beside `tools/corpus/seeder/`, not nested under it. A test project
  nested inside the seeder's own directory falls inside the seeder csproj's default file glob (no project file
  stops it, unlike `bin`/`obj`), so the seeder assembly tried to compile the test project's own files too.
  Matches the existing `tools/check-coverage` / `tools/check-coverage.Tests` sibling-folder precedent.
- Decision: no CLI parsing package. `Equiv.Corpus.Seeder`'s CLI is four flags (`--root`, `--manifest`, `--count`,
  `--seed`, `--changed`), parsed by hand in `Program.cs`; `docs/adr/0002-dependencies.md`'s `System.CommandLine`
  row is scoped to `Cli`, and pulling it into a second project for this shape of surface wasn't worth extending
  that scope.
- Found (fixed): `PairSyntax`'s renderer wraps almost every non-leaf expression in its own parens (or
  `checked`/`unchecked`) on top of whatever the caller adds — an `if`'s condition or a literal binary operand
  therefore renders doubly parenthesised. `DropNullCheck` and `ChangeConstant` originally pattern-matched the
  immediate child node's type directly and so never matched anything generated by `PairGen`, which
  `PairGenTests.EveryOperatorIsDrawn` caught (CsCheck's `Where` retry cap exceeded, reported against the
  `default(MutationOperator)` placeholder rather than the real culprit — tracked down by testing
  `SyntaxMutator.Sites` directly against a captured `PairGen` sample instead of trusting the generator's own
  error report). Fixed with a small paren-stripping `Unwrap()`, which also lets those two operators see through
  parenthesised real corpus code (e.g. `if ((x == null))`, `n + (1)`).
- Found (fixed): a freshly built `SyntaxFactory` token or node carries no trivia, and Roslyn never inserts a
  space just because two adjacent tokens would otherwise lex as one identifier. `IntroduceTemporary`'s `var`
  immediately followed by the temporary's name merged into a single invalid identifier
  (`PairGenLoweringTests.EveryGeneratedSideLowersWithoutOpaque` caught this as a `CS0103` in the shrunk pair).
  Fixed by normalising the freshly built declaration's own internal spacing and reusing the replaced node's
  trivia at every splice point; `InvertIf`'s brand-new `else` clause and `RenameLocals`' declarator rename had
  the same class of risk (a keyword or type name directly against a new identifier) and got the same fix.
- Decision: `CompileCheck.StillCompiles` compiles the original and mutated file text independently, in memory,
  against the BCL only (`TRUSTED_PLATFORM_ASSEMBLIES`, the same references `Equiv.Tests.Integration`'s
  `PairRuntime` already uses for M0-012's own compile step), and compares only the count of `Error`-severity
  diagnostics. A lone corpus file never compiles standalone (it cannot see sibling types or NuGet packages), but
  those errors are identical on both sides and cancel out; only errors the mutation itself introduces survive
  the comparison. This is the acceptance criterion 3 check; the tool never invokes MSBuild (Roslyn syntax
  rewriting only, per the ticket).
- Decision: at most one seed per file, in addition to at most one per method. `corpus.ps1 -SeedMechanical`
  attempts a real `dotnet build` of the whole seeded copy after the seeder runs, as a backstop beyond the
  per-file compile check above (which cannot see a cross-file break, since it has no project context at all);
  capping seeds at one per file means a build failure is always attributable to exactly one seed without
  needing to bisect. A failure there is reported as a warning naming the copy to inspect, not auto-recovered.
- Decision: method selection is an Efraimidis-Spirakis weighted sample without replacement (weight 5 for a
  changed identity, 1 otherwise), and the operator for a chosen method is the first of a Fisher-Yates shuffle
  of all twelve with `Sites > 0`; both draw from the one `Random(seed)` the CLI is given, so a run is
  reproducible from its seed alone (`Seeder_IsDeterministicForASeed`).
- Decision: "weighted towards changed pairs when the census lists any" reads the most recent `equiv.sarif`
  under `.corpus/pairs/<slug>/runs/**` (any mode, not only `census`), if one exists yet; a result counts as
  changed unless it is `EQ001` with `properties.proofMethod == "congruence"` (ADR 0034's own definition of
  unchanged). `corpus.ps1` extracts this list itself (consistent with its other `-Metrics`/`-Unchanged` JSON
  handling) rather than teaching the Roslyn-only seeder tool to parse SARIF.
