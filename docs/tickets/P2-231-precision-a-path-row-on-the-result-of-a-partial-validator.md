# P2-231 Precision bug: a path row fires on the result of a member that .NET Framework's own path check already passed
Status: in-progress
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-207

## Goal
Every path row has the one precondition `invalidPath` (a blank path, 248 characters or more, a control character,
one of `" < > | ? *`, or a stray colon), and the one validator is `Path.GetFullPath`, which on .NET Framework rejects
all of it. Some rows' own .NET Framework check rejects less: `Path.Combine` and `Path.GetFileName` check only `" < > |`
and control characters, so their change reaches no other argument. The result of such a member passed that same
check on .NET Framework, so it is a valid path for another row whose check is the same, and not for a `FileStream`.
Two of P2-207's six need this (P2-207's Notes have each trace):
- Git Extensions #38, `FileAssociatedIconProvider::CreateTempFile(string)`: its only caller passes
  `Path.GetFileName(fullPath)` to the flagged `Path.Combine(Path.GetTempPath(), fileName)`; P2-207 follows the caller,
  and `fullPath` is valid by no rule;
- Duplicati #30, `IndexHtmlHandler::Process(...)`: the two `Path.Combine(path, ...)` calls read `GetPath`'s result,
  a `Path.Combine` of a field set from a constructor parameter; and `File.OpenRead(File.Exists(html) ? html : htm)`
  reads `htm`, valid only because `Directory.Exists(path)` guards it, after `htm = Path.Combine(path, "index.htm")` was
  declared: a guard on a variable reaching a local that a transform made from it, which no rule reads either.

When done, a row whose .NET Framework check is narrower than `invalidPath` says so, the members whose result passed
that check are read as valid for it, and #38 is no longer EQ006, or `equiv-adr` records why not.

## Spec references
`docs/tickets/done/P2-188-*.md` (validators and their soundness argument), `P2-207-*.md`, the `precondition` and
`validPaths` header of `src/Equiv.Core/RuntimeChanges/runtime-changes.json`, VERIFICATION-MODEL section 3.

## Acceptance criteria (all must hold; nothing beyond them)
1. Each path row's .NET Framework check is read from the reference source and recorded in the table; a member is a
   validator for a row only when its own check covers that row's.
2. Rerun `gitextensions-8522` and `duplicati-3124` in compare mode quick; Notes give #38's and #30's results and the
   path-row EQ006 count next to P2-207's.

## Tests
Precondition unit tests: a `Path.Combine` of `Path.GetFileName(p)` is not flagged, a `FileStream` of it is; and one
where the path really comes from an argument and stays flagged.

## Size guard
A precondition per character class across every row: stop and route it through `equiv-adr`.

## Out of scope
Device-path prefixes (P2-130's undetermined four).

## Notes
- Filed 2026-10-16 by P2-207 (criterion 1).
