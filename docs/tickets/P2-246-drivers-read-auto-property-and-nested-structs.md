# P2-246 Execution reach: a driver reads and builds a struct whose fields are behind auto-properties or are flat structs
Status: todo
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-242

## Goal
P2-242 made a flat struct (`DriverStruct`: every instance field visible to the driver and of a scalar or `string` kind) a
`probe` argument and a comparable result, and left two common shapes out by its size guard:
- a struct whose state is in auto-properties (`public float X { get; }`): its backing fields are private, so it is not
  flat, and its result is `NotComparable` on both sides, which says nothing about the values;
- a struct with a field that is itself a flat struct (a rectangle of two points): its field has no scalar kind.

When done, a struct's auto-property backing field is read through its property's visible getter and matched to a
constructor parameter by the property's name, and a field of a flat struct type is read and built by the same rule,
nested as a JSON object in `probe` and as a nested array in the canonical form.

## Spec references
ADR 0035 decisions 2 and 3, tickets P2-242, P2-245 (array, string and object fields), `DriverStruct`, `DriverSource`,
`ProbeArguments`.

## Acceptance criteria (all must hold; nothing beyond them)
1. A struct whose every instance field is a visible field of a supported kind or the backing field of an auto-property
   with a visible getter is read field by field, in declaration order, the backing field through its getter.
2. A field whose type is a flat struct (by this ticket's rule, to any depth without a cycle) is read and built as one;
   `probe` takes it as a nested JSON object.
3. Such a struct is built through an object initializer when every field or property is settable, else through the
   constructor whose parameters are its fields' or properties' names and types, in order; otherwise it is read only.

## Tests
Unit tests for each shape (auto-property read, auto-property built by constructor, nested struct read and built, a
cycle-free depth of two), and a real-driver run of one in `ReplayIntegrationTests`.

## Out of scope
Array, object and delegate fields (P2-245). Properties with bodies, which are not state.

## Notes
- Filed 2026-10-18 by P2-242 under its size guard: no sampled result needed either shape, but modern C# declares most
  struct state as auto-properties, so a struct result is otherwise still `NotComparable` in the common case.
