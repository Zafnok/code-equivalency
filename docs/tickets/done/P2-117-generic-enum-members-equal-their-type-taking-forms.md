# P2-117 `Enum.GetValues<T>()` and `Enum.IsDefined<T>(v)` equal the `Type`-taking calls they replace
Status: done (PR #459)
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
- Decision: two entries, `bcl.enum-is-defined-generic` and `bcl.enum-get-values-generic`. The generic member's
  identity holds the enum (`IsDefined`1(System.DayOfWeek)<System.DayOfWeek>`), so an entry's `modern` spells it
  `{T}` and a new adapter item, `{"typeArgument": n}`, takes it from the `typeof` at source position n. The item
  passes nothing and the `typeof` is not evaluated. It addresses a `typeof` of an enum type only: both generic
  members are constrained to `struct, Enum`, and for any other type the `Type`-taking member throws.
- Decision: `IsDefined`'s value item carries `"ofTypeArgument": true`: the value, unboxed, must be of exactly the
  enum the `typeof` names. `Enum.IsDefined(typeof(E), 3)` is legal and has no generic form, and
  `Enum.IsDefined(typeof(E), otherEnumValue)` throws, so neither is rewritten (criterion 2's unit test,
  `GenericEnumMembersLoweringTests.ATypeThatIsNotATypeOfOfTheSameEnumIsNotRewritten`, has both, a `Type` variable,
  a `typeof` of a non-enum and an `object` value).
- Decision: `GetValues<E>()` returns `E[]` and `GetValues(Type)` returns the same array typed `Array`, and one
  callee cannot have two result sorts. The entry carries `"returnsTypeArgumentArray": true`: the rewritten call
  yields the `E[]` and the legacy value is that read through `cast.<E[]>.System.Array`, which is how
  `(Array)Enum.GetValues<E>()` lowers. The table rejects the flag on an entry with no `typeArgument` item.
- The `Count` half needed more than the table. With the call rewritten, the legacy loop still enumerated
  `Cast<E>()`'s `IEnumerable<E>` through `GetEnumerator`, `MoveNext`, `Current` and `Dispose`, and the modern loop is
  the index loop over the array (P1-004), with no call. Decision: a `foreach` whose collection is
  `Enumerable.Cast<E>()` of a call that such an entry rewrites is lowered as the index loop over the rewritten
  call's array. `Cast<E>` returns an `IEnumerable<E>` it is given as it is, the `E[]` is one, and an array
  enumerated through `IEnumerable<E>` yields its elements in index order. This is a lowering rule
  (`IrLowerer.CastArray`, IOPERATION-COVERAGE row `ForEachLoop`), keyed on the entry, and it is the only place
  `Cast<E>()` is read: `Enum.GetValues(typeof(E)).Cast<E>().ToList()` keeps its `Cast` call, and against
  `Enum.GetValues<E>().ToList()` it is not proved. Nothing in `jellyfin-13023` needs that; it is one more rule when
  a run finds it.
- `(K)Enum.Parse(typeof(K), s, true)` and `(T)Activator.CreateInstance(typeof(T))` are not included: the cast does
  not lower once the call is rewritten. The rewritten call would return `K`, the legacy call returns `object`, and
  the explicit unboxing `(K)` around it stays opaque with reason `Conversion`; equating the pair needs the box and
  the unbox removed together, which is a rule about the conversion, not a table entry. `CreateInstance`'s `T` is
  also not an enum, which the `typeArgument` item requires.
- Out of scope, by shape: `Enum.GetNames(Type)` to `GetNames<T>()` has `GetValues`' shape exactly except that both
  return `string[]`, so it would be a `typeArgument` entry with no result flag. `Enum.GetName(Type, object)` to
  `GetName<T>(T)` has `IsDefined`'s shape. `Enum.ToObject(Type, ...)` has no generic form with the same result
  type (it returns `object`) and shares neither shape. None is added; each is one row when a run finds it.
- `IlLoweringParityTests`: both sides' `Count` joined `Known`. That test lowers with no API equivalence, so it sees
  the legacy loop as written, where the IL calls `MoveNext` on the enumerator's conversion to `IEnumerator`
  through a cast map and the IOperation lowering calls it on the `IEnumerator<T>`; and the modern loop over a
  call's result, where the IOperation lowering keeps the array's null flag in a shadow variable and the IL reads
  the null map in the loop, so the headers do not pair up. Both are differences between the two lowerings of one
  body, not of the pair.
- `Known` is proved by the solver (`bounded`) and `Count` by `lockstep-induction`: the two loops are the same IR.
