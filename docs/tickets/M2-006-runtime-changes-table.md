# M2-006 Runtime-changes table (EQ006)
Status: todo
Effort: M
Model: Sonnet, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M2-003

## Goal
A shipped `runtime-changes.json` listing BCL members whose behaviour differs between
.NET Framework 4.8 and .NET 10 for textually identical calls, each with the URL of the
Microsoft breaking-change entry. Initial rows: culture-sensitive string comparison and
`IndexOf` family (ICU vs NLS), `string.GetHashCode` randomisation, x86 floating-point
intermediates (x87 vs SSE) for `float`/`double` arithmetic when the legacy platform
target is x86, `BinaryFormatter` and serialization defaults, `Encoding` defaults.
The frontend tags matched calls to these members; the backend never treats them as the
same uninterpreted function; the result is Divergent with ruleId EQ006 and the link in
the message. `equiv.config.json` can suppress per member. The table has a schema and a
test that every row's URL is well-formed and every member identity resolves against
the reference assemblies.

## Spec references
VERIFICATION-MODEL.md sections 3, 5, 6; ADR 0008.

## Deliverables
- [ ] expand into concrete items before coding (see tickets/README.md template)
- [ ] tests: unit, snapshot (a sample calling `IndexOf` on both sides yields EQ006), property (suppression is a no-op for members not in the table)

## Out of scope
Auto-mapping renamed APIs (still Divergent unless the user maps them).

## Notes
