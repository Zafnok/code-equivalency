# P2-211 Precision: the two `StringBuilder` append results P2-164 leaves on `gitextensions-9860`
Status: done (PR #521)
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-164

## Goal
P2-164 lowers a handler-bound `StringBuilder.Append($"...")` or `AppendLine($"...")` to the call of the `string`
member with the joined parts, where no hole can tell the handler's piece-by-piece writes from one write
(`docs/tickets/done/P2-164-*.md`, VERIFICATION-MODEL section 3). By reading the source, two of the results P2-124
found on `gitextensions-9860` (`docs/runs/2026-10-12-upgrade-verdict.md`) are not taken:

- `GitCommands.Patches.PatchManager::ToPatch(...)`, EQ002: `result.Append($"\n--\n{Application.ProductName} {AppSettings.ProductVersion}")`.
  The holes are static getters and the builder is a local made by `new()`, but it is handed to `chunk.ToPatch(subChunkToPatch, result)`
  before the append, so code a hole runs might reach it.
- `GitCommandsTests.Git.GitBlameCommitTests::TestToString()`, EQ006 in thorough mode from the IL pass, under the
  row `System.Text.StringBuilder::AppendLine(ref `: `str.AppendLine($"Author date: {authorTime}")` and the same with
  `committerTime`, two `DateTime` holes, which P2-086 and P2-139 do not cover, so the handler's calls stay, the pair
  stays Unknown and the IL pass fires the row. No hole touches the builder.

First rerun the pair in compare mode, quick and thorough, to confirm which of P2-124's five EQ002 and the one EQ006 are left.
Then make what is left Equivalent where it is, or record why it is not.

## Spec references
VERIFICATION-MODEL section 3 (the interpolated string and `StringBuilder` paragraphs), ADR 0049, ADR 0040 (runtime
rows), `docs/tickets/done/P2-164-*.md`, `docs/tickets/done/P2-139-*.md` (its table: "A `DateTime` hole", no owner).

## Acceptance criteria (all must hold; nothing beyond them)
1. The rerun's counts, before any code, in `## Notes`: P2-124's five EQ002 and one EQ006, each now Equivalent, Unknown or still Divergent.
2. Each one still Divergent is Equivalent, or `## Notes` says why it cannot be with no new assumption, decided with `equiv-decide`.

## Tests
A unit test per shape taken; the oracle where a lowering changes.

## Out of scope
Format and alignment clauses. Holes of other types than the one the rerun points at.

## Notes
- Filed 2026-10-15 by P2-164, from reading the modern side's source (`37797ea4dd74`), not from a rerun.
- **Rerun, quick (criterion 1).** equiv `e48ae839` (main, P2-164 in), `gitextensions-9860` in `full` mode, compare mode
  quick, 281 s, exit 1: EQ001 13861, EQ002 6, EQ003 226, EQ004 2, EQ005 2. Of P2-124's five EQ002:
  `GetSelectedLinesAsNewPatch` EQ001 (`bounded`), `Chunk::ToPatch` EQ001 (`lockstep-induction`),
  `ErrorReportUrlBuilder::Build` EQ001 (`bounded`), `SerializableException::ToString()` EQ001 (`lockstep-induction`),
  and `PatchManager::ToPatch(...)` still EQ002. `GitBlameCommitTests::TestToString()` is EQ003 (`opaque`: old
  `InterpolatedString`, new `ref-argument`), as quick runs no IL pass. The other five EQ002 are the five behaviour changes
  P2-124 confirmed (`GitExtensions.Program::Main()`, `TranslationSerializer::Serialize`, `BugReporter.Program::Main()`,
  `GourcePlugin::DownloadFile` and `::SearchForGourceUrl()`), none an append.
- **Rerun, thorough (criterion 1).** Same commit, compare mode thorough, 5198 s, exit 1: EQ001 13864, EQ002 19, EQ003
  208, EQ004 2, EQ005 2, EQ006 2. The five appends as in quick: four EQ001, `PatchManager::ToPatch(...)` EQ002 from the
  first pass. `GitBlameCommitTests::TestToString()` is still EQ006, `decidedBy: il-pass`, row
  `System.Text.StringBuilder::AppendLine(ref ` (`changedIn` net6.0). So of P2-124's six: four Equivalent, one still
  EQ002, one still EQ006, none Unknown. The 13 other EQ002 the IL pass decides are P2-135's binding-only differences;
  the other EQ006 is P2-124's undetermined `Environment::get_ProcessorCount(`.
- Decision: `PatchManager.ToPatch`'s builder, handed to `chunk.ToPatch(subChunkToPatch, result)` before the append -> a
  use of the builder may also be an argument, passed by value to a parameter of the builder's type, of a call of an
  ordinary method declared in the body's own compilation that is not virtual, abstract, an override, extern, async or
  an iterator, whose every use of that parameter is in turn such a use (the same rule, read in the callee's body, a
  callee already being read counting as kept): the callee then hands the builder to no code outside the bodies read
  and cannot return it, so no hole reaches it. `Chunk.ToPatch` only appends to it. Alternatives: record it as not
  provable (the rule today; sound, takes nothing); trust any callee not to keep the builder (an assumption about
  unread code). Rule: 3 (each condition is one a unit test pins). No new assumption: the callee's body is read, not
  assumed, and non-virtual means the body read is the body that runs.
- Decision: a partial method -> read in the part with the body (`PartialImplementationPart`); one with no body is read
  as a body with no use, since the compiler drops its calls. Alternatives: never take a partial method. Rule: 1 (what
  Roslyn binds the call to is the definition part, and the body that runs is the implementation part's).
- Decision: the oracle (`LoweringOracleTests`) -> unchanged. A taken append lowers to the IR P2-164 already gives it, so
  no lowering changes; only which appends are taken does, and `BothBindingsMakeTheSameCalls` runs both bindings of each
  new taken shape through the interpreter. Alternatives: a generated helper method in `LoweringOracleGen` to hand the
  builder to (a second member per case in the generator). Rule: 4.
- Decision (`equiv-adr` bar test): no IR node, no verdict's meaning, no line of VERIFICATION-MODEL section 1 -> the
  P2-164 lowering rule widened, in section 3 and `IOPERATION-COVERAGE.md`'s `InterpolatedStringHandlerCreation` row.
- **Criterion 2, `PatchManager.ToPatch`.** After the change, quick, same pair, 299 s: EQ001 13862, EQ002 5, EQ003 226,
  EQ004 2, EQ005 2. `PatchManager::ToPatch(...)` is EQ001 (`lockstep-induction`); compared result by result with the
  quick run before the change, it is the only one that moved.
- Decision: `GitBlameCommitTests.TestToString()`'s EQ006 -> stays, recorded here and filed as P2-226. Equivalent would
  need its two `DateTime` holes joined: that the text `string.Format` makes of a boxed `DateTime` on net5.0 is the text
  `AppendFormatted<DateTime>` makes on net6.0, with the current culture and no format. That is a claim about two
  runtimes' date formatting and culture data, which no catalogue entry or reading of the body gives, so it is a new
  assumption (P2-139's table leaves the `DateTime` hole with no owner, and this ticket's Out of scope keeps it there).
  What can go without one is the EQ006 itself: its builder is a local made by `new()`, used only as a receiver, in no
  `try`, so by P2-164's rule no hole reaches it; but the row fires in the IL pass, which applies no row suppression and
  no `requires` fact (`IlLowerer` passes none; P2-114's Notes), so the fix is a `requires` fact for the two rows read
  in the IL pass too. That would leave the pair Unknown, not Equivalent, and is outside this ticket's criteria, so it
  is P2-226. Alternatives: do it here (beyond "nothing beyond them"); add a `DateTime` formatter to the join (the
  assumption above). Rule: 4.
