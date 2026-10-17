# P2-236 Precision bug: a path row fires on a local made from a variable before a guard on that variable passed
Status: todo
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-231

## Goal
P2-074's guard rule reads a variable nothing assigns where `File.Exists` or `Directory.Exists` has returned true for it.
A local declared from that variable before the guard, by a transform with plain constants, holds a valid path once the
guard has passed on the variable: the variable held one value throughout, so the local was made from a path the guard
accepts. No rule reads this. Duplicati #30, `IndexHtmlHandler::Process(...)`, needs it (P2-207's and P2-231's Notes
have the trace): `htm = Path.Combine(path, "index.htm")` is declared before
`if (Directory.Exists(path) && (File.Exists(html) || File.Exists(htm)))`, and `File.OpenRead(File.Exists(html) ? html : htm)`
in that `if` reads `htm`. P2-231 cleared the procedure's two `Path.Combine` rows; its result is now Unknown
(`abstraction`) with `File.OpenRead(string)` as its `runtimeChangedCall`.

When done, such a local is read as a valid path under its source variable's guard, or `equiv-adr` records why not.

## Spec references
`docs/tickets/done/P2-074-*.md` (guards), `P2-207-*.md` (the last-write rule) and `P2-231-*.md`, the `validPaths`
header of `runtime-changes.json`.

## Acceptance criteria (all must hold; nothing beyond them)
1. The rule and its soundness argument are in Notes: what the local may be made by, and what may not run between its
   declaration and the guard.
2. Rerun `duplicati-3124` in compare mode quick; Notes give #30's result and the path-row EQ006 count next to P2-231's.

## Tests
Precondition unit tests: the #30 shape is not flagged; a local made from the variable after it is reassigned, or made
by a member that is no transform, stays flagged.

## Size guard
A local made from another local made from the variable (a chain): stop, that is not this ticket.

## Out of scope
Device-path prefixes (P2-130's undetermined four).

## Notes
- Filed 2026-10-17 by P2-231 (criterion 2).
