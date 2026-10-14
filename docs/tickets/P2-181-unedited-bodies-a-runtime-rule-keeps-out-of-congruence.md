# P2-181 An unedited body that a runtime rule keeps out of congruence is proved where the rule's change cannot be reached
Status: in-progress
Effort: L
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
On a pair that crosses a runtime, a body whose two texts are equal is still not congruent when it
calls a runtime-sensitive member (ADR 0040; `properties.notCongruent` component `runtime-rule`, ticket
P2-173). The solver then has to decide it. P2-130's three migration runs (equiv `e98fd1b9`, compare
mode quick, `docs/runs/2026-10-14-migrations-verdict.md`) show it almost never does:

| Pair | Changed pairs | `runtime-rule` | proved | EQ006 | Unknown (abstraction, timeout, opaque, other) |
|---|---|---|---|---|---|
| gitextensions-8522 | 938 | 493 (52.6%) | 4 | 150 | 339 (201, 76, 54, 8) |
| duplicati-3124 | 707 | 269 (38.0%) | 1 | 122 | 146 (66, 62, 13, 5) |
| openra-17989 | 321 | 242 (75.4%) | 0 | 47 | 195 (81, 85, 22, 7) |
| All three | 1,966 | 1,004 (51.1%) | 5 | 319 | 680 (348, 223, 89, 20) |

So half the migrations' changed pairs are code nobody edited, and they are 680 of the 1,222 Unknown
results (55.6%) and 319 of the 411 Divergent (77.6%). It is the largest cause of both of the
scoreboard's weak rows, and no ticket owns it: P2-073 to P2-075 narrowed when a row fires EQ006, not
what happens to a body after it is taken out of congruence, and P2-173 left pairs that cross a runtime
out.

When done, an unedited body whose runtime-sensitive calls cannot reach their rows' documented change
(the same preconditions P2-073 and P2-074 use for EQ006) is Equivalent, and one whose calls can is
Unknown or EQ006 as today.

## Spec references
ADR 0040, ADR 0008, VERIFICATION-MODEL section 3 (runtime-changed members) and the `notCongruent`
table, `src/Equiv.Core/RuntimeChanges/runtime-changes.json` and its preconditions,
`docs/tickets/done/P2-073-*.md`, `P2-074-*.md`, `P2-113-*.md`, `P2-173-*.md`; `.claude/skills/equiv-adr`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Count, over the three migration pairs at `main`'s head, the 1,004 `runtime-rule` pairs by the row
   or rows their body reaches, and for each Unknown the runtime-sensitive function its candidate
   depends on. Notes hold the top rows and functions with counts.
2. Decide, through `equiv-adr`, when a body with equal texts whose runtime-sensitive calls cannot reach
   a row's change may be Equivalent (for example: congruent when every such call's row precondition is
   false by its arguments, or the two sides' functions shared where the precondition is false), and
   why that cannot hide a reachable change.
3. Implement it, with unit tests and a sample pair whose unedited body calls a path row with a
   constant path and another with a parameter.
4. Rerun the three migration pairs in compare mode quick and record, in Notes, the `runtime-rule`
   pairs by outcome next to the table above, and that every result that was EQ006 is still EQ006 or
   has a documented precondition that its arguments make false.

## Tests
As criterion 3; a soundness test in which the change is reachable and the pair is not Equivalent.

## Size guard
New runtime rows: stop, that is catalogue work.

## Out of scope
`extern` methods (P2-165). Pairs on one runtime.

## Notes
- Filed 2026-10-14 by P2-130 from its three migration runs: the largest unowned cause of Unknown and
  of Divergent results on the scoreboard's large pairs.
- Decision: to count the 1,004 by row (criterion 1), `properties.notCongruent.identity` of a
  `runtime-rule` result now names every rule that applies, each once, the legacy side's first, joined
  by `, `: the callee, field or event a `runtime-changes.json` row flags (the identity of the call, which
  matches the row by prefix), or `x87`, `float-to-integer` or `extern`. It named the side or sides
  before; with equal texts and one interval both sides have the same rules but `x87`. Alternatives: a
  throwaway instrumented build (the count could not be repeated by the next ticket), the row's
  `member` prefix (needs a second match, and a reviewer reads a callee more easily). Rule: 1 (mirrors
  the `callee` component). It changes the content of a SARIF property: `Release: minor`, as P2-173's
  property had.
