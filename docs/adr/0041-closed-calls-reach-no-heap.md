# ADR 0041: A call to a string or primitive member with only string and primitive arguments reaches no heap map

Status: accepted (2026-09-30). Amends ADR 0018's "Calls are stateful" for closed calls only.

## Context
ADR 0018 makes every call read and write every `field.*` and `array.*` map, because which maps a
callee reaches is not known without a call graph. That is sound, but the solver can then build a
Divergent on a heap that no real callee produces. P1-017's IL mode of the differential gate found
one (P2-060): `z = ($"{s}t" == null);` lowers from IL to `String.Concat(string, string)`, Z3 chose a
`Concat` that writes `Oracle.F` and `u[0]`, a loop that reads both then ran differently on the two
sides, and the C# replay does not diverge. The same pair written `s + "t"` gives the same Divergent
from IOperation. `String.Concat` cannot name `Oracle` and is handed nothing that leads to it. ADR 0026's
2026-09-30 clarification had to excuse every such model from the gate's rule 2.

## Decision
A call is **closed** when its callee's containing type is inert, and so is every parameter type of
the constructed callee (`ref`, `out` and `in` included) and every type argument of the method. The
inert types are `bool`, `char`, the 8- to 64-bit integers, `float`, `double`, `decimal`, `string`, every
enum, and `Nullable<T>` of an inert `T`. A closed call reads no heap map and writes none. Its result,
`threw` flag and `ref`/`out` outputs are functions of (callee, arguments, position), and its trace event is
(callee, arguments). Every other call keeps ADR 0018's rule unchanged: it reads and writes every
map. Both lowerings mark a call closed by the same rule. The encoder encodes a callee identity as
closed only when every call to it in the product is closed. A closed call that shares its identity
with an open one lacks heap pairs, so it leaves its own side's maps unchanged, but it reads and
writes the maps the encoder threads, as the open call does. The two calls then share one function
and one event shape. A closed call's result and `threw` flag stay free functions that the solver
chooses, so the differential gate's rule 2 still excuses a replay that does not diverge whenever the
model's run records a call, closed or open.

## Why
- A callee reaches a heap map only through a reference: to the map's object, or to user code that
  holds one. An inert value is neither. It holds no reference to a mutable object, and it can run no
  user method: `string` and the primitives are sealed and declare no callback, and an enum or
  `Nullable<T>` carries only a value. The containing type is inert too. So a closed callee holds no
  stored state that leads back to user code, the way a `Thread` holds the delegate `Join` runs.
- This covers the callbacks P2-060 names. A delegate, an interface, `object`, a non-sealed class, an
  array, a span or a generic argument of any of those is not inert, so a call handed one stays open.
  An extension method on `string` is declared on the user's static class, so it stays open.
- Dropping the heap from a closed call's event is sound for the same reason. The callee cannot read
  the heap, so two calls with equal arguments cannot tell two heaps apart. It also removes ADR 0018's
  second precision cost for these calls: a field write before `Concat` on one side and after it on
  the other no longer makes the traces differ.
- Position stays an argument. The callee may still read ambient state such as the current culture,
  so two calls with equal arguments are still not forced to agree (ADR 0018).
- Rule 2 keeps its exemption for closed calls because of a measurement. With the exemption narrowed
  to open calls, the gate passes its 200-pair PR budget but fails the 5,000-pair nightly budget
  (seed `9v8KtBVEhjFh`, DropNullCheck under IL). There the model has `Nullable<int>.GetValueOrDefault()`
  throw, which no real call does. That is a closed call's `threw` flag, not the heap, so this ADR does
  not remove it.
- Deciding closure per identity in the encoder keeps each identity at one function arity and one
  event shape. That matters only when one site cannot see the callee's symbol, such as an
  API-equivalence adapter's modern member, which is always open.

## Rejected
- **Reachability by assembly references (a callee cannot write a map whose declaring type its
  assembly cannot name, unless handed a callback).** Unsound for callbacks stored before the call:
  `thread.Join()` is handed nothing, yet runs the delegate given to `new Thread(...)`. A `List<T>` can
  also hold a user comparer. Proving that a BCL type stores no callback needs its private fields,
  which metadata import does not expose.
- **A catalogue of callee summaries (which maps each BCL member writes).** It is per member, it needs
  review for every entry, and the ticket rules out summaries beyond reachability.
- **Keeping ADR 0018's rule and tainting call outputs.** ADR 0026 rejected tainting ordinary call
  results, and it would turn every model through a call into Unknown.
- **Closing a call when only its arguments are inert.** `Console.WriteLine(string)` writes through
  `Console.Out`, which the program can set to its own `TextWriter`.

## Consequences
- The one assumption this adds is named in VERIFICATION-MODEL section 1: an inert type's members do
  not run user code installed as ambient state. The only such state they read is the current
  culture, which a program would have to replace with its own `CultureInfo` subclass whose getters
  write its fields.
- `IrCall` gains `Closed` (default false), written as `closed` in the IR text. A closed call has no heap
  pairs, and the validator rejects one that has. `ICallOracle` is unchanged, because the model oracle
  asks the encoding which identities are closed.
- VERIFICATION-MODEL sections 1, 2 (the `IrCall` row), 5 and 7 (rule 2) change in the PR that
  accepts this ADR. ADR 0026 gains a second 2026-09-30 clarification: a Divergent can rest on a call's
  result or `threw` flag too, not only on its heap writes.
- A Divergent that rests on a BCL call's `threw` flag or result, which the real member cannot give,
  is still reported as EQ002. Ticket P2-081 owns that.
- Ticket P2-060 implements it and pins the gate's pair. `tools/corpus/seeder/SyntaxMutator.cs` treats an
  interpolated string as a call when reordering statements (P1-017). It stays as it is: an
  interpolated string with a non-string hole is still an open call.

## Clarifications
- 2026-10-02 (P2-088). **An anonymous type's constructor.** `new { X = x, ... }` calls a constructor
  the compiler writes: it stores each argument in a field of the new object and runs nothing else.
  The Why's first bullet covers it. A callee reaches a heap map only through a reference it follows
  or user code it runs, and this constructor follows none of its arguments and runs none. So the call
  is closed whatever the property types are, a reference to a mutable object included. The inert-type
  rule stays the only rule for a callee whose body is not known. Lowered only where the object is itself
  an argument of a call. A read of a property is a getter call on the value and stays open. The IL
  lowering does not mark the constructor closed: it sees an ordinary `newobj`, so the two lowerings
  differ here, as they do for every construct only one of them lowers.
