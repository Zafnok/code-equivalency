# P2-232 Precision bug: a path row fires on a valid path a trailing separator, an empty string or a callee's own parameter carries
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-208

## Goal
P2-208 joins a string constant to a valid path. Git Extensions #55, `FormResolveConflicts::Mergetool_Click(object,EventArgs)`,
is still EQ006 on `Directory.SetCurrentDirectory(Module.WorkingDir)`. `GitModule.WorkingDir` is a get-only
auto-property its constructor sets once, which P2-201 reads, to
`(workingDir ?? "").NormalizePath().EnsureTrailingPathSeparator()` (P2-208's Notes have the trace). Three things no
rule reads stand between it and a valid path:
- `EnsureTrailingPathSeparator(this string? dirPath)` is a public extension that returns its own parameter, after
  `dirPath += NativeDirectorySeparatorChar` under a check that it is not empty and does not end in a separator: a
  callee's returned parameter is read only as its caller's argument would be, and P2-188 to P2-207 read a callee's
  body alone;
- the value appended is a `static readonly char` field of the solution set to `Path.DirectorySeparatorChar`, a
  framework field with no C# constant, through a compound assignment, which `LocalValues.Write` gives no value;
- `NormalizePath` returns `string.Empty` for a blank path (and from a `catch`), else `Path.GetFullPath(...)`, so
  `WorkingDir` can be empty. A blank path meets `invalidPath`, though `Directory.SetCurrentDirectory("")` throws
  `ArgumentException` on .NET Framework and on .NET alike: whether an empty string reaches a row's change is a
  property of the row (`Path.GetDirectoryName("")` throws on .NET Framework and returns null on .NET).

Clearing #55 also clears the step P2-228's row 52 (`GetGitDirectory()` reads `WorkingDir`) and P2-218's
`WorkingDir` case wait on, and OpenRA #15's `Platform.SupportDir` (P2-228) appends a separator too.

When done, the largest of these is followed, or `equiv-adr` records why none is.

## Spec references
`docs/tickets/done/P2-208-*.md`, `P2-201-*.md` and `P2-188-*.md`, the `validPaths` header and the `invalidPath`
precondition in `runtime-changes.json`, ADR 0019.

## Acceptance criteria (all must hold; nothing beyond them)
1. Each of the three is assigned a rule or recorded as not followed, and each rule followed has its soundness
   argument in Notes (for the empty string: the rows it does not reach, each with the documented behaviour on both
   runtimes).
2. Rerun `gitextensions-8522` in compare mode quick; Notes give #55's new result and the path-row EQ006 count next
   to P2-208's.

## Tests
Precondition unit tests for each rule followed, and one where the path really comes from an argument and stays
flagged.

## Size guard
A call graph over the solution, or reading a member's writers outside its own type: stop and route it through
`equiv-adr`.

## Out of scope
Device-path prefixes (P2-130's undetermined four). P2-228's fields filled on first use.

## Notes
- Filed 2026-10-16 by P2-208 (criterion 1).