- Criterion 1, the runs: 2026-10-14, equiv `c6d5f751` (`main`'s head `31cc1c09` plus the identity
  change, which changes no verdict), compare mode quick, `--jobs 4`, one after the other, on the
  checkouts P2-130 fetched and restored: `gitextensions-8522` 528 s, `duplicati-3124` 869 s,
  `openra-17989` 643 s, each exit 1. The `runtime-rule` results are 493, 269 and 242, 1,004 in all, with
  P2-130's outcomes to the result (abstraction 348, EQ006 319, timeout 223, opaque 89,
  unaligned-loop 19, recursion 1, proved 5). 745 bodies reach one row, 190 two, 69 three or more.
- Criterion 1, bodies by the row or rule they reach (a body counts once under each; all three pairs):

  | Row or rule | Bodies | EQ006 | Unknown (abstraction, timeout, opaque, other) | Proved | Precondition |
  |---|---|---|---|---|---|
  | `Path::Combine(string,string)` | 181 | 61 | 120 (38, 67, 12, 3) | 0 | `invalidPath` |
  | `String::StartsWith(` | 106 | 19 | 87 (38, 23, 20, 6) | 0 | `cultureSensitiveText` |
  | `float-to-integer` (OpenRA only) | 69 | 0 | 69 (26, 31, 8, 4) | 0 | built-in rule |
  | `String::IndexOf(` | 52 | 5 | 47 (35, 6, 5, 1) | 0 | `cultureSensitiveText` |
  | `String::Split(char[],StringSplitOptions)` | 51 | 9 | 42 (27, 10, 2, 3) | 0 | none (now `trimEntries`) |
  | `File::Delete(string)` | 50 | 11 | 39 (11, 24, 3, 1) | 0 | `invalidPath` |
  | `File::OpenRead(string)` | 45 | 24 | 21 (5, 13, 2, 1) | 0 | `invalidPath` |
  | `Directory::CreateDirectory(string)` | 42 | 12 | 30 (6, 20, 3, 1) | 0 | `invalidPath` |
  | `String::EndsWith(` | 42 | 6 | 36 (26, 3, 7, 0) | 0 | `cultureSensitiveText` |
  | `Encoding::GetString(` | 37 | 12 | 25 (13, 8, 4, 0) | 0 | none |
  | `Path::GetDirectoryName(string)` | 37 | 6 | 31 (13, 8, 8, 2) | 0 | `invalidPath` |
  | `Path::GetFileName(string)` | 34 | 6 | 28 (8, 12, 6, 2) | 0 | `invalidPath` |
  | `Process::Start(` | 29 | 12 | 16 (15, 1, 0, 0) | 1 | none |
  | `File::Create(string)` | 27 | 21 | 6 (3, 2, 0, 1) | 0 | `invalidPath` |
  | `String::Split(string[],StringSplitOptions)` | 24 | 3 | 21 (8, 7, 5, 1) | 0 | none (now `trimEntries`) |
  | `Environment::get_OSVersion(` | 23 | 12 | 11 (3, 4, 4, 0) | 0 | none |
  | `Environment::GetFolderPath(SpecialFolder)` | 22 | 12 | 10 (3, 6, 1, 0) | 0 | none |

  Then, under 22 each: `String::Equals(string,StringComparison)` 21, `Path::GetFileNameWithoutExtension`
  21, `Math::Max(float,float)` 21, `File::ReadAllText(string)` 20, `Math::Min(float,float)` 19,
  `Path::GetFullPath` 17, `FieldInfo::SetValue(` 17, `ProcessStartInfo::get_UseShellExecute(` 15,
  `Encoding::get_Default(` 15. By family (a body counted once per row): 30 path rows 579, the 8 ICU
  rows 253, the two `Split` rows 75, the `Regex` row 2, and 64 rows and rules with no precondition 457
  (`float-to-integer` 69 of them).
- Criterion 1, what each Unknown's candidate depends on: 348 of the 680 Unknowns are `abstraction`.
  In 347 of them every entry of `properties.abstractions` is a function both sides share, never the
  runtime-changed member: `delegate` 130, `String::Concat(string,string)` 68, `String::IsNullOrEmpty`
  51, `String::get_Length` 37, `String::IsNullOrWhiteSpace` 33, `String::Substring(int,int)` 33,
  `String::Substring(int)` 30, `conv.f32.i32` 21, `String::Trim` 20, `f32.mul` 19. The two texts are
  equal, so a shared function sees different arguments only because a runtime-changed call upstream gave
  each side its own result: that call, named by `notCongruent`, is the function each candidate depends
  on, and the result does not say so. New ticket P2-195. The 223 `timeout`, 89 `opaque` and 20 other
  Unknowns have no candidate.
