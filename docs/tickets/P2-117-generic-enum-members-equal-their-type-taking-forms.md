# P2-117 `Enum.GetValues<T>()` and `Enum.IsDefined<T>(v)` equal the `Type`-taking calls they replace
Status: todo
Effort: S
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-066

## Goal
An upgrade PR takes the analyzer's suggestion to use the generic `Enum` members (CA2263). P2-066's
`jellyfin-13023` has two such edits, and both are EQ002 although nothing changes:
- `Jellyfin.Data.Entities.User::AddDefaultPreferences()`:
  `Enum.GetValues(typeof(K)).Cast<K>()` becomes `Enum.GetValues<K>()`;
- `Jellyfin.Server.Implementations.Tests.TypedBaseItem.BaseItemKindTests::EnumParse_GivenValidBaseItemType_ReturnsEnumValue(System.Type)`:
  `Enum.IsDefined(typeof(K), v)` becomes `Enum.IsDefined(v)`.

Each side calls a different BCL member, so the traces differ (ADR 0018) and each call's result is
free. Minimal repro, as a sample pair:

```csharp
// legacy
static bool Known(System.DayOfWeek d) => System.Enum.IsDefined(typeof(System.DayOfWeek), d);
static int Count() { int n = 0; foreach (var d in System.Enum.GetValues(typeof(System.DayOfWeek)).Cast<System.DayOfWeek>()) { n++; } return n; }
// modern
static bool Known(System.DayOfWeek d) => System.Enum.IsDefined(d);
static int Count() { int n = 0; foreach (var d in System.Enum.GetValues<System.DayOfWeek>()) { n++; } return n; }
```

Today both methods are EQ002. They should be Equivalent. Add the forms to the shipped equivalence
table (VERIFICATION-MODEL section 3), as P2-070 does for a rebound overload: the `Type`-taking call
with a `typeof` argument is rewritten to the generic one. The same PR has
`(K)Enum.Parse(typeof(K), s, true)` to `Enum.Parse<K>(s, true)` and
`(T)Activator.CreateInstance(typeof(T))` to `Activator.CreateInstance<T>()`; both are Unknown (opaque,
`Conversion`) today. Include them if the cast lowers once the call is rewritten, and say in `## Notes`
if it does not.

## Spec references
VERIFICATION-MODEL section 3 (the equivalence table, `properties.equivalencesApplied`), ADR 0018,
P2-070, `docs/runs/2026-10-03-upgrade-verdict.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. `samples/generic-enum-members` (the pair above) is Equivalent for both methods, and each result
   lists the equivalence it used.
2. A `Type` argument that is not a `typeof` of the same enum does not use the equivalence (unit test).

## Tests
Integration test on the sample; the unit test in criterion 2.

## Out of scope
Other analyzer rewrites. `Enum.GetNames`, `Enum.GetName` and `Enum.ToObject`, unless they share the
table entry's shape: list them in `## Notes`.

## Notes
- Found by P2-066: 2 false EQ002 on `jellyfin-13023`.
