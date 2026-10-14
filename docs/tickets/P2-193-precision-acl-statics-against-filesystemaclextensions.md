# P2-193 Precision bug: `File.GetAccessControl(path)` and its siblings are Divergent from the `FileSystemAclExtensions` call that replaces them
Status: todo
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
.NET Core removed the static ACL members of `File` and `Directory`; their documented successor is
`new FileInfo(p).GetAccessControl()` (and the `DirectoryInfo` and `Set` forms) from
`System.IO.FileSystemAclExtensions`. P2-130's adjudication of `duplicati-3124`
(`docs/runs/2026-10-14-migrations-verdict.md`) found 4 false EQ002, the four
`Duplicati.Library.Common.IO.SystemIOWindows::Get/SetAccessControlFile/Dir` methods, which make exactly
that replacement over the same full path.

An ADR 0020 adapter lists argument positions and constants; it cannot wrap an argument in a
constructor. When done, the four pairs have catalogue entries and the adapter form they need, or a
recorded reason why not.

## Spec references
ADR 0020, `api-equivalences.json`, `ApiEquivalenceTable`, the .NET documentation of the four members.

## Acceptance criteria (all must hold; nothing beyond them)
1. Decide, with the `equiv-adr` bar test, whether an adapter may wrap an argument in a constructor
   that only allocates (`FileInfo(string)` validates its path: record how that is handled).
2. Add the four entries, or record why not.
3. Rerun `duplicati-3124` in compare mode quick; Notes give the four procedures' new results.

## Tests
Catalogue table tests; a lowering test of the wrapped argument; a sample pair.

## Size guard
Adapters that build anything but one constructor call: stop.

## Out of scope
Other removed .NET Framework APIs.

## Notes
- Filed 2026-10-14 by P2-130 (criterion 4).