- Criterion 1, the sample read by hand (`gitextensions-8522`, drawn with `random.Random(181)`: 15
  `StartsWith` bodies, 15 `Path.Combine`, 10 `Split`, 8 `IndexOf`): every `StartsWith` call has a
  receiver that is no constant (a line of git output, a parameter), and an argument that is no constant
  or has a character that is no ASCII letter or digit (`"push "`, `"+++"`, `"@"`); every `Path.Combine`
  has a first argument that is a parameter, a field, a property or the result of one of the solution's
  methods (`GetGitDirectory()`, `AppSettings.GetInstallDir()`); all 10 `Split` bodies pass the constant
  `RemoveEmptyEntries` or `None`; 4 of the 8 `IndexOf` bodies search only for a `char` (`IndexOf('/')`,
  `IndexOf('"')`). Under P2-073's rules, which read only the method, the first two families can reach
  their change.
- Criterion 1, a cause outside the rows: OpenRA's 69 `float-to-integer` bodies are on a net472 to
  net5.0 pair, which does not cross .NET 9. Four of its modern projects are `netstandard2.1` mods the
  game loads at run time, which `run.properties.runtimes` gives source `unhosted`, so ADR 0040 decision
  1 has them cross every change the table covers. New ticket P2-194.
- Criterion 2, decided with `equiv-adr` (bar test row 1: P2-073's rule already decides it, and this
  applies it to more calls): no new ADR. A body with equal texts is congruent exactly when none of its
  calls can reach an applicable row's change, and "cannot reach" stays what P2-073 and P2-074 made it:
  a property of the call read in the method that makes it, the same in the bound fingerprint and in
  the lowering (`RuntimeChangeReach`, one place). This ticket widens it by the two cases the count shows
  are both common and decidable from the call alone:
  - `cultureSensitiveText` is never met by a search for a character: a call whose every argument but
    the receiver is a `char` or an `int` (`IndexOf(char)`, `IndexOf(char,int)`, `LastIndexOf(char,int,int)`).
    Why it cannot hide a change: those overloads are documented as ordinal on .NET Framework and on
    .NET, and the ICU switch changed only the culture-sensitive overloads, which the row's own reason
    names. A `char` overload with a `StringComparison` (on .NET only) is read as before.
  - The two `Split` rows with a `StringSplitOptions` get the precondition `trimEntries`, met by any
    options but `None` (0) and `RemoveEmptyEntries` (1). Why it cannot hide a change: .NET Framework
    throws for every other value and accepts those two, so the documented change (TrimEntries, 2) and
    any undefined value are met, and options that are no constant reach the row as before.
- Decision: the solver is left as it is. The ticket's other example, sharing the two sides' functions
  where a precondition is false, would need the precondition on the solver's terms, and a string is an
  uninterpreted sort there, so `invalidPath` and `cultureSensitiveText` cannot be stated of a value the
  method does not fix; where the method fixes it (a constant, a valid-path source), the call is already
  not runtime-changed and shares its functions. Alternatives: a shared Boolean `reach:<row>(args)` that
  guards the per-side functions (no constraint would ever make it false that the frontend does not
  already apply), an SSA constant propagation in the lowering (the sample found no constant that
  reaches a call through a local). Rule: 4.
- Decision: the character search is a fact of the call's operands, `CallArgument.IsCharOrIndex` (a
  `char` or `int` parameter), set by the frontend; Core decides. Alternatives: excluding the `char`
  overloads by row `member` (the rows are prefixes, and splitting them would add rows, which the size
  guard forbids), a parameter type name on every operand (more than the rule reads). Rule: 4.
- Decision: the sample is `samples/runtime-row-unreachable-change`, three calls that cannot reach (a
  path literal, `IndexOf('/')`, `Split` with `RemoveEmptyEntries`) and the same three with a parameter
  in place of the constant, which stay EQ006: the soundness test. The ticket's two path calls are
  `Cache` and `Under`.
- Overlap: P2-189 criterion 1 (the `Split` rows) and the `IndexOf(char)` case of P2-190 are done here,
  because the count put them in 75 and 52 of the 1,004. Both tickets' texts now say so; their reruns
  still report their procedures.
