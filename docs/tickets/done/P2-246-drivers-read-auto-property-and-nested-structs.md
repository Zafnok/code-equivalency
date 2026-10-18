# P2-246 Execution reach: a driver reads and builds a struct whose fields are behind auto-properties or are flat structs
Status: done (PR #548)
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
- Decision: `DriverStruct.Fields` is a list of `DriverStruct.Field(Name, Type)` rather than `IFieldSymbol`: a backing
  field is read, set and named in `probe`'s JSON by its property's name, so the consumers (`DriverSource`,
  `ProbeArguments`) needed only the type change and get nesting from their existing recursion.
- Decision: "auto-property" means a getter that only returns its backing field: in source, every declaration of the
  getter is an accessor with no body; from metadata (which links no backing field to its property) the field is the
  compiler's `<Name>k__BackingField` and the getter is `[CompilerGenerated]`. A C# 14 `field`-keyword getter with a body
  (`get => field * 2`) is a property with a body, so it is out of scope and the struct is not flat; a set-only
  `field`-keyword property has no getter and is not flat either.
- Decision: an object initializer needs every field writable and every property's setter visible and not `init`: the
  driver source is C# 7.3 (P2-056), which cannot call an `init` accessor, so an `init` property is built by its
  constructor. A struct whose nested struct is read only is itself read only.
- Decision: the cycle guard is the set of enclosing structs on the walk. Valid C# has no struct layout cycle (CS0523),
  but the frontend can see a compilation with errors, and a cycle there would otherwise recurse without end.
- Decision: no `Release:` footer. `probe` accepts and compares more struct shapes but its arguments, result and errors
  keep their shape, which `equiv-release` counts with newly supported constructs as a patch.
- Checked in passing: a source struct with a primary constructor whose parameter is captured (`struct S(int a)`) shows
  the captured field among its members, so it is not flat, not read as if `a` were absent.
