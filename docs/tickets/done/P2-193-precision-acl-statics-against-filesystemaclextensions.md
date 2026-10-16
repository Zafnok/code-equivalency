# P2-193 Precision bug: `File.GetAccessControl(path)` and its siblings are Divergent from the `FileSystemAclExtensions` call that replaces them
Status: in-progress
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
- Criterion 1 (bar test, `equiv-adr`): the first row. ADR 0020 already decides it (the two sides must throw the same
  exception type; an equivalence that needs a further precondition is not an entry), so it is a dated Clarification on
  ADR 0020, not a new ADR. Outcome: an adapter item that wraps an argument in one constructor call would count the
  constructor's exceptions as part of the adapted call; a constructor that only allocates meets the condition by
  itself, and `FileInfo(string)` and `DirectoryInfo(string)` do not, because they validate the path before the legacy
  member's own first check. How the validation is handled: it is the precondition, and it is not shown at these calls.
- Measured on .NET Framework 4.8.9363 (Windows PowerShell 5.1, legacy path handling), `File.GetAccessControl(p)`
  against `new FileInfo(p).GetAccessControl()`, and the `Directory`/`DirectoryInfo` forms. .NET Framework is the one
  runtime with both members (the `System.IO.FileSystem.AccessControl` package's `FileSystemAclExtensions` there calls
  the `FileInfo` instance member). Same result or same exception type: `""`, `" "`, `a|b`, `C:\a*b`, a tab, the `\\?\`
  forms, `CON`, a missing directory, a relative name, `C:`, a trailing space, `C:\Windows`, an existing file,
  `file::$DATA`. Different exception type: a missing alternate data stream (`FileNotFoundException` /
  `DirectoryNotFoundException` against `NotSupportedException`), a lone `:` and `http://x/y` (`ArgumentException`
  against `NotSupportedException`), a 303-character path (`ArgumentException` against `PathTooLongException`).
  `File.SetAccessControl(p, null)` against `new FileInfo(p).SetAccessControl(null)`, and the `Directory` forms: a
  missing file gives `ArgumentNullException` on both; `""`, `a|b`, a missing stream, `:` and a long path give
  `ArgumentNullException` from the legacy member (security object checked first) against the path's exception from
  the constructor. On every path the constructor accepts, the two agree.
- Criterion 2: no entries. Each of the four needs "a path .NET Framework's validation accepts", which
  `ValidPaths` (P2-074) could show at a call, but Duplicati's four methods pass `PrefixWithUNC(path)` of their own
  parameter, a path from the caller, which `ValidPaths` does not follow. An entry gated on `ValidPaths` would decide
  none of the calls that asked for it, so it was not built. The four are Divergent on the malformed paths above, so
  P2-130's "false positive" for rows 1, 4, 6 and 10 of `docs/runs/2026-10-14-migrations-verdict.md` is, under
  VERIFICATION-MODEL section 1 (the exception type is observable), a real difference on malformed paths; the run
  record is history and is left as written.
- Decision: no catalogue table test, wrapped-argument lowering test or sample pair, since no entry and no adapter form
  was added; the Tests list belongs to the entries. Recorded instead: the measurements above.
- Criterion 3: `corpus.ps1 -Compare duplicati-3124 -Mode quick` on this branch (`src/` as `main` at 068d8d79), exit 1
  after 253 s. `SystemIOWindows::GetAccessControlDir(string)`, `GetAccessControlFile(string)`,
  `SetAccessControlFile(string,FileSecurity)` and `SetAccessControlDir(string,DirectorySecurity)`: EQ002 each, as in
  the 2026-10-15 quick run (P2-217); the pair's EQ002 count is 13 in both. Their callers `GetMetadata` and
  `SetMetadata` stay EQ001.