- The rows' change is still reachable by most of the 1,004 under a per-method reading: a path from a
  caller or a field (P2-188 owns those P2-074 does not follow) and culture-sensitive text whose
  receiver is not a constant. No rule here decides them, and none was added (size guard).
- Criterion 4, the reruns: 2026-10-14, equiv `408a972c`, compare mode quick, `--jobs 4`, on the same
  checkouts: `gitextensions-8522` 692 s, `duplicati-3124` 634 s, `openra-17989` 351 s, each exit 1. The
  box ran this ticket's tests during both sets of runs, so neither set's times compare with P2-130's.
  The 1,004 `runtime-rule` pairs of criterion 1, by what each is now, next to the ticket's table:

  | Pair | `runtime-rule` before | congruent now | proved | EQ006 | Unknown (abstraction, timeout, opaque, other) | still `runtime-rule` |
  |---|---|---|---|---|---|---|
  | gitextensions-8522 | 493 | 42 | 4 | 143 (was 150) | 304 (178, 70, 50, 6) | 451 |
  | duplicati-3124 | 269 | 32 | 1 | 118 (was 122) | 118 (50, 54, 11, 3) | 237 |
  | openra-17989 | 242 | 15 | 0 | 44 (was 47) | 183 (73, 81, 22, 7) | 227 |
  | All three | 1,004 | 89 | 5 | 305 (was 319) | 605 (was 680: 301, 205, 83, 16) | 915 |

  Over every matched pair the three runs have 18 fewer EQ006 (356 to 338), 89 more congruent pairs and
  3 more solver proofs; EQ002 is unchanged (55).
- Criterion 4, every result that was EQ006: 19 are not EQ006 now, and each cited a row this ticket
  narrowed, every call to which in the body now has arguments that cannot reach it:
  - `Split` with constant options `None` or `RemoveEmptyEntries` (`trimEntries`): 16. Congruent:
    `CommitAutoCompleteProvider::ReadOrInitializeAutoCompleteRegexes()`, `GitModule::GetMergedBranchesAsync`,
    `AppSettings::get_ThemeVariations()`, `LazyStringSplitTests::None`, `LazyStringSplitTests::RemoveEmptyEntries`,
    `SerializableExceptionTests::Sanitize`, `Duplicati.CommandLine.Help::.cctor()`,
    `MicrosoftGraphBackend::CreateFolder()`, `AutoUpdateSettings::get_URLs()`, `OpenRA.MiniYaml::FromString`,
    `VoxelModelCache::LoadVoxel`, `LabelWithHighlightWidget::MakeComponents`. Proved (bounded):
    `GoogleDrive::GetFolderId`. Unknown: `FilterGroups::ParseFilterList` (opaque), `SendMail::SendMessage`
    and `BoxBackend::GetCurrentFolder` (timeout). The last four are edited bodies whose `Split` call the
    solver now shares between the sides.
  - a search for a character (`cultureSensitiveText`): 3. Congruent: `ConfigFile::FindKeyIndex`
    (`LastIndexOf(char)`), `WordDictionary::VerifyAffixKey` and `ExtensionMethods::GetPrintableCommandText`
    (`IndexOf(char)`).
  One result became EQ006: `PatchManager::CorrectHeaderForNewFile(string)`, an unedited body that was
  Unknown (`abstraction`); its `IndexOf(char)` calls are shared now and its `StartsWith("+++")` row, which
  can still reach, gives the solver a counterexample. Two more results moved without an EQ006:
  `BuildServerIntegrationSettingsPage::CreateBuildServerSettingsUserControl()` and
  `CommandLineBuilder::VerifyThrowNoEmbeddedDoubleQuotes`, both edited, from `abstraction` to proved
  (their `Split(..., RemoveEmptyEntries)` and `IndexOf('"')` are shared); three Duplicati Unknowns changed
  reason (`Options::get_Version()` unaligned-loop to timeout, `ReadOptionsFromFile` and
  `MegaBackend::GetCurrentFolder` abstraction to opaque).
- What is left: 915 of the 1,004 are still kept out by a rule their calls can reach as the method
  reads them. The largest are the path rows with a path from a caller, a field or the solution's own
  method (P2-188), culture-sensitive text with a receiver that is no constant (no ticket: P2-073 decided
  that such a receiver reaches the ICU rows), OpenRA's 69 `float-to-integer` bodies (P2-194), and rows
  with no precondition (`Encoding.GetString`, `Process.Start`, `OSVersion`, `GetFolderPath`; P2-189 and
  P2-190 own the cases their adjudication found).
