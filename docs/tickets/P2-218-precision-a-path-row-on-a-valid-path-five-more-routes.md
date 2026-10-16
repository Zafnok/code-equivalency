# P2-218 Precision: a path row on a valid path that reaches the call by five routes no done ticket follows
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-207, P2-208, P2-232

## Goal
P2-188, P2-200 and P2-201 (done) follow a valid path through the solution's own methods (four calls deep, through
a sealed type's override), through a `readonly` field or get-only auto-property, and through a validator; P2-207 and
P2-208 own the routes they left. P2-212's adjudication (`docs/runs/2026-10-15-precision-verdict.md`) found 5 false
EQ006 in its fresh samples on routes none of them names:
- `gitextensions-8522`, `GitCommands.Settings.GitExtSettingsCache::ReadSettings(string)`: the parameter of a
  `protected override` whose only call is its base class's template method, which passes a get-only property right
  after `File.Exists` returned true for it;
- `gitextensions-8522`, `GitUITests.Theming.ThemeLoaderTests::Should_throw_When_cyclic_css_imports()`: a property of
  an NSubstitute substitute that a `Returns` call in the same class set to a constant valid path;
- `gitextensions-8522`, `GitCommands.GitModule::InTheMiddleOfMerge()`: an interface call on a `static readonly` field
  initialised with `new` of a sealed solution type (P2-200 reads an override only when the receiver's static type is
  sealed); clearing it also needs P2-232's `WorkingDir` (filed by P2-208);
- `duplicati-3124`, `Duplicati.UnitTest.CommandLineOperationsTests::get_zipAlternativeFilepath()`: a `static readonly`
  field that the same object's field initializers already passed to `Path.Combine`, so on .NET Framework an invalid
  value throws before the getter can run;
- `duplicati-3124`, `Duplicati.Library.Utility.TempFile::.ctor()`: a settings property whose only setters throw unless
  `Directory.Exists` accepts the value, and which otherwise returns `Path.GetTempPath()`.

When done, the largest of these routes is followed, or `equiv-adr` records why none is.

## Spec references
`docs/tickets/done/P2-188-*.md`, `P2-200-*.md`, `P2-201-*.md` (routes, bounds and soundness arguments), P2-207,
P2-208, ADR 0019 (a callee pair assumed equivalent), ADR 0040.

## Acceptance criteria (all must hold; nothing beyond them)
1. Notes give each of the five its route and what else it needs, as P2-200's Notes did for its eight.
2. The route that clears the most of them alone is followed, with its soundness argument in Notes.
3. `gitextensions-8522` and `duplicati-3124` rerun in compare mode quick; Notes give the five procedures' new results.

## Tests
The path-row tests for the route taken, and a sample where the route carries an invalid path and the row fires.

## Size guard
A whole-solution call graph, or reading a mocking library's semantics beyond `Returns` of a constant: stop.

## Out of scope
The routes P2-207, P2-208 and P2-232 own.

## Notes
- Filed 2026-10-15 by P2-212 (criterion 6).
