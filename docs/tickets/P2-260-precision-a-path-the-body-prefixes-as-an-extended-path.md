# P2-260 Precision bug: a path the body prefixes with `\\?\` meets the invalid-path-character rows, which neither runtime applies to an extended path
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
Duplicati's `SystemIOWindows` passes every path through `PrefixWithUNC`, which makes it an extended `\\?\` path (or
`\\?\UNC\`). .NET Framework 4.6.2 and later, with `UseLegacyPathHandling` off (Duplicati's hosts target 4.7.1 and set
it off in their `app.config`), checks no character of an extended path, and .NET 5 checks none either, so the
"rejects invalid path characters" rows cannot fire on such a call: a path holding `|` reaches the file system on both
runtimes and fails there the same way. P2-247 (`docs/runs/2026-10-20-precision-execute-verdict-2.md`) adjudicated 10
Divergent results of `duplicati-3124` that rest on those rows, all false positives:
- EQ006 (observed by a replay the legacy driver's legacy path handling made, P2-259): `DirectoryCreate(string)`,
  `DirectoryDelete(string,bool)`, `FileDelete(string)`, `FileCreate(string)`, `GetFileAttributes(string)`,
  `PathGetFullPath(string)`, and the exposure `PathChangeExtension(string,string)` of P2-212's sample (the last two
  were P2-237 confirmations);
- EQ002: `SetAccessControlFile(string,FileSecurity)`, `SetAccessControlDir(string,DirectorySecurity)`,
  `GetAccessControlFile(string)`, `GetAccessControlDir(string)` (P2-193's four: their model has the legacy static ACL
  member reject the path where `FileSystemAclExtensions` does not; on an extended path neither validates).
When done, a path argument the body built by prefixing `\\?\` (a constant prefix concatenated, or a solution helper that
returns one on every path it returns) is not an invalid-path-character witness on either runtime, unless the pair's
project sets `UseLegacyPathHandling` on.

## Spec references
`src/Equiv.Core/RuntimeChanges/runtime-changes.json` (the path-validation rows and their preconditions),
`RowPreconditions`, `ValidPaths` (P2-074), tickets P2-193, P2-259, `docs/runs/2026-10-20-precision-execute-verdict-2.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. A unit test: a call of a path row on `"\\\\?\\" + p` (and on the result of a helper whose every return is so
   prefixed) does not fire the invalid-path-character row; the same call on `p` still does.
2. A `samples/` pair where an unedited helper prefixes `\\?\` and calls `File.Delete` is EQ001, not EQ007.
3. `duplicati-3124` in `full`: each of the eleven procedures above is given with its result now, in Notes.

## Notes
- Filed 2026-10-20 by P2-247. The harness half (the legacy driver's legacy path handling) is P2-259.
- Measured outside equiv on .NET Framework 4.8 by P2-247's Duplicati sub-agent, with a program built for 4.7.1 and
  `UseLegacyPathHandling=false`: a `\\?\` path holding `|` returns from `GetFullPath` and `GetFileName` and throws
  `IOException` from `CreateDirectory`, `Delete` and `Create`; an ordinary path holding `|` throws `ArgumentException`.
