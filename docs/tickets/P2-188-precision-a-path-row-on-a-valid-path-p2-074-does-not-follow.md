# P2-188 Precision bug: a path row fires on a valid path that reaches the call through a solution member, a field or a caller
Status: in-progress
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
P2-074 (done) stops a path-validation row from firing on a path from a listed source, transform or
guard inside the method, and "a path from a caller or a field is not followed". P2-130's
adjudication (`docs/runs/2026-10-14-migrations-verdict.md`) found 13 false EQ006 in its samples (5 on
`gitextensions-8522`, plus 3 more of P2-047's rows 52, 61 and 62, 7 on `duplicati-3124`, 1 on
`openra-17989`), all a valid path that reaches the call by a route P2-074 does not follow:
- through a solution method or property that only returns a valid path (`Translator.GetTranslationDir()`,
  `GitModule.GitCommonDirectory`, `AppSettings.ApplicationDataPath`, `IndexHtmlHandler.GetPath(Uri)`);
- through a solution helper whose last step is a validating path member on the legacy runtime
  (`SystemIOLinux.NormalizePath` returning `Path.GetFullPath`; `GitModule.WorkingDir`, set through
  `GetFullPath` in the constructor), so the legacy call never sees an invalid path;
- through a field or get-only property set from a source (the test directory, `Path.GetTempPath()`),
  or a property whose writers store only paths a `FileStream` or `File.Move` accepted;
- through the only caller of a private method;
- from `Directory.GetFiles(...)`, which is not a listed source, in a `foreach` variable.

The appendix of the verdict lists every procedure. When done, the largest of these routes is followed.

## Spec references
`docs/tickets/done/P2-074-*.md` and `P2-073-*.md`, the row preconditions in
`src/Equiv.Core/RuntimeChanges/`, ADR 0040.

## Acceptance criteria (all must hold; nothing beyond them)
1. Count the routes over the 13 results and the pairs' other path-row EQ006, and record them.
2. Follow the largest route (at least: a validating path member's result is a valid-path source, also
   through a solution-local method that returns it), with the soundness argument in Notes.
3. Rerun the three migration pairs in compare mode quick; Notes give each listed procedure's new
   result and the path-row EQ006 count next to today's.

## Tests
Precondition unit tests per route followed; a pair where the path really comes from an argument and
stays EQ006.

## Size guard
An interprocedural analysis beyond one call level: stop and route it through `equiv-adr`.

## Out of scope
Device-path prefixes (`\\?\`): the verdict leaves Duplicati's 4 such results undetermined.

## Notes
- Filed 2026-10-14 by P2-130 (criterion 4).
