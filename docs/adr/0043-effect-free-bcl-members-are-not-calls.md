# ADR 0043: A catalogued BCL member that runs no observable code is not a call

Status: accepted (2026-10-02). Amends ADR 0018's "Calls are stateful" for the members of one
catalogue, and adds one assumption to VERIFICATION-MODEL section 1.

## Context
P2-047's audit found 3 false EQ002 among 31 audited (ticket P2-071). One side reads `String.Length`
once more. One side allocates an empty `ConcurrentBag<T>` where the other allocated another empty
collection. One side constructs a parser under an upgraded library's new class name. ADR 0018 makes
every call a trace event with a free `threw` flag, a result that depends on its position, and, unless
it is closed (ADR 0041), a write of every heap map. So one more read of `b.Length` on one side is an
extra event, it may throw, and it moves every later call to another position. No run of the real
member does any of that. The smallest pair:

```csharp
static int F(string a, string b) { if (a == null || b == null) return 0; return a.Length; }
static int F(string a, string b) { if (a == null || b == null) return 0; _ = b.Length; return a.Length; }
```

## Decision
The frontend holds one catalogue of **effect-free** BCL members. A use of one is not an `IrCall`: it
has no trace event, no `threw` flag, no heap pair, and it does not count as a position. The catalogue
has two kinds of entry, and a member is in it only by name.

1. **A getter of an immutable value.** `System.String::get_Length()`. The receiver is null-checked
   where a call's is. The read is then the `IrPure` function `get:<call identity>` of the receiver,
   which raises nothing. Both sides share it, so one string has one length wherever it is read.
2. **A parameterless constructor that only allocates.** `new T()` for `List<T>`, `Dictionary<K,V>`,
   `HashSet<T>`, `Queue<T>`, `Stack<T>`, `LinkedList<T>`, `SortedDictionary<K,V>`, `SortedList<K,V>`,
   `SortedSet<T>`, `Collection<T>`, `ConcurrentBag<T>`, `ConcurrentDictionary<K,V>`,
   `ConcurrentQueue<T>` and `ConcurrentStack<T>`, when the type is declared in metadata and not in the
   solution. The new object is read from `new.<Sort>` at the body's count of that sort's creations so
   far, as a new array's reference is (P2-001), and its null shadow is false. A constructor with an
   argument (a capacity, a comparer, a source collection) is not in the catalogue and stays a call.

**One empty list.** `List<T>` and `Collection<T>` are one family. A `new Collection<T>()` that is
the operand of an implicit reference conversion to an interface is lowered as `new List<T>()` at
that conversion: it reads `new.<List sort>` and then `cast.<List>.<To>`. So a body that stores a new
`List<int>` in an `IList<int>` field and one that stores a new `Collection<int>` there leave the same
heap. This assumes that no code asks such an object for its concrete type: a type test, a downcast,
`GetType`, `ToString`, reflection or serialisation. Anywhere else (a local of type `Collection<T>`, a
conversion to `object`) a `Collection<T>` is its own sort. No other two types are one family.

The IL lowering (ADR 0039) applies entries 1 and 2 by the same catalogue. It does not apply the
family rule, because a `newobj` carries no conversion.

## Why
- Each entry is a fact about the member, not about the program. `String.Length` reads a field of an
  immutable object. The listed constructors allocate, and run no code the program supplied: the
  default comparer of a `Dictionary` or a `SortedSet` is found at construction and called only by a
  later member, which is still a call.
- An `IrPure` is the construct that already means "a function of its arguments, no event, no heap, no
  position" (ADR 0025). Its result is tainted (ADR 0026). That is right here: the solver chooses a
  string's length freely, so a model in which `"abc"` has length 7 is not a witness.
- `new.<Sort>` is the construct that already means "the k-th allocation of this sort" (P2-001). Both
  sides share it by name, so their k-th allocations are one reference, and nothing ties it to the
  objects the inputs reach, which only adds inputs.
- A side that allocates a collection it never uses, or reads a length it discards, no longer differs
  from one that does not. That is ADR 0018's first listed precision cost, removed for these members.
- The family is one pair because each pair is a claim that two types answer every interface member
  alike. `Collection<T>` wraps a `List<T>` and forwards to it. `HashSet<T>` against `List<T>` is the
  counter-example for anything wider: `Add(1); Add(1); Count` is 1 and 2.
- The family rule is the only way the ticket's second pair can be Equivalent. Without it the two
  stored values are `cast.<List>.<IList>` and `cast.<Collection>.<IList>` of two different
  references. The assumption it needs is named in section 1, as ADR 0041's is.

## Rejected
- **`String.Length` as a `length.System.String` map, as an array's length is.** It is untainted, so
  a Divergent could rest on a string constant's length that no string has, and the differential
  gate's rule 2 would replay a string whose real length is not the model's.
- **A flag on `IrCall` (`EffectFree`).** It changes Core, the text format, the validator, the encoder
  and the interpreter to say what `IrPure` and `new.<Sort>` already say.
- **A rule instead of a list (every getter of an inert type, every parameterless constructor of a
  type in `System.Collections`).** `Nullable<T>.Value` throws, `List<T>.Count` reads the heap, and a
  parameterless constructor of a framework type can read the environment. ADR 0041 rejected a
  catalogue of heap summaries for the review each entry needs. That cost is accepted here for a short
  list, because no rule over signatures separates these members from the ones that throw.
- **Every catalogued collection as one family.** A false Equivalent for `HashSet<T>` against
  `List<T>`.
- **Dropping the dynamic type of every new object behind an interface.** The same, for user types.
- **Third-party constructors (the audit's third case).** Their bodies are not in the run. A call
  site with the same text that binds to another type is ADR 0042's rebound call. One with other text
  stays a call.

## Consequences
- A Divergent that depended on the value of `String.Length` is now Unknown with reason `abstraction`
  (`s.Length > 3` against `s.Length > 4`). `--execute` can still observe it (ADR 0035).
- A side that makes one more allocation of a sort before another allocation of it shifts the count,
  so the later objects are different references and the pair can be Divergent. Arrays already behave
  this way.
- A real change that swaps `List<T>` for `Collection<T>` behind an interface, in a program that does
  ask for the concrete type, is reported Equivalent. That is the assumption above.
- These members are no call sites, so they are in no rebound pair (ADR 0042) and the census's
  `externalCallees` no longer counts them. The snapshots of `samples/unknown-new-throw` and
  `samples/cleanup-modern-syntax` lose their `System.String::get_Length()` and `List<int>` constructor
  rows.
- VERIFICATION-MODEL sections 1, 2 and 3 change in the PR that accepts this ADR. ADR 0018's row in
  the README names this ADR.
- Ticket P2-071 implements it: `samples/effect-free-bcl-call`, and unit tests that a catalogued
  getter is an `IrPure` and no call, that a catalogued constructor reads `new.<Sort>` and is no
  call, that a constructor with an argument, a source-declared type of the same name and a
  `Collection<T>` not converted to an interface are unchanged, and that the IL lowering agrees on
  entries 1 and 2. P2-081 (a closed call's answers) and P2-099 (collection expressions) build on it.
