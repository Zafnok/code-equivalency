# P2-174 Two instantiations of a generic type in one body share a field map of the first one's type, and lowering can crash
Status: todo
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-027

## Goal
`HeapInputs.Field` names a field's map `field.<declaring type's metadata name>.<field>`. A metadata
name carries no type arguments, so the `Item1` of a `(string, int)` and of an `(int, string)` are one
map, `field.System.ValueTuple_2.Item1`, and `HeapInputs.Input` hands back whichever the body named
first. The second read then yields a value of the first tuple's element type.

Found while implementing P2-170, at equiv `0a2f9a03`. Minimal repro, valid C#:

```csharp
static int M((string, int) p, (int, string) q) { int m = q.Item1; string s = p.Item1; return m; }
```

`IrLowerer.Nullness` throws `InvalidCastException` (`IrBitVec` to `IrSort`): `p.Item1` is read from
a map of `bv32`, and the store to `s` asks the `null.*` map of a value that is no sort. With the two
statements in the other order `q.Item1` is a `System.String` stored to a `bv32` local. Where the
ill-typed value only feeds an operator, as in `p.Item1.Length + q.Item1`, the operator goes opaque
with reason `Binary` and nothing fails.

A tuple outside P2-027's sort is the common case, but nothing here is about tuples: a generic type of
the solution with a field of its type parameter (`Box<T>.Value`) has the same one map for `Box<int>`
and `Box<string>`. The sort of the receiver is shared in the same way (`System.ValueTuple`2` for
both), so the map's key type does not tell the two apart either.

P2-170 does not make this worse for its own shape: a deconstruction asks `HeapInputs.Holds` first and
stays opaque when the map is of another type. A plain `t.Item1` read or write has no such check.

When done, a body that names two instantiations of one generic type lowers without an exception and
without an ill-typed read, and a field of one instantiation is never read from the map of another.

## Spec references
`IOPERATION-COVERAGE.md` rows `FieldReference` and `Tuple`; VERIFICATION-MODEL section 3 (fields as
heap maps); `docs/tickets/done/P2-027-tuple.md`; `docs/tickets/done/P2-125-*.md` (tuple names and
sorts); `.claude/skills/equiv-extend-ir`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Decide through `equiv-decide` between the two fixes and record it in Notes: a map per
   instantiation (the map's name, and the receiver's sort, carry the type arguments), or one map per
   field as now with every read and write of a field whose map is of another type made opaque.
2. The repro above lowers, in both statement orders, and validates.
3. The same for a generic class of the solution with a field of its type parameter, read at two
   instantiations in one body.
4. Both sides of a pair still name the same map for the same field of the same instantiation, so a
   body with one instantiation lowers to the IR it lowers to today (snapshot tests unchanged, or the
   change to each is described in the PR).
5. `HeapInputs.Holds` and its use in `IrLowerer.IsTakenApart` go away if criterion 1 picks a map per
   instantiation, since no map is then of another type.

## Tests
- `IrLowererTests`: criteria 2 and 3, each in both orders.
- `LoweringOracleTests` is unchanged and passes.

## Size guard
A change to how a sort is named for a generic type that is not a field's receiver: stop, that is
its own ticket.

## Out of scope
Widening P2-027's `IrTuple` sort to reference elements.

## Notes
- Filed 2026-10-13 from P2-170. No open ticket owned the finding.
