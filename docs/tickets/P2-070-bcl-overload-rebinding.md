# P2-070 Identical source that binds to a different BCL overload is the same call
Status: in-progress
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-047

## Goal
P2-047's audit found 3 false Divergents where the source is identical, but the modern reference
assemblies add an overload or move a member. `s.TrimEnd('/')` binds to `TrimEnd(params char[])`
on .NET Framework and to `TrimEnd(char)` on .NET. `s.TrimStart()` binds to `TrimStart(params char[])`
with an empty array, then to the new `TrimStart()`. `d.FullName` binds to `DirectoryInfo::get_FullName`,
then to `FileSystemInfo::get_FullName`. Each side calls an unrelated uninterpreted function. Minimal
repro, as a sample pair (the same file on both sides, the legacy project targeting net48):

```csharp
static string Strip(string s) => s.TrimEnd('/');
static int Indent(string s) => s.Length - s.TrimStart().Length;
```

Today this is EQ002. It should be Equivalent. Add the rebinding forms to the shipped equivalence
table (VERIFICATION-MODEL section 3), with the argument adapter each needs, so that the legacy call
is rewritten to the modern one.

## Spec references
VERIFICATION-MODEL section 3 (the equivalence table, `properties.equivalencesApplied`), P2-047's audit.

## Acceptance criteria (all must hold; nothing beyond them)
1. `samples/bcl-overload-rebinding` (the pair above, plus `DirectoryInfo.FullName`) is Equivalent,
   with each entry listed in `properties.equivalencesApplied`.
2. A variant that trims a different character on the modern side stays Divergent.

## Tests
- Integration test on `samples/bcl-overload-rebinding`.
- Unit tests for each new table entry and its adapter.

## Out of scope
Rebinding in user or third-party libraries (P2-069). Overloads that change behaviour.

## Notes
- Found by P2-047: 2 on Git Extensions, 1 on the Tomas pair.
- ADR 0042 (P2-069): once P2-069 lands, the Goal's pair is EQ003 with `properties.reboundCalls`
  naming each overload pair, not EQ002. A catalogue entry rewrites the legacy call before call sites
  are compared, so each entry this ticket adds takes its site out of the rebound pairs.
- Decision: three entries, `bcl.string-trim-end-one-char`, `bcl.string-trim-start-no-chars` and
  `bcl.directory-info-full-name`, exactly the three forms the audit found. `Trim` and the other
  element counts are not added (nothing beyond the criteria); each is one more row when a run finds it.
- Decision: the catalogue was applied to invocations only, and `d.FullName` is a property read. An
  entry now also rewrites an accessor call (getter, setter) when its adapter passes every operand
  through in order and unchanged, which is the same condition `BoundSerialiser.PassesArgumentsThrough`
  already uses for the fingerprint, so lowering and fingerprint agree. An accessor entry with any other
  adapter leaves the call as it is. Event accessors (`Subscribe`) are not touched.
- Decision: criterion 2's variant is a fourth method of the same sample, `StripOther`, which trims `'/'`
  on the legacy side and `'.'` on the modern side. The entry still applies to it and is listed; the two
  `TrimEnd(char)` calls take different constants, so it is EQ002.
- `Strip` and `Indent` are proved by the solver (`bounded`), `Full` by congruence: its entry passes
  arguments through, so the legacy fingerprint already names the modern getter. The trim entries pass
  through too, but the legacy tree still holds the `params` array, so the fingerprints differ.
- The `bcl.directory-info-full-name` reason leans on full trust: the .NET Framework override demands
  path-discovery permission first. Partially trusted code is outside what the tool models.
- Local run: `webapi-basic` and `version-bump` fail in a fresh worktree until `build.ps1 -Integration`
  restores them; that is unrelated to this ticket.
