# P2-218 Precision: a path row on a valid path that reaches the call by five routes no done ticket follows
Status: in-progress
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
- Branch: stacked on P2-232 (PR #530), which this ticket depends on and which was still open; its PR is based on P2-232's
  branch until that merges.
- Criterion 1, the five by route, from the legacy checkouts (`.corpus/repos/*@3f4ed21998af`, `@beaf03562fdc`; each
  procedure is the same on the modern side) and the P2-232 runs below. For each, what clears it, since a route that leaves
  another flagged call in the procedure clears nothing:

  | Pair | # | Procedure | Route | What else it needs |
  |---|---|---|---|---|
  | Git Extensions | 26 | `GitExtSettingsCache::ReadSettings(string)` | a parameter of a `protected override` whose only caller is the base class's template method (`FileSettingsCache` calls `ReadSettings(SettingsFilePath)` under `File.Exists(SettingsFilePath)`, which P2-217 reads) | not sound as it stands: `GitExtSettingsCache` is public, not sealed, with a public constructor, so a subclass in any assembly (Git Extensions loads plugins) can call `base.ReadSettings(x)` with any `x`. Only a sealed or non-public type closes its callers, and finding them all is a search across the solution's types, the size guard's call graph |
  | Git Extensions | 32 | `ThemeLoaderTests::Should_throw_When_cyclic_css_imports()` | an NSubstitute substitute's property that `Returns` set to a constant (`provider.AppThemesDirectory.Returns("c:\gitextensions\themes")` in `CreateMockPathProvider`) | the library's semantics past `Returns` of a constant: `Returns` configures the last call made on any substitute on the thread, so a `Returns` anywhere in the test assembly after a helper that read `AppThemesDirectory` reconfigures it, as do `ReturnsForAll<string>`, `When(...).Do(...)` and `Configure()`; the substitute also escapes into `ThemeCssUrlResolver`. Showing none of these happens is a reading of every such call in the assembly: the size guard |
  | Git Extensions | 36 | `GitModule::InTheMiddleOfMerge()` | an interface call on a `static readonly` field whose only value is `new` of a sealed type (`GitDirectoryResolverInstance.Resolve(WorkingDir)`) | `WorkingDir` (P2-232, now a valid path or empty), and two more: `Resolve` returns a path read from a `.git` file's `gitdir:` line (`path.EnsureTrailingPathSeparator()` under `Path.IsPathRooted(path)`, which validates less than `invalidPath` names), and `gitPath` under `Directory.Exists(gitPath)` after an earlier reassignment, which P2-074's guard rule does not read |
  | Duplicati | 27 | `CommandLineOperationsTests::get_zipAlternativeFilepath()` | `Path.Combine(BASEFOLDER, this.zipAlternativeFilename)`: a `static readonly` field the same object's field initializers already passed to `Path.Combine` (`SOURCEFOLDER`, and `DATAFOLDER` and four more in `BasicSetupHelper`) | `zipAlternativeFilename`, a `readonly` field set to the constant `"data-alternative.zip"`: a constant is not read as a valid path anywhere but as a call's own argument |
  | Duplicati | 41 | `TempFile::.ctor()` | `TempFolder.SystemTempPath`, whose setter throws unless `Directory.Exists` | not sound as it stands: the getter returns `SystemContextSettings.Tempdir`, which `SystemContextSettings.StartSession(tempdir)` sets from its public parameter unchecked, as well as from the checked setters. And `GenerateUniqueName()` (the project defines `DEBUG`) returns a name built from a stack frame that `IndexOfAny(Path.GetInvalidFileNameChars())` checked: a partial validator (P2-231) |

  Routes that clear one alone: #27's (two rules, below). #26's and #41's are not sound as the ticket states them, #32's is
  past the size guard, and #36's needs two more rules besides P2-232. No route clears more than one.
- Decision: the route followed is #27's, the only one that clears any of the five alone. It is two rules:
  - A string constant that does not meet `invalidPath` is a valid path wherever the rules read a value (a stored field,
    a conditional's branch, a callee's return), as it already is as a call's own argument
    (`RuntimeChangeTable.IsValidPathConstant`). P2-207's test that `Run() => M("index.txt")` keeps `M`'s row is now the
    opposite, and right: `index.txt` is a path .NET Framework accepts; `M("index|txt")` keeps it.
  - In an instance member of a class, a read of a `static readonly` field that an instance field or property
    initializer of the class or a base class has already passed to the same member as the same parameter, with nothing in
    the initializer able to skip that call (only calls, object creations, arguments and conversions between it and the
    initializer), does not reach that member's path rows (`ValidPaths.IsAcceptedBefore`). It is read only for the call's
    own rows, never as a valid path elsewhere: `Path.Combine` accepting a value says nothing of a colon or a blank.
- Soundness of the second rule. Every object of the class ran all of its own and its base classes' instance field and
  property initializers before any instance method, accessor or constructor body could run on it (a constructor that
  chains `: this(...)` reaches one that runs them; a record's copy constructor copies an object that did). On .NET
  Framework the earlier call returned only if its validation accepted the field's value, else the constructor threw and
  no object existed; the field is `static readonly`, so it has held that value since its type was initialized. The
  later call is the same member with the same value in the same parameter, so its validation accepts it again: the row's
  change, which is that validation, is not reached. Not read: a static member; a struct (its default value runs no
  initializer); a site in a field or property initializer (a class's own run before its base class's) or in a
  constructor initializer (`: base(...)` and `: this(...)` arguments run before the base class's initializers); a field
  that is not `static readonly`; a field or method seen through another compilation's symbols (they are compared by
  symbol, so a retargeted one never matches). Gaps, on the side of not flagging: an object made without its constructor
  (`RuntimeHelpers.GetUninitializedObject`, some serializers) and a static field written by reflection, as P2-207 records
  for reflection.
- `equiv-adr` bar test: third row. Both rules apply the valid-path catalogue's existing reading (ADR 0019 is not
  touched: no callee is assumed anything new) to a new value shape; recorded here, as P2-201 and P2-208 recorded theirs.
  #26's, #32's and #41's routes are recorded as not followed, as P2-208 recorded #51's and #8's.
