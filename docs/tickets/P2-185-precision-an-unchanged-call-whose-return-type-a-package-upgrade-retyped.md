# P2-185 Precision bug: an unchanged call whose return type a package upgrade retyped gives two values
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
P2-130's adjudication (`docs/runs/2026-10-14-migrations-verdict.md`) found 2 false EQ002 on
`gitextensions-8522`, in a byte-identical test file: `GitCommandsTests.SshPathLocatorTest::File_system_access_throwing_should_return_empty_string()`
(P2-047's row 26) and `::Find_on_gitBinDir_parent_throwing_should_return_empty_string()`.
`System.IO.Abstractions.DirectoryBase::GetParent(string)` keeps one identity on both sides, but returns
`DirectoryInfoBase` with System.IO.Abstractions 2.0 and `IDirectoryInfo` with 11.0. The result is
converted to `object` and handed to `Throws<Exception>(object)`, so the two sides pass two values of
two sorts and every later call differs. P2-069 (done) paired the rebound `Returns` call (ADR 0042) and
left this case: the callee did not rebind, only its return type changed.

When done, a call site with the same key on both sides whose callee keeps its identity but whose
return type differs is treated as ADR 0042 treats a rebound call (possibly the same call, Unknown),
or decided another way recorded through `equiv-adr`.

## Spec references
ADR 0042 (and its Rejected entry on a callee whose return type alone changes), ADR 0018,
`docs/tickets/done/P2-069-*.md`, P2-166.

## Acceptance criteria (all must hold; nothing beyond them)
1. Decide, through `equiv-adr`, how a call whose identity is unchanged and whose return type differs
   is lowered, given ADR 0042's Rejected entry.
2. A sample pair with a library whose method's return type changes between the sides, called in an
   unchanged body, is not Divergent; one whose body changed is.
3. Rerun `gitextensions-8522` in compare mode quick; Notes give the two procedures' new results.

## Tests
Unit tests for the rule, and the sample pair.

## Size guard
A change to how a rebound callee identity is found: stop, that is ADR 0042's key.

## Out of scope
A retyped field (P2-166).

## Notes
- Filed 2026-10-14 by P2-130 (criterion 4).
