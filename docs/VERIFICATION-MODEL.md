# Verification model

This document is the specification the code must satisfy. Tests cite its section numbers.

## 1. What we claim

For a matched procedure pair (P_old, P_new) with the same input signature, we claim
**Equivalent** iff for every input state the observable outputs are equal. Observable
outputs are: the return value, the final values of `ref`/`out` parameters, the final heap
(every field and array slice either side touches; ADR 0018), the sequence of opaque calls
made (callee identity, arguments, and the heap at the call; a closed call's event has no heap,
ADR 0041), and whether the procedure throws (exception type, not message).

Verdicts are modular (ADR 0019). A call to another matched procedure is an uninterpreted
function that both sides share, so a verdict assumes those callee pairs are equivalent. The
SARIF result names them (`assumedCallees`) and flags the ones not proved in the same run
(`unprovenAssumptions`). This holds for cycles too: if every pair on a cycle of matched
procedures is Equivalent, every pair is partially equivalent (the mutual-summary rule).

A changed callee that the caller cannot observe does not stay an assumption (ADR 0036 decision 2;
ticket P1-010). Take an Equivalent caller whose unproven assumptions include a lowered callee pair
(f, f'). The run builds a relational contract K from what the caller observes of that call (section
5.2). It proves on the product of f and f' that the pair satisfies K, and then proves the caller again
with each side's call given its own outcome and heap, related to the other side's only by K. If the
caller is still Equivalent, f moves from `unprovenAssumptions` to `contractsUsed`. The proof of K is
itself modular, so f's own unproven assumptions join the caller's.

A callee with no pair is not assumed, it is read (ADR 0045; ticket P2-097). A one-sided helper is a
`private` ordinary method with a body, declared in one side's source, whose identity is Added or
Removed after the rename map. It is not generic, not in a generic type, not `async`, not an iterator
and not `extern`, has no `ref`, `out` or `in` parameter, does not return by reference, lowered without
a failure, and does not call itself, directly or through other one-sided helpers. Its expanded body,
the lowered body with every call this rule resolves inside it replaced in turn, has at most 256 IR
instructions. A call to one from a method of the helper's own type is replaced by a copy of that
expanded body: the parameters are bound to the evaluated arguments and `this` to the null-checked
receiver, the helper's synthesised inputs become the caller's by name, and a heap map enters the copy
at the caller's version and leaves as the caller's next version. The helper's call has no trace
event and no functions; its body's calls are the caller's, at the caller's positions. A returning
exit continues after the call. A throwing exit keeps its exception type: `System.Exception` takes the
call's `threw` edge, and any other type is the caller's own throw, which needs the call to be outside
every `try`, `catch`, `finally`, `using`, `lock` and `foreach` region, or the call is not resolved. A
call that is not resolved is an ordinary call. The frontend resolves on the lowered IR after
matching, for both lowerings. A callee matched on both sides is never read this way (ADR 0019).

A call is closed when its callee's containing type, every parameter type and every type argument
are inert: `bool`, `char`, the 8- to 64-bit integers, `float`, `double`, `decimal`, `string`, an enum, or
`Nullable<T>` of an inert `T` (ADR 0041). A closed call reads and writes no heap map. This assumes that
an inert type's members run no user code installed as ambient state (a `CultureInfo` subclass set as
the current culture whose getters write the program's fields). The constructor of an anonymous type is
closed too, whatever its property types are: the compiler writes it, and it only stores its arguments
(ADR 0041, clarified 2026-10-02).

A use of a member in the effect-free catalogue (section 3; ADR 0043) is not a call: it is no trace
event and it cannot throw. An empty `List<T>` and an empty `Collection<T>`, each created and
converted to an interface at once, are one object. This assumes that no code asks such an object for
its concrete type (a type test, a downcast, `GetType`, `ToString`, reflection or serialisation).

Evaluating a hole of an interpolated string is assumed to leave the current culture's integer
formatting as it found it: the hole does not set the thread's current culture, and does not write the
number format of a writable one (ADR 0044). `string.Format` formats an integer hole after the later
holes have run and `DefaultInterpolatedStringHandler` before, so only a pair whose sides bind the same
text differently leans on this.

Where a body sits is not compared (ADR 0046). The file path or line number the compiler supplies for a
`[CallerFilePath]` or `[CallerLineNumber]` parameter is an input both sides share, so a pair that
differs only in the directory it was checked out to, the file a body is in, or the line a call is on
is Equivalent. A path or line the source writes out is an ordinary value and is compared.

Everything else (timing, allocation, log text, exception messages) is not observed.

No single algorithm decides equivalence for every program pair, but this sub-problem
(regression verification: same language family, mostly identical code) is tractable in
practice. Loops are handled by the ladder in section 5.1; a procedure gets **Unknown**
only when every rung fails, the solver times out, some input reaches an `IrOpaque` node
that is not shared by both sides (ADRs 0014 and 0024; a call site the two sides bind to different
callees is one, ADR 0042), every divergence found depends on
an abstraction (ADR 0026), or the method's bound body is erroneous (`Unbound`, ADR 0029). Every
Unknown states its scope. A `line` Unknown is still a proof about every input that reaches none
of the lines it lists; a `method` Unknown claims nothing (ADR 0029). A pair whose bound bodies fingerprint equal and are not
runtime-sensitive is Equivalent by congruence, without the solver (`proofMethod:
congruence`, ADR 0024): identical bound code makes the same claim a shared call does. A body is
runtime-sensitive only by a runtime rule that applies inside the pair's runtime interval (section 3,
ADR 0040), so on a same-runtime pair no body is. Every result carries `properties.proofMethod` (which rung proved it),
`properties.boundedBy` when the claim is bounded, and `properties.opaqueNodes`, so a
reader can see exactly how strong the claim is. Never report Equivalent without saying how.

Running the code on the two real runtimes (`--execute`, ADR 0035) is a second oracle beside the
solver, and execution never proves. It can confirm a Divergent (`replay`), show one
(`proofMethod: observed`, a Divergent the runtimes were seen to produce), or bound an Unknown with
a likelihood (`differentialTesting`, the Good-Turing chance that the next generated input shows
new behaviour, stated for the generators' input distribution). A tested Unknown stays Unknown. No
number of passing runs makes a result Equivalent, and a `line` Unknown's residual claim, which is a
proof, is reported next to the testing figure and never replaced by it (ADR 0029).

## 2. IR

Procedure = signature + ordered basic blocks + entry block. Block = instructions +
terminator. SSA: every `IrVar` is assigned once; blocks with several predecessors use
`IrPhi`.

Types: `Bool`; `BitVec(n)` for integral types (n in 8, 16, 32, 64, signedness kept on
the operation, not the type); `Sort(name)` for everything else (strings, objects,
decimals, floats), treated as uninterpreted with equality only; `Map(key, value)` for
SSA heap slices (one map per field, one per array sort) encoded as SMT arrays. Floating
point is a `Sort` in the MVP (not IEEE-modelled); a post-MVP ticket exists. Operators on
floating point, `decimal` and user-defined operators are `IrPure` applications of named
functions both sides share (ADR 0025, ticket M4-002), so unchanged arithmetic is provable
without modelling its semantics. A value tuple of two or three `bool` or integral elements is the
`Sort` `tuple(<element types>)` (`tuple(bv32,bool)`), a tuple literal the pure function `tuple.new` of its
elements, and an element read, by `Item<n>` or by the name the tuple gives it, the pure function
`tuple.item<n>` of the tuple, since element names are compile-time only (`IrTuple` holds the spelling; P2-027).
Unlike other pure functions these two are exact: at each `tuple.new` the encoder asserts that each
`tuple.item<i>` of the result is its `i`-th element, and at each `tuple.item<n>` of `t` that `t` is
`tuple.new` of its items, so a read of a literal is its element and two tuples read to have equal elements
are equal. Both are theorems of tuples, so they are asserted unguarded.

IR instructions never throw. Every exception edge is explicit in the CFG: the frontend
lowers `checked` arithmetic to an overflow test plus a branch to a throw block, and a
call that may throw yields a Bool that the frontend branches on.

Instructions:

| Instruction | Meaning |
|---|---|
| `IrConst(var, value)` | literal |
| `IrBinary(var, op, a, b)` | arithmetic, bitwise, comparison; wrapping semantics; signedness is part of `op` |
| `IrOverflows(var, overflowOp, a, b)` | Bool: would the checked operation overflow; `overflowOp` in SAdd, UAdd, SSub, USub, SMul, UMul, SDiv |
| `IrUnary(var, op, a)` | negation, not, conversions with explicit target width and signedness |
| `IrPhi(var, [(block, var)])` | SSA merge |
| `IrCall(var?, threw?, callee identity, args, refouts, heap)` | opaque call; appended to the observable call trace; `threw` is a Bool output. `refouts` are the new versions of the call's `ref` and `out` arguments, in parameter order, each a definition; a `ref` argument's value at the call is also one of `args`, an `out` one's is not (M4-003). `heap` lists, per by-ref map the call reads and writes, the map's name, the version before the call (a use) and the version after it (a definition); the C# frontend lists every `field.*` and `array.*` map the body touches, at every call, since which fields a callee reaches is not known without a call graph (P1-005), except at a `closed` call (section 1), which has no heap pairs (ADR 0041; P2-060). Result, `threw`, each ref output (one function per output index) and each map's new version are functions of callee, arguments, the heap at the call and the call's position in the trace (ADR 0018); a closed call's are functions of callee, arguments and position |
| `IrMapRead(var, map, key)`, `IrMapWrite(newMap, map, key, value)` | SMT `select`/`store`; fields and arrays are maps in SSA like any other value |
| `IrPure(var, throws, function, args)` | applies a catalogued pure function (`f64.add`, `dec.mul`, `op:<identity>`, `delegate:<fingerprint>`); no trace event, no heap, no position; each entry of `throws` is a Bool output branching to an `IrThrow` of its exact exception type; shared by both sides except runtime-sensitive functions, which are side-specific (ADR 0025) |
| `IrOpaque(var?, reason, sourceSpan, fingerprint?, reads, threw?, heap)` | frontend could not lower; execution past this point is not modelled, so an input that reaches it has an unknown outcome (ADR 0014), unless the same `fingerprint` occurs on the other side, in which case both occurrences are one call `opaque:<fingerprint>` over `reads` (ADR 0024). A fingerprinted fragment has what that call needs: a `threw` flag the frontend branches on and the heap pairs an `IrCall` has; `reads` and each pair's `before` are uses, `threw` and each `after` definitions (M4-004) |

Terminators: `IrGoto`, `IrBranch(cond, then, else)`, `IrSwitch`, `IrReturn(var?, outs)`,
`IrThrow(exceptionTypeIdentity, outs)`, `IrUnreachable` (assume false; produced by loop
unrolling, never by the frontend). `outs` names, for every `ref`/`out` parameter, the
SSA version live at that exit; that is how final by-ref values become observables on
both normal and exceptional exits.

Exact record shapes, the text format and the validator rules are specified in ticket
M1-002 and pinned by its snapshot tests.

Null: reference-typed values are a `Sort` plus a separate `Bool` "is null" shadow
variable. A dereference lowers to a conditional `IrThrow(NullReferenceException)`, placed
where the CLR checks it: at the field or element load or store, or at the call, after every
operand evaluated before it (index, arguments, a stored value) (P2-017).

Heap and nullness are inputs (M2-004). A procedure's parameter list is its C# parameters
followed by the synthesised inputs its body needs, ordered by name: the receiver `this`,
one `null.<Sort>` map from a reference sort to Bool, one `field.<Type>.<Field>` map per
field touched, `array.<Sort>` from an array reference to its elements by bv32 index plus
`length.<Sort>` from an array reference to its length, per array sort indexed, keyed by the
reference like `null.<Sort>` so that two variables holding one array share its elements (P1-006), and one
`cast.<From>.<To>` map from `<From>`'s IR type to `<To>`'s sort per implicit reference or boxing
conversion between different IR types (M3-010), one `istype.<From>.<To>` map from `<From>`'s sort to Bool per
reference type test (M4-005), one `typeof.<T>` input of `System.Type` sort
per closed type `T` a body reads with `typeof(T)` (P2-002), and one `new.<Sort>` from bv32 to an
array sort per array sort a body creates (P2-001), or to a collection's sort per catalogued collection
it creates (ADR 0043). An array creation `new T[n]` (one `int` dimension)
throws `System.OverflowException` when `n` is negative, then reads its reference from `new.<Sort>` at
the body's count of that sort's creations so far (0 for the first), writes `n` into `length.<Sort>`
and a constant map of `default(T)` into `array.<Sort>` at that reference, and stores an initialiser's
values at indices 0, 1, ... in order; its shadow is false. `new.<Sort>` is shared by name, so both
sides' k-th creations of a sort are one reference; nothing keeps it apart from the arrays the inputs
reach, which only adds inputs, never removes a real run. A cast map is an uninterpreted function with no
trace event: the same operand always converts to the same value. The converted value's nullness is
read from `null.<To>` like any value's, not tied to the operand's, which over-approximates (a real
upcast of a non-null value is never null). A type test of a reference `x` against a reference type `T` (`x is T`,
a type or declaration pattern, `x as T`, a downcast `(T)x`) is `!isNull(x) && istype.<From>.<T>[x]`: `istype` is a
free predicate per pair of types, shared by both sides by name, and nothing ties it to the type hierarchy. `x is T t`
binds `t` to `cast.<From>.<T>[x]`, never null; `x as T` is that cast when the test passes and `null` otherwise, and
its nullness is the test's negation; `(T)x` branches to `IrThrow("System.InvalidCastException")` when the test fails
on a non-null `x`, is `null` for a null `x`, and the cast otherwise. Unboxing, a test of or against a type parameter,
and a test between types no reference conversion relates stay opaque. `typeof(T)` for an open generic or method type parameter
`T` stays `IrOpaque("TypeOf")`; for a closed `T` it reads `typeof.<T>` directly, is never null and
adds no trace event. The product
encoding (M3-001, ADR 0021) shares the C# parameters by position, because that is how a caller
binds them, and the synthesised inputs by name; two parameters of different types are never
shared, each is then an input of its own side. A synthesised input's name is `this` or contains
a dot, and a C# parameter's never does: that is how the encoder tells them apart (`IrParameterNames.IsSynthesised`
is the one definition). A C# parameter whose name would be synthesised, which can only be one declared `@this`,
is spelled with a leading `$` in IR (`$this`; its source name stays `this`). No C# identifier contains `$`, so
that name is never another parameter's (M3-007). A value's shadow is a `mapread` of `null.<Sort>`,
so equal references are equally null; `new` sets the shadow to false instead. `this`,
`null.*`, `cast.*`, `istype.*`, `length.*`, `typeof.*`, `new.*` and `caller.*` are `In`, because nothing changes them, except
that `length.<Sort>` is `Ref` in a body that creates an array of that sort (P2-001; ADR 0018 clarification). No CLR array has a
negative length, so the encoder assumes every read of a `length.*` input is non-negative, whether or not the read is
reached (the CLR never reads a null reference's length, so this drops no input a caller can pass), and the model
decoder gives 0 wherever a model's length map is negative, which can only be at a reference nothing reads (P2-019). `field.*` and `array.*` are
`Ref` (ADR 0018, ticket M3-007), so every exit names their final version in `outs` and the
final heap is an observable like any `ref` parameter. When only one side of a pair has a given
`Ref` map, the other side never touches that slice, and the encoder compares the first side's
final value against the shared input. IR variable names take only letters, digits, `_`, `.`
and `$`, which is why these names are spelled with dots.

Two gaps the M2-004 heap model left open (ADR 0015), both closed before M3-003 as ADR 0018
schedules. The first, a call that neither read nor wrote the heap, so that a pair differing only in
where it reads a field around a call was not distinguished, is closed by P1-005: every `IrCall`
reads and writes each `field.*` and `array.*` map its procedure touches. The second gap, array maps keyed per array *variable*
so that two variables holding one array were two independent slices, is closed by P1-006: the
element and length maps are keyed by the array reference.

## 3. Lowering rules (C#)

Source of truth is Roslyn `ControlFlowGraph.Create` over `IOperation`. The Roslyn CFG
already desugars `foreach`, `using`, `lock`, `?.`, `??`, pattern matching, string
interpolation and `try`/`finally` into explicit blocks. That is why we lower from the
CFG instead of walking syntax: syntactic sugar is gone before we see it. What we add is
SSA renaming, type narrowing, opaque-call identity, and explicit exception edges.

One desugaring is undone (P1-004): the CFG turns every `foreach` into enumerator calls, but a `foreach`
over a single-dimensional array lowers to the index loop the compiler emits. A loop is recognised
from the operation tree (an array collection and a declared loop variable) together with the CFG
(that variable's assignment from `IEnumerator.Current` names the enumerator's capture). The capture
holds the array instead; a bv32 index starts at 0 and has a phi at the loop header; `MoveNext` is
`index < length.<Sort>[array]` (signed, null-checked as `a.Length`); `Current` is the element read
as `a[index]` (the `array.<Sort>` slice at the array, then the index, null- and bounds-checked),
after which the index steps by one; the enumerator's `finally` is not run. A loop whose variable is a
deconstruction, or whose element read is not found, stays the M4-001 enumerator calls.

The CFG captures an assignment's target before a value that branches, and `f ??= v` captures `f`
(P2-006, P2-007). A captured local or parameter is that variable. A captured field (not a lowered tuple
element), single-dimensional array element or auto-property is its map at the receiver (and index)
evaluated at the capture, and any other captured property is its receiver and index arguments evaluated
there: a read of the capture reads the map or calls the getter, a write writes the map or calls the
setter, and the capture has no null shadow of its own.

`e.E += h` and `e.E -= h` on an event are a call to its `add` or `remove` accessor with `e` (none for
a static event) and `h`, lowered as a setter call is: no result, `e` null-checked at the call after
`h` is evaluated, and a `threw` edge (P2-005). A field-like event is no exception, since the compiler
calls its accessor too.

A lambda, anonymous method or method group converted to a delegate (P2-067; ADR 0024 and ADR 0025 clarifications)
is an `IrPure` of the function `delegate:<fingerprint>` when the conversion runs no code and has a bound fingerprint.
It runs no code when its operand is a lambda or anonymous method, a static method, or a method of `this` or `base` in
a class: nothing is evaluated, nothing can throw, and the heap is not read, so the delegate is no trace event, has no
heap pair and no `threw` edge. The fingerprint is the conversion's, by the rule for an opaque expression below (the
delegate type, and the lambda's bound body or the method's identity), and it is refused in the same cases; the
arguments are the same reads. A lambda's function is `delegate:<fingerprint>#<n>`, where `n` is the position of its
site among the body's lambda sites with that fingerprint in lowering order (a site copied onto several paths, as a
`finally`'s is, stays one site): two lambdas compile to two methods, so their delegates are never equal, and
`e += a; e -= b` over two lambdas with one body removes nothing. A method group's function has no site, since two
conversions of one method with one receiver are equal delegates. Equal functions of equal reads are one value on both
sides, so a callee handed the delegate is the same call; two evaluations of one site are also one value, which
matches `Delegate.Equals` and conflates only reference identity, as a boxing `cast.<From>.<To>` does. A lambda whose
capture is stored at a point reachable after the conversion is opaque again, with reason `DelegateCreation` and no
fingerprint. A method group whose receiver is evaluated (`o.M`, which null-checks `o` and may itself run code) stays
an `IrOpaque` with reason `DelegateCreation`, shared as any fingerprinted fragment is, and a conversion with no
fingerprint (a runtime-sensitive body, a capture some lambda or local function writes, a local function declared
outside it, a struct's `this`) stays one that is not shared. A delegate creation's null shadow is false, as a `new`'s is. Like every `IrPure` result the delegate is
tainted (section 6): two sides whose lambdas differ apply two functions, and a divergence that depends on them is
Unknown(Abstraction) naming both, not Unknown(Opaque).

An interpolated string (P2-086) binds `string.Format` on .NET Framework and `DefaultInterpolatedStringHandler` on
.NET 6 and later, so the same text is two different bound trees (ADR 0024) and reaches the solver. It is lowered as
the concatenation of its parts, which is what both bindings compute, when every hole is a `string` or an 8- to 64-bit
integer with no format or alignment clause: each part in order is joined to the ones before it by the closed call
`System.String::Concat(string,string)` (ADR 0041), exactly as the `+` chain of the parts lowers, a text part being its
constant and a `string` hole its value, null included. An integer hole is first the closed call to its type's
parameterless `ToString()`, as a written `i.ToString()` is: both bindings format an integer with the current culture,
and a closed call's result already depends on its position, so no assumption about the culture is added. A lone hole
is joined to `""`, since `$"{s}"` is never null. The two bindings differ in one thing this lowering cannot hide:
`string.Format` formats after every hole is evaluated, the handler formats each hole before the next one is
evaluated. So every hole after the first integer hole must only read a local, a parameter, a constant or a field of
`this`; with no call and no throw there, the two orders are one. Any other interpolated string (a clause, a hole of
another type, whose formatting goes through `IFormattable` or `ISpanFormattable` members the bindings do not share,
or a call after the first integer hole) stays an `IrOpaque` with reason `InterpolatedString`, fingerprinted per
binding.

Effect-free BCL members (ADR 0043; P2-071). One frontend catalogue lists, by name, the BCL members that
run no observable code. A use of one is no `IrCall`: no trace event, no `threw` edge, no heap pair, no position.
It has two kinds of entry. A getter of an immutable value, which is `System.String::get_Length()`: the
receiver is null-checked as a call's is, and the read is the `IrPure` function `get:<call identity>` of the
receiver, which raises nothing and is tainted like every `IrPure` result (section 6). And the parameterless
constructor of `List<T>`, `Dictionary<K,V>`, `HashSet<T>`, `Queue<T>`, `Stack<T>`, `LinkedList<T>`,
`SortedDictionary<K,V>`, `SortedList<K,V>`, `SortedSet<T>`, `Collection<T>`, `ConcurrentBag<T>`,
`ConcurrentDictionary<K,V>`, `ConcurrentQueue<T>` or `ConcurrentStack<T>`, when the type is declared in
metadata: the new object is `new.<Sort>` at the body's count of that sort's creations so far, as a new
array's reference is, and its shadow is false. A constructor with an argument stays a call, and so does every
member the catalogue does not name. `List<T>` and `Collection<T>` are one family: a `new Collection<T>()` that
is the operand of an implicit reference conversion to an interface is lowered as `new List<T>()` there, so it
reads `new.<List sort>` and `cast.<List>.<To>` (the assumption is section 1's). Anywhere else a `Collection<T>`
is its own sort. The IL lowering (section 3.1) applies the two kinds of entry and not the family rule, since a
`newobj` carries no conversion.

An anonymous object that is itself an argument of a call (P2-088), through the conversion to the parameter's type
if there is one, is the closed call `{X,Y}::.ctor(<types>)` of its property values, each evaluated in declaration
order first: the constructor's identity with the type spelled by its property names in declaration order, since an
anonymous type has no name. So two sides that build the same object from equal values agree, and other names,
another order or other property types are another callee. Like any closed call it has a trace event and a `threw`
edge. An anonymous object that goes anywhere else (returned, stored in a local, nested in another one, a receiver)
stays an `IrOpaque` with reason `AnonymousObjectCreation`, and a read of a property stays a getter call on the value.

A collection expression (P2-099) is the construct it replaces, so the two are compared as two spellings of one
body. For an array target `[a, b]` is the array creation `new T[] { a, b }` above, and `[]` is the call
`System.Array::Empty<T>()` the compiler emits (a creation of length 0 where the framework has no `Array.Empty`).
For a class with a parameterless constructor, `List<T>` among them, it is a new object, as `new T()` is (for `List<T>`
no call; ADR 0043), and then one `Add` call per element, as a collection initializer is (P2-120). The `Add` is the only
method of that name with one parameter that the class or a base type declares, and its parameter has the elements'
type; a call to it is lowered as any call is, a forwarder as its target (ADR 0047). With no element nothing is added,
so `[]` is `new T()` for any such class, a `Dictionary<K, V>` included. For an `IEnumerable<T>`,
`IReadOnlyCollection<T>` or `IReadOnlyList<T>` target it is the array, read through the `cast.<T[]>.<Target>` map the
array's conversion reads. For an `IList<T>` or `ICollection<T>` target it is the `List<T>` the compiler builds, read
through `cast.<List<T>>.<Target>` (P2-120). Each element is evaluated, then stored or added, before the next, so element
order is in the trace. The CFG evaluates an element that needs more than one operation (`new T { P = x }`, `a ?? b`),
and every element before it, into flow captures ahead of the collection expression. For a class target the order above
still holds (P2-120): the object is made where the first such element starts, and each `Add` is where the next element
starts, or at the collection expression for the last ones. That is the order of the collection initializer and of the
compiled code. An element starts at the first statement or branch value of the graph whose syntax lies inside the
element's. An array is created after its captured elements, as the CFG has the array creation it replaces. Its shadow
is false only where the old form's is: a creation or a `new`, read through no cast map. A spread element, a span, a
type parameter, a type built by a `CollectionBuilder` method, a struct, a class whose constructor takes an argument, a
class with an element and not exactly one such `Add`, and an array the array creation leaves opaque, stay an
`IrOpaque` with reason `CollectionExpression`. The conversion around a target-typed `new()` whose creation is already
of the target type is its operand, so `new()` is `new T()`.

The CFG does not desugar a deconstruction (P2-025). A statement that deconstructs a tuple literal into
locals, parameters, captured lvalues, fields or discards, one level deep, lowers as C# evaluates it:
each field's receiver, then every element of the literal (each already converted to its target's
type), then each store, all left to right, so `(a, b) = (b, a)` swaps. Anything else, a `Deconstruct`
method or a tuple-typed value included, is `IrOpaque` with reason `DeconstructionAssignment`.

C# integer semantics the lowering makes explicit (M2-003; `char` is bv16, ADR 0013):

- Checked `+ - *` and unary `-` test `IrOverflows` and branch to one shared
  `System.OverflowException` throw block. A checked explicit integral conversion throws
  when the value does not round-trip, or when exactly one side is signed and the
  signed-side value is negative.
- `/` and `%` test for a zero divisor (`System.DivideByZeroException`). Signed `/` and
  `%` also test `IrOverflows sdiv` (`System.OverflowException`) whether or not the code
  is checked, because .NET throws on `MinValue / -1` and `MinValue % -1` in both contexts.
- A shift count is masked to `width - 1` before the IR shift, as C# does.
- An expression the lowerer leaves opaque carries its bound fingerprint (ADR 0024: the fragment's
  `IOperation` tree, a lambda of the graph as its bound body, serialised as a body is, with the
  method's parameters numbered by first occurrence like its locals) and reads the locals and
  parameters declared outside it that it references, a reference's null shadow after it, in order of
  first occurrence. It then has a `threw` flag branching as a call's does and a heap pair per heap map
  (M4-004). It gets no fingerprint when it is not an expression; holds a flow capture, null test or
  caught exception of the graph, or a struct's `this`; writes a local or parameter declared outside
  it; calls a local function declared outside it; reads a `ref` local; holds a lambda capturing a
  variable that is stored after it in the IR or that any lambda or local function writes; or is
  runtime-sensitive inside the pair's runtime interval, which both sides are given alike, so a
  fragment both hold gets the same answer on each (ADR 0040; P2-055). A whole-body opaque has none.
- An opaque call's `threw` flag branches to `IrThrow("System.Exception")`. `throw new T(...)`
  records the constructor call and then lowers to `IrThrow("T")` on T's static type (M2-004).
  Throwing any other expression is `IrOpaque` with reason `Throw`, because the thrown object's
  dynamic type is not known statically, and `throw;` is `IrOpaque` with reason `rethrow`.

An `async` method (ticket M4-006) is lowered as the synchronous body its CFG already is, over the original
operations, never over the compiler's state machine. `await e` is `IrCall(await:<awaiter type>, [e])`: its result is
the awaited value and its `threw` flag branches as any call's. The awaiter type is spelled as a member identity spells
its declaring type, with its type arguments as a generic callee's; a reference-typed `e` whose `GetAwaiter` is an
instance method is null-checked first, as a `callvirt` receiver is. The procedure returns the task's result: the type
argument of a generic task-like return type, nothing for `Task`, `ValueTask` or `void`. Why this is sound for a pair
where both sides are `async`: every observable of section 1 happens in the same order whether or not the body is
suspended at an `await` (the heap is threaded through the call, so what other code does meanwhile is a havoc both sides
share), and an exception thrown anywhere in the body, before the first `await` or after one, is caught by the method's
builder and stored in the returned task as it is, faulted (cancelled for `OperationCanceledException`), the same way on
both sides; so a throw of type `T` in the IR stands for exactly the task a caller observes. Two awaits are two calls at
different trace positions (ADR 0018), so awaiting one task twice is not forced to yield one value, and a real awaitable
that is not idempotent is modelled. `ConfigureAwait(false)` is an ordinary call whose result is what is awaited. A pair
where exactly one side is `async` is Unknown with detail `async-mismatch`, without the solver: a synchronous method
throws to its caller at the call, an `async` one into its task, which the caller sees only when it awaits, so their
exception timing differs. Iterators (`yield`, async or not) stay whole-body opaque with reason `iterator`: their
desugaring is a state machine.

`await using` and `await foreach` (ticket P1-029) lower as the CFG desugars them, which is as the synchronous `using`
and `foreach` do with the awaits the compiler emits in place. An `await using`, statement or declaration, is its body in
a `try` whose `finally` calls `DisposeAsync()` on the resource and then awaits the result (`await:<awaiter type>`), on
the normal and on the exceptional exit, after the null test that skips both for a null resource of a reference type;
several resources in one statement nest, the last acquired disposed first. An `await foreach` is the enumerator loop:
`GetAsyncEnumerator(...)`, a `MoveNextAsync()` call whose awaited result is the loop's condition, `get_Current`, and a
`finally` that calls and awaits `DisposeAsync()` when the enumerator has one. `WithCancellation` and `ConfigureAwait`
on the collection are ordinary calls whose result is what is enumerated, so the awaiters are then the configured ones.
These awaits have no `await` expression: the awaiter type is the result of the awaited type's own parameterless
`GetAwaiter`, and one that only an extension method supplies leaves that await `IrOpaque` with reason `Await`. The
`default` the compiler passes for a parameter `GetAsyncEnumerator` leaves optional, its `CancellationToken`, is the
constant element 0 of its sort on both sides: it is only ever that call's argument, and as an opaque it would have no
fingerprint to share, its syntax being the loop. Since `using` calls `Dispose()` and `await using` calls
`DisposeAsync()` and awaits it, a pair with one on each side makes different calls and is not Equivalent.

Floating-point, `decimal` and user-defined operators (ADR 0025, ticket M4-002) are `IrPure`
applications of the functions one frontend catalogue lists: `f32.<op>` and `f64.<op>` (arithmetic,
negation and comparisons, which never throw; `==` is `f64.eq`, not an equality of sort elements, since
`NaN != NaN`), `dec.<op>` (overflow on `+ - * / %`, divide-by-zero on `/ %`), and `conv.<from>.<to>`
for every numeric conversion to or from `float`, `double` or `decimal` (overflow for floating point
to `decimal`, for `decimal` to an integral type, and for floating point to an integral type when
checked). Unary `+` is its operand. A user-defined operator or conversion, including `string ==`, is
`op:<call identity>`, which may throw any exception, as an opaque call may. Each exception flag
branches to where that exact type goes, so `catch (OverflowException)` catches `dec.mul`'s overflow
and `catch (DivideByZeroException)` does not. A floating-point to integer conversion is
runtime-sensitive when the pair's runtime interval crosses .NET 9, where it began to saturate. Every
function taking or yielding floating point is runtime-sensitive on the side whose floating point alone
runs on x87: its project is on .NET Framework with a 32-bit platform and the other side's is not (ADR
0040 decision 2; P2-055). A .NET (Core) project is never x87, and two x87 sides agree. Lifted (nullable)
operators, compound assignment and `++`/`--` on these types stay `IrOpaque`.

Migration-specific normalisations (applied to both sides before matching):

- `System.Web` vs `Microsoft.AspNetCore` attribute routes map to one route identity.
- `IHttpActionResult` vs `IActionResult` map to one result identity (status code observed,
  body opaque). This is done by `api-equivalences.json` type and member entries (ADR 0020,
  ticket M3-009), not by a separate normaliser, and like every catalogue entry it is applied
  to the legacy side only. `HttpResponseMessage` has no entry: an action returning it stays
  Divergent unless the user maps it.
- Namespace and type rename maps come from `equiv.config.json`.
- BCL API changes are NOT auto-equated (`WebClient` vs `HttpClient` calls are different
  identities and therefore Divergent unless the user maps them, or Unknown when the call site's
  text is the same on both sides, as the rebound call sites below). False alarms are
  cheaper than false proofs. The one exception is a shipped, cited catalogue
  (`api-equivalences.json`, ADR 0020, ticket M3-009) of member and type pairs that are
  exactly equivalent whenever both are invoked: overload drift such as
  `String::Split(Char[])` → `String::Split(Char, StringSplitOptions)`, and Web API 2 →
  ASP.NET Core result helpers and result types. It also holds the rebinding forms (ticket
  P2-070), where identical source binds to an overload the modern reference assemblies add or
  to a member they move: `String::TrimEnd(Char[])` with one element → `String::TrimEnd(Char)`,
  `String::TrimStart(Char[])` with no element → `String::TrimStart()`, and
  `DirectoryInfo::get_FullName()` → `FileSystemInfo::get_FullName()`. The frontend rewrites a
  legacy call while lowering it, with an argument adapter, and every entry applied to a pair is
  listed in `properties.equivalencesApplied`. A property or event accessor call has no source
  arguments to adapt, so an entry rewrites one only when its adapter passes every operand
  through in order and unchanged. Users can suppress entries in `equiv.config.json`.
- Rebound call sites (ADR 0042, ticket P2-069). A call site is a call the lowering emits for a member
  at a syntax node: an invocation, an object creation, a property, indexer or event accessor, an
  `await`. Its key is the node's source tokens and the member's name. When a key occurs on both sides
  of a matched pair and binds to identity L on the legacy side and M on the modern side, and L
  differs from M after the rename map, the catalogue above and the config's call-identity map have
  been applied, (L, M) is a rebound pair: possibly the same function, neither equated nor told
  apart. A key bound to several identities on a side pairs each legacy-only identity with each
  modern-only one, and a callee in the runtime-changes table is never in a rebound pair. Every call
  to L in the legacy body and to M in the modern body is an `IrOpaque` with reason `rebound-call` and
  no fingerprint, emitted where the call would be, after its receiver and arguments are evaluated and
  the receiver is null-checked. An input that reaches one has an unknown outcome (ADR 0014). The pair
  lists its rebound pairs in `properties.reboundCalls`. A call to a member with another name, or at a
  site with other text, is an ordinary call. The IL lowering (section 3.1) marks the same identities.
- Forwarders (ADR 0047, ticket P2-068). A forwarder is an ordinary static method declared in the
  solution's source that is not `virtual`, not `async`, not generic and not in a generic type, has no
  `ref`, `out` or `in` parameter, does not return by reference, whose declaring type has no static
  constructor (written, or implied by a static initializer), that has no `[Conditional]` attribute
  and, like its declaring type, none from `System.Security` or below, and whose body is the one statement
  `return G(p1, ..., pn);` (or `G(p1, ..., pn);` when it returns nothing): `G` is a static method, the
  arguments are explicit and are the forwarder's own parameters, each at its own position with no
  conversion, `G` has no other parameter and takes each by value, and the parameter and return types
  of the two are the same.
  A call to a forwarder is lowered as the same call to `G`, or to the end of the chain when `G` is a
  forwarder too; a chain that returns to a method already on it is not followed. The rename map, the
  catalogue above, the runtime-changes table, closed calls (ADR 0041), rebound call sites and the
  bound fingerprint (ADR 0024) all see the target; a rebound site's key keeps the member name written
  at the site. Both sides and both lowerings apply it. A method both sides have is resolved only when
  both sides are forwarders to the same target identity; otherwise every call to it in the run stays a
  call to it, assumed as section 1 says (ADR 0019). The pair lists what it resolved in
  `properties.forwardersResolved`. Any other callee is not read: an instance method, a body that
  changes an argument or has a second statement, a forwarder known only as a compiled reference.
- Runtime-changed APIs: a shipped data table (`runtime-changes.json`, sourced from
  Microsoft's .NET Core 3.0 to .NET 10 breaking-changes list) names BCL members whose
  behaviour differs between two runtimes even when the call is textually
  identical (ICU vs NLS string comparison and `IndexOf`, x87 vs SSE floating point on
  x86, `GetHashCode` randomisation, serialization defaults), each row with the runtime
  that changed it (`changedIn`). A matched pair of calls to such a member is never
  treated as the same uninterpreted function; it produces Divergent with ruleId EQ006
  and a link to the breaking-change entry. Users may suppress per member in
  `equiv.config.json`.

Every runtime rule applies only inside the pair's runtime interval (ADR 0040 decision 2; ticket
P2-055). A matched pair's interval runs between the runtimes of the two projects its bodies come
from (`run.properties.runtimes`, section 6), in either order, older end excluded and newer end
included. .NET Framework orders before every .NET (Core) version. A project hosted on several
runtimes contributes them all, and the interval runs from the oldest to the newest of both
projects'. Two unhosted projects with the same `netstandard` target framework are one runtime; a
pair with any other unhosted project crosses every change the table covers. Both bodies of a pair
are lowered and fingerprinted with that one interval. The rules are:

- a `runtime-changes.json` row, when the interval crosses its `changedIn`; a row whose `changedIn` is
  `null` applies whenever the two runtimes differ;
- floating-point to integer conversion, when the interval crosses `net9.0`;
- x87 floating point, on the side whose project alone runs on the 32-bit .NET Framework JIT. This is
  the one rule that depends on the platform too, so it can apply between two projects on the same
  .NET Framework version.

On a same-runtime pair the interval is empty: no callee is runtime-changed, no pure function is
side-specific by a runtime rule, runtime sensitivity never blocks congruence, and EQ006 cannot be
reported. A .NET 8 to .NET 10 pair gets only the rows changed in .NET 9 or .NET 10. A .NET Framework
4.8 to .NET 10 pair crosses every row, as before. When a pair's interval reaches below the oldest
.NET version the table covers (`coveredFrom`, `netcoreapp3.0`), the run carries one `warning`
tool-execution notification with descriptor id `uncovered-runtime-range` naming the widest
uncovered range and the number of matched pairs that cross it; a behaviour that changed there is
not flagged.

### 3.1 IL fallback (ADR 0039)

With `--il-fallback`, a matched pair that is not congruent, and where either side's IOperation
lowering holds an `IrOpaque` whose fingerprint the other side does not share, is lowered again on
**both** sides from ILSpy's ILAst. The ILAst is read from the side's compilation emitted in memory
with a portable PDB, through P1-012's structural transforms only (none that rebuilds a C#
construct). The IL bodies replace the IOperation bodies only if they hold fewer unshared opaques;
otherwise the pair keeps its IOperation lowering. A pair is never lowered half from each.

The IL lowering produces the same IR the rules above do: every type and member reference is
resolved to the loaded compilation's symbol and goes through the same sort and identity mapping, so
calls, field and array maps, `cast.<From>.<To>`, `null.<T>`, the pure catalogue and ADR 0021's
parameter naming are identical between the two lowerings. An instruction the IL table does not map
(`docs/tickets/IL-COVERAGE.md`) is an `IrOpaque` whose reason is its ILAst key (`LdFtn[lambda]`,
`UnboxAny`, ...), with the source span of the nearest sequence point. The table declines what the
IOperation rules decline for a semantic reason: unboxing, reading a caught exception, `ref` locals,
`throw` of anything but a `new`, `default` of a type parameter, and local functions. It also declines lambdas, which
the IOperation rules lower from a bound fingerprint (P2-067) that the IL does not have.

An argument the compiler supplies for a `[CallerFilePath]` or `[CallerLineNumber]` parameter (Roslyn's
`ArgumentKind.DefaultValue`, of type `string` or `int`) is not its constant (ADR 0046, P2-098). It reads the
synthesised input `caller.file` (a `string`) or `caller.line` (bv32), one of each per body, shared by both sides by
name, and the bound fingerprint writes it as `caller=File` or `caller=Line` with no value. An argument the source
writes out is an ordinary value, and so is a supplied default of any other parameter, `[CallerMemberName]` and
`[CallerArgumentExpression]` included. IL does not say which arguments were supplied, so the IL lowering applies the
rule to a string constant equal to the body's own file path, and to an `int` constant inside the body's own line span,
passed for such a parameter.

## 4. Matching

Identity = assembly-agnostic namespace + type + member name + normalised parameter
types. Present on one side only means `Added` or `Removed` (SARIF level `note`).
Ambiguous overload mapping means `Unknown`.

## 5. Encoding

Product program: declare inputs once, inline P_old and P_new with disjoint SSA names,
assert that some observable differs (disjunction over return, out params, call trace,
threw flag, exception type, final heap), check. A call's result, `threw` flag and heap
effect are uninterpreted functions of (identity, arguments, heap at the call, position),
where the position is the number of calls the same side made before it (ADR 0018). Calls
at the same position with the same identity, arguments and heap therefore agree across
sides. Two calls on one side are never forced to agree, because a real callee may be
stateful. The exception is an identity a runtime-changes row applies to inside the pair's runtime
interval (section 3), which gets side-specific functions. An `IrOpaque` whose fingerprint occurs on both sides is encoded as
exactly such a call, with identity `opaque:<fingerprint>`, its reads as arguments, and its `threw`
flag and heap pairs (ADR 0024; M4-004): the backend rewrites it into that `IrCall` before the
ladder runs, so the encoder, the replay oracle and the taint predicate treat it as any call. Every
path past a shared fragment branches on its tainted `threw` flag, so a divergence there is
Unknown(Abstraction), never Divergent. An `IrPure` is a function of its arguments only, shared unless runtime-sensitive (ADR
0025). The call trace is a bounded list compared element-wise; an event
is (identity, arguments, heap at the call). The heap at a call ranges over every map a heap pair
names on either side, in name order (P1-005). A call reads a map it pairs at its `before`; any
other map it reads at the version the encoder threads through that side's calls, which starts at
the shared input and is replaced by each call's new version of the map. A side that does not have
the map as a parameter reports that threaded version as its final value. A callee identity every
call to which in the product is closed (ADR 0041) is encoded without the heap: its functions take the
arguments and the position only, it leaves every map, threaded or not, as it was, and its event is
(identity, arguments). A closed call whose identity also has an open call has no heap pairs, so it
leaves its own side's maps unchanged, and it reads and writes the threaded maps as that open call
does.

A pair that is not Equivalent is then asked under which inputs it is (ADR 0048; ticket P1-022). This
runs on rung 1's product of a pair without a loop or a self-call, for a Divergent the solver found
and for an Unknown with reason `abstraction`. A pair whose model calls a runtime-changed member is
left out, which is every EQ006, since that callee's functions are each side's own. The condition is
a hypothesis under ADR 0036:
- **Candidates.** The Bool values either body computes from shared inputs alone. A shared input is
  a source parameter both sides have, such a reference parameter's `null.<Sort>` shadow, or a Bool
  or bitvector constant. A value reaches them through `IrBinary` and `IrUnary` only. A call, a read
  of any other map, an `IrPure`, a phi, an opaque node, a parameter one side lacks or a truncation
  in between leaves the value out. So does a value that reads no parameter, or three or more. Each
  value `c` gives two candidates, `c` and `not c`. A pair has at most 16, the shallowest values
  first, and no value more than 8 operations deep. Nothing is synthesised.
- **Check 1, the proof.** `p` together with "some observable differs or a side reaches an unshared
  opaque node" is unsatisfiable. This is ADR 0014's second query restricted to `p`, so it is the
  proof an Equivalent rests on, for the inputs `p` holds on.
- **Check 2, not vacuous.** `p` is satisfiable together with the product's own assertions, so some
  input meets it.

A candidate that passes both is admitted. A query the solver gives up on admits nothing. The
condition is the disjunction of the admitted candidates, since each is proved alone. An admitted
candidate that implies another is dropped, because the other covers its inputs; that costs one
query per ordered pair and is skipped above 6 admitted candidates. Each query gets the pair's
resource limit and timeout. The result's own counterexample satisfies "some observable differs" on
the same product, so it must make the condition false. The backend checks that, and a failure is a
bug in the tool: the condition is not reported (section 6).

### 5.1 Loop ladder

Loops and recursion are tried on each rung in order; the first rung that proves
Equivalent or finds a counterexample wins, and `proofMethod` names it.

| Rung | Method | Claim | When it applies | Ticket |
|---|---|---|---|---|
| 1 | Bounded unrolling, k iterations, `assume false` on the last back edge; self-calls inlined k deep | Divergent with a concrete trace; Equivalent (`boundedBy: k`) only when no input reaches the bound | always; runs first because counterexamples surface at small k | M3-002 |
| 2 | Lockstep relational induction (mutual summaries): align loop pairs by position in the loop nesting forest and pair each header's state; cut both sides at every header and prove, from equal inputs and from each pair of equal header states, that both sides reach the same next header with equal states or leave with equal observables | **unbounded** Equivalent | both sides have the same loop forest and pairable header states; covers unchanged and cosmetically changed loops | M3-002 |
| 3 | k-induction: rung 2 with k prior iterations assumed equal | unbounded Equivalent | bodies agree only after warm-up | M3-002 |
| 4 | Constrained Horn clauses solved by Z3 Spacer: each side is cut at every loop header, one relation per pair of cut points, and Z3 synthesises the coupling invariant | unbounded Equivalent (the invariant in `properties.invariant`), Divergent when a derivation replays, or Unknown(chc-timeout, chc-spurious) | loops do not align (loop to LINQ, fusion, iterator rewrite), and neither side calls or applies a pure function | P1-001 |
| 5 | Proposed coupling invariant checked by Z3, first mined from runs of both sides (`trace-invariant`), then from a model (`llm-invariant`); a wrong guess can never yield Equivalent | unbounded Equivalent, or Unknown(no-invariant) | rung 4 timed out | P1-002, P1-009 |

Recursion is handled by rung 2 with the recursive call as the induction point
(the standard regression-verification treatment): a self-call stays a call both sides share. Rung 1 inlines it
instead, so its counterexamples are real. Mutual recursion needs no rung: a call to another matched procedure is
shared (section 1, ADR 0019), so `Recursion` means only a self-call that no rung decided.

Rung 1's result is a proof only when no input reaches the bound; otherwise it only refutes, and rungs 2 and 3
decide. A pair with loops or a self-call that no rung decides is Unknown: `Opaque` when a failed obligation reaches
an `IrOpaque`, `Recursion` when a side calls itself, `UnalignedLoop` when the loops do not align or neither
induction proves them and rung 4 does not apply, `ChcTimeout` when Spacer gave up, `ChcSpurious` when Spacer's
derivation does not replay to a divergence or its invariant does not solve the clauses, `Timeout` when only the solver gave up. A header's state is its phis plus every other value
live on entry to it (loop-invariant values, heap maps, values used after the loop). A rung 2 obligation's model is a
counterexample only when it comes from the base (real inputs) and replays to a divergence through the original
procedures; a step's model may start from an unreachable state. Every result lists the rungs it ran, with their
outcomes, in `properties.ladderTrace`. Partial equivalence is what every
rung proves; termination is compared separately as an observable only when both sides
have a syntactic termination argument (bounded counters), otherwise not claimed.

Rung 4 (ticket P1-001) follows Felsing et al., "Automating regression verification" (ASE 2014). Each side is cut
at every loop header, so its cut points are its entry, its headers and its exit, and there is one relation per pair
of an old and a new cut point, over the shared inputs, the sort literals and both sides' states there (at the exit:
returned, value, exception type, by-ref finals). A step runs one segment on one side or both: both step when both
segments return to their header or both leave it, otherwise only the side that loops steps, and a side that has
exited waits. Every pair of terminating runs is then one path of steps, so no loop pairing is needed. The query's
`bad` is two exited states whose observables differ, or a segment that reaches an `IrOpaque`. Rung 4 does not apply
to a pair either side of which calls or applies a pure function, since a call trace is no relation Spacer can infer.

Rung 4 asks over the integers first (`--chc-int-mode`, default on): a bitvector is the integer its bits denote read
signed, and an operation is exact (add, sub, neg, a product or quotient by a constant, comparisons, extensions,
truncation) or any integer within bounds (bitwise operations, shifts, a product of two unknowns). An invariant Spacer
finds over the integers is a proof over the bitvectors when it also solves the clauses read with wrap-around
arithmetic, which an SMT check decides rule by rule. Otherwise it stands only when a second Spacer query proves that
no exact operation of either side can overflow, since only then is every bitvector run an integer run; failing both,
rung 4 asks over the bitvectors. `properties.chcMode` names the arithmetic the answer holds in: `int` or `bitvector`.
Every derivation is replayed through the original procedures (a value the integers allow may be one no bitvector
run computes): a divergence is Divergent, an opaque node reached is `Opaque`, and anything else is `ChcSpurious`
with both runs in the detail. Every invariant is checked too before it is a proof (ticket P2-059): it must solve the
clauses of the query that found it, rule by rule, since Z3's Spacer has answered unsatisfiable with one that does
not; one that fails is `ChcSpurious`.

Rung 5 (tickets P1-002 and P1-009, ADR 0036) runs only when rung 4 timed out. It first asks a local proposer, on by
default because it runs in process and sends nothing (P1-009): it runs both procedures in `IrInterpreter` on up to 200
inputs (each earlier counterexample's first, then random ones), cut at their loop headers as rung 4 cuts them and
paired as rung 4 steps them, and defines each relation as the conjunction of every template instance that held on
all its samples: over each pair of same-sort arguments `x = y`, `x = y + c` and `x = c*y` for small constants, and
`x <= y`, and over each argument its range where the bounds are the same in every trace; a relation no run reached is
`false`. Z3 checks the candidate exactly as below; after a rejection it drops each conjunct the counterexample's
conclusion falsifies and retries. A proof is Equivalent with `proofMethod: trace-invariant` and `properties.proposedBy:
trace`. Disjunctive invariants are out of its reach, so a pair whose loops must end at `max(n, 0)`, such as loop fusion,
stays Unknown. Only if it fails does rung 5 ask a model, and only when `--invariant-model <id>` names one; the
CLI then prints `note: sending loop IR text to <id>` on stderr. It sends the model the IR text of both procedures and
rung 4's relations with their arguments' names and sorts, and asks for one SMT-LIB `define-fun` per relation, at
most three times. Each candidate is parsed against each relation's own arguments and may name nothing else, then
checked against rung 4's clauses read with wrap-around arithmetic, so an admitted invariant is a proof over the
bitvectors: the init, step and exit obligations must all be unsatisfiable. A parse failure or a failed obligation
(with the model's values of both relations' arguments) goes back to the model as a rejection. An admitted candidate
is Equivalent with `proofMethod: llm-invariant`, the candidate in `properties.invariant` and the model id in
`properties.proposedBy`; otherwise the pair is `NoInvariant`. Every round, its candidate and Z3's verdict is a step of
`properties.ladderTrace`. A wrong candidate is rejected by Z3, so it can never make a pair Equivalent.

### 5.2 Callee contracts

A callee contract K (ADR 0036 decision 2; ticket P1-010) relates one call's two outcomes, the legacy
side's and the modern side's, when both are called with equal arguments on an equal heap. K is a
conjunction. Its first candidate is built from the caller:
- the two sides throw alike;
- when both throw, they throw the same exception type;
- they make the same calls;
- they leave each heap map the same, for every map the caller's product or either callee side names;
- for every predicate the caller applies to the call's result, or to a heap map the call left, the
  predicate has the same value on both sides unless either side throws.

A predicate is a Bool the caller computes from that call's outputs through constants, operations,
map reads and unchanging synthesised inputs (`null.*`) only: a branch condition, a comparison, a
`== null` test, or a Bool result itself.

K is admitted when the product of f and f' cannot break it. That product is section 5's, with "some
observable differs" replaced by "K fails or the call traces differ". A loop segment's cut events are
trace events, so the traces must always agree. Rung 1 runs on the pair unrolled; a model of it
rejects K. An acyclic pair with no such model admits K. A pair with loops goes on to rungs 2 and 3,
with K as the exit condition. A rejected K loses every conjunct the model falsifies and is checked
again, four checks in all. The caller cannot tell exception types, the callee's calls or a heap map
it does not name apart, so a candidate whose model falsifies one of those conjuncts gets no contract.
Neither does a callee pair that reaches an `IrOpaque`, takes a source parameter by reference,
returns different types on the two sides, calls itself or has irreducible control flow.

In the caller's product, each side's call to f gets its own result, `threw`, heap and ref-output
functions (the `:old`/`:new` functions a runtime-changed callee gets). For every old call to f and
every new call to f with the same argument types, the product asserts: if both are reached at the
same position with equal arguments and an equal heap, their outcomes satisfy K. A conjunct the caller
has no term for (exception types, calls, a map it does not name) is left out. Calls that are not
aligned so are unrelated, exactly as shared functions leave them. The call stays in the trace. Sharing
the functions, or `threw`, under K would assume r = r', which is the assumption being removed. The
caller then runs through the whole ladder as usual.

## 6. Verdict semantics and SARIF mapping

| Verdict | SARIF `level` | `kind` | ruleId |
|---|---|---|---|
| Equivalent | none | `pass` | EQ001 |
| Divergent | `error` | `fail` | EQ002 (counterexample in `properties.model` and in `message`; with `proofMethod: observed` when the real runtimes showed it, below) |
| Unknown | none (rule default `warning`) | `open` | EQ003 (reason in `properties.unknownReason`: timeout, opaque, unmatched-overload, unaligned-loop, recursion, abstraction, unbound, chc-timeout, chc-spurious, no-invariant) |
| Added | none (rule default `note`) | `informational` | EQ004 |
| Removed | none (rule default `note`) | `informational` | EQ005 |
| Divergent (runtime-changed API) | `error` | `fail` | EQ006 (breaking-change link and both ends of the pair's runtime interval in `message`: `... diverges via a runtime-changed API between net8.0 and net10.0 (...)`; only for a row that applies inside that interval, ADR 0040) |

Every verdict on a matched pair with bodies also carries `properties.assumedCallees` and
`properties.unprovenAssumptions` (ADR 0019), `properties.equivalencesApplied` when a
catalogue entry fired (ADR 0020), and `properties.reboundCalls` when a call site was rebound (ADR
0042): one `{ legacy, modern }` per rebound pair of callee identities, sorted by legacy and then
modern identity. It is not part of the fingerprint. `properties.forwardersResolved` lists the forwarders a body's calls were
resolved through (ADR 0047): one `{ forwarder, target }` per forwarder, sorted by forwarder and then target; it is not part
of the fingerprint either. A result whose ladder reached rung 4 carries `properties.chcMode`, and an
Equivalent by `chc` carries Spacer's coupling invariant in `properties.invariant` (section 5.1). An Equivalent by
`llm-invariant` or `trace-invariant` carries the admitted invariant there too, and what proposed it in
`properties.proposedBy`: the model id, or `trace` (ADR 0036). An Equivalent whose proof used callee
contracts (section 5.2; ticket P1-010) has `proofMethod` suffixed `+contract` (for example
`bounded+contract`). It also carries `properties.contractsUsed`, one `{ callee, contract, proposedBy }`
per callee: `contract` is K in SMT-LIB over `r.old`/`r.new`, `threw.*`, `type.*`, `calls.*` and
`heap.<map>.*`, and `proposedBy` is `observed-predicates`. Each such callee is left out of
`unprovenAssumptions`, and its own unproven assumptions are added to the caller's `assumedCallees` and
`unprovenAssumptions`. A result one of whose rung 1 queries a second solver answered (section 6; ADR 0050,
ticket P1-033) has `proofMethod` suffixed with `+` and the solver's name, after `+contract` when both
apply: `bounded+cvc5`. That holds for a Divergent and an Unknown too, which otherwise carry no
`proofMethod`, and names the rung the solver answered for; a Divergent the real runtimes showed stays
`observed`. The `ladderTrace` step of that rung carries `solver`, the solver's name and version
(`cvc5 1.4.1`). A result Z3 decided alone is named as before. A result of a pair where a call to a one-sided helper was resolved in either body
(section 1, ADR 0045) carries `properties.calleesInlined`: one `{ callee, side }` per helper, `side`
being `legacy` or `modern`, helpers resolved inside helpers included, sorted by callee and then side.
It is not part of the fingerprint.

A counterexample is replayed in `IrInterpreter` with taint (ADR 0026): results of `IrPure`
and of `opaque:` calls are tainted, and so is an `opaque:` call's own trace event, since it stands for the
fragment's calls. Taint follows data, and a branch on a tainted value taints the rest of that side. The result is Divergent only when a compared observable differs and is
untainted on both sides. Otherwise it is Unknown with reason `Abstraction`, carrying the model
as `properties.candidateCounterexample` and the abstractions it depends on as
`properties.abstractions`. Each entry has an `identity` (an `IrPure` function name, such as `f64.add` or
`delegate:<fingerprint>#0`, or `opaque:<fingerprint>`), a `side` and, when known, a `span`; an `opaque:` entry also has `reason`, the
fragment's `IrOpaque` reason, for example `DelegateCreation` (ticket P2-062).

With `equiv compare --execute`, every Divergent is also replayed on the two real runtimes, the
second oracle of ADR 0035 (decision 2; ticket M4-009). Its model's inputs are bound back to each
side's parameters by the product's pairing rule (ADR 0021) and become C# arguments of the M3-032
generator types: a `bool`, an integer, a `char` or an enum from its bitvector; a `string` from its
sort element, `null` where the model's `null.<Sort>` map holds it and otherwise `"s<id>"`, so that
equal elements are equal strings; a `float`, `double` or `decimal` as the number `id`; any other
reference type only as `null`. A static method is called directly, and an instance method on
`new T()`, which needs a public parameterless constructor. Each side's project is emitted with its
references beside it, and with one `InternalsVisibleTo` naming the driver's assembly (`EquivReplay`)
added to its compilation as a syntax tree of its own, so the driver calls `internal` and
`protected internal` members directly, with no reflection; no source file changes. A strong-named
project may name a friend only by public key, so its driver is signed with a key generated for the
run and the attribute carries that key (P2-052). A driver calls each side's method once on that side's project's detected
runtime (`run.properties.runtimes`; ADR 0040 decision 3, P2-056), under the invariant culture, and also under `tr-TR` when either body
calls a member of the runtime-changes table, as differential testing does (P2-038). The result carries
`properties.replay`:
- `reproduced`: the two canonical outcomes (M3-032's canonical form) differ under some culture;
- `not-reproduced`: they are equal under every culture, and `properties.replayOutcomes` gives both
  invariant-culture outcomes (`kind`, `value`). The model and the CLR disagree; in a corpus run that
  is a soundness or modelling finding and gets a ticket;
- `not-applicable`: they are equal, `replayOutcomes` gives both, and the result is EQ006 (the model's
  call trace holds a runtime-changed callee). EQ006 claims the member differs between runtimes, and
  its side-specific functions are free in the model, so one call with the model's inputs need not
  show it (a hash seed, a default encoding, an ICU detail). Not a soundness finding (P2-038);
- `not-constructible`, with `properties.replayReason`: the method, or a type that contains it, is
  `private`, `protected` or `private protected` (`not public (<accessibility>)`, so `not public (private)`
  counts what only reflection could reach; P2-052); the method is generic, an
  accessor other than a getter, or takes a parameter by reference; the receiver has no public
  parameterless constructor or the model makes it null; a parameter's type has no generator; the
  model has a synthesised input other than `this` and `null.*` (a heap map, a cast or type-test
  map, `typeof`, `new`), since replay builds no object graphs; a project does not emit
  (`emit-failed`); the model's two runs end alike, so the divergence is in the call trace, which a
  driver does not observe; the model's call traces differ and the two real outcomes are equal, since
  the model's outcomes may then rest on call answers the solver chose after the traces split (ADR
  0026, "Why"; P2-037), so equal real outcomes are no evidence against it, while differing ones still
  give `reproduced`; both sides throw the same exception where the model's runs do not both throw,
  so the driver's `new T()` or an argument is not the model's (P2-038); or a side gives no
  comparable outcome. These come before `not-applicable`, so `not-reproduced` always means the
  model was wrong.

Each driver is built for its side's runtime: on .NET Framework an `.exe` with an `app.config` whose
`supportedRuntime` sku names that version, compiled at C# 7.3; on .NET a `.dll` with a
`runtimeconfig.json` naming its own `net<v>` and the installed framework version with
`rollForward: Disable`, compiled at the C# version that runtime ships with. A same-runtime pair runs
both sides on that one runtime. A project hosted on several runtimes runs on the first in
`runtimes` order. A runtime that is not installed makes that side `not-constructible` with
`replayReason` `runtime <tfm> not installed`, as does a project with no detected runtime (an unhosted
`netstandard` one); another runtime is never used in its place. `--execute` needs Windows only when
some loaded project runs on .NET Framework; otherwise it exits 3 naming the project and its runtime
(`error: --execute: <project> runs on <tfm>, and .NET Framework needs Windows (ADR 0040)`), and
`equiv mcp`'s `probe` refuses such a pair with the same message.

Replay never changes the verdict, the rule id, the fingerprint or the exit code, and a run without
`--execute` runs no code and writes no `replay`.

With `--execute`, every Unknown pair is also tested on generated inputs (ADR 0035 decision 3;
ticket P1-008). Execution never proves: this path yields Unknown or an observed Divergent, never
Equivalent. The pair is callable when both methods are, as for replay, and take parameters of the
same M3-032 generator kinds position by position; the receiver is `new T()`. One driver process per
side reads the cases on stdin, with M3-032's per-case timeout. The inputs are the solver's
`candidateCounterexample`, when it has one that can be built as arguments, then the M3-032
generators' stream from a fixed seed: every combination of edge values, then random draws, a
parameter with finitely many values drawing among them. Each input runs under the invariant
culture, and also under `tr-TR` when either body calls a member of the runtime-changes table.
Each input's species is the tuple of both runtimes' outcome classes per culture (the exception
type, or `returned` and one of 16 buckets of a fixed hash of the canonical value), whether all its
canonical outcomes are equal, and the IR path signature of each side: the blocks `IrInterpreter`
enters, with the input's arguments bound by position and every call and pure function answered
with a default, until the first opaque node or the first branch on such an answer
(`IrRun.Path`). Because equality is part of the species, a divergent input is a new species while
none has been seen. After n inputs with f1 species seen exactly once, the discovery probability is
f1 / n (Böhme, Liyanage and Wüstholz, FSE 2021); the generators are not adaptive, so the plain
Good-Turing estimate applies, and it bounds the chance of a new species, a divergence included,
under the generators' distribution only. Testing stops when that estimate is below `--test-target`
(default 0.001) after at least 1,000 inputs, or at `--test-budget` (default 10,000 inputs or 60 s
per pair). The result then carries `properties.differentialTesting`: `inputs`, `species`,
`singletons`, `discoveryProbability`, `speciesDefinition: "outcome+equal+irPrefix"`, `stoppedBy`
(`target` or `budget`) and `distribution: "equiv generators v1"`, and its message ends with
`Tested on <n> inputs; estimated chance the next input shows new behaviour: <p> (equiv generators,
not a proof).` A pair that cannot be tested, or a side whose driver cannot build an input's
arguments, carries only `differentialTesting.notConstructible` with the reason. None of these is
part of the fingerprint.

A divergent input (both canonical outcomes comparable and different) is run once more on each side
in fresh processes. If both outcomes repeat, the result becomes Divergent, EQ002 with
`properties.proofMethod: observed`, the input and its culture as `properties.model`, and both
canonical outcomes in the message. Its fingerprint is that of a new verdict, so against a baseline
that held the Unknown it is `new`. If either outcome changes, the input is only a species.

An Unknown result lists every reached opaque node and every abstraction it depends on as a
`relatedLocation` whose message is the reason, each line once, legacy side first and then in source order. Its
primary location is the first of them on the modern side, else the procedure (ADR 0027). `partialFingerprints` do
not change with it: an opaque Unknown's detail names each `side: reason` once, without lines.

Every Unknown carries `properties.scope` (ADR 0029):
- `line`: every cause is a span inside the method, and the first query of ADR 0014 was
  unsatisfiable. The result also carries `properties.residualClaim: "equivalent unless a
  relatedLocation is reached"`, and its message says so.
- `method`: a whole-body opaque, a timeout, an exhausted loop ladder, an unmatched overload, or
  `Unbound` code. So is any Unknown on a pair with a loop or self-call: rung 1 runs the first query
  on the unrolled pair, which proves nothing past the bound. An `abstraction` Unknown is `method`
  too, since the first query found its candidate, so that query was satisfiable (ticket M3-025).

Every solver query has two budgets, both set in `equiv.config.json` as positive integers (ticket P2-050):
- `resourceLimit` (default 5000000; `compare --resource-limit <n>` overrides it) is Z3's `rlimit`, a count of
  the solver's own steps. It is the budget that ends a query. It does not depend on how fast or how loaded the
  machine is, so the same inputs give the same results on every run, and a baseline comparison shows no `new`
  result that nothing caused.
- `timeoutMs` (default 60000) is the wall-clock backstop behind it, for a query that spends long in work Z3
  does not count. A result that ran into it can differ between runs.

Matched pairs are verified on up to `jobs` threads at once (default 1; `compare --jobs <n>`
overrides the config's `jobs`; ticket P2-077). The results are written in the order one thread writes them, and
threads never end a query sooner than one thread would: `resourceLimit` does not depend on what else is running,
and a phase that verifies on `n` threads gives each query `n` times `timeoutMs` on the clock, since threads that
share a processor each get a share of it. The default stays 1 until a run on several threads gives no result a
run on one does not (tickets P2-100, P2-132). A verifying run counts the
rungs a limit timed out in `run.properties.queryEndings`: `resourceLimit` for those the resource limit ended and
`wallClock` for those the backstop ended, over the ladders of the run's results. A run on several threads must
not have more `wallClock` endings than the same run on one.

A second solver is asked when one is configured (ADR 0050; ticket P1-033): `"solvers": { "cvc5": { "path":
"<executable>" } }` names a cvc5 executable, which `equiv` runs as a process and never ships. Every query
goes to Z3 first. A rung 1 query Z3 gives up on (`divergence`, `opaque` or `bound`) is then printed as Z3
prints it from a plain solver and sent to cvc5, after two rewrites that leave its meaning as it is: a
`seq.++` of one argument is its argument, and a constant array whose default is not a value becomes a fresh
array constant, constrained to hold that default at every index the query reads from an array built on it. A
query in which such an array is used as a whole (passed to a function, compared with another array) has no
such set of indices and is not sent. cvc5 gets `timeoutMs` of wall-clock time and a resource limit of its own
(`--rlimit` 2000000, chosen in `docs/runs/2026-10-04-cvc5-budget.md`), so that the limit and not the clock
ends most of its queries too.

- `unsat` from cvc5 is trusted as Z3's is: the query is unsatisfiable, and rung 1 goes on to its next query,
  which is asked of Z3 first and of cvc5 if Z3 gives up.
- `sat` is never a verdict. The values cvc5 gives the query's Bool and bit-vector constants are asserted
  beside the query, Z3 completes the model under its own limits, and that model is replayed in the
  interpreter exactly as a model Z3 found itself (sections 1 and 5; ADR 0014, ADR 0026). Only the replay
  makes a Divergent or an Unknown(abstraction).
- Anything else leaves the query the timeout it was: an `unknown`, a script cvc5 cannot read, values that
  are missing or are not literals of the constant's sort, a model Z3 cannot complete, a process that fails.

Induction obligations (rungs 2 and 3), Horn clauses (rungs 4 and 5), contract queries and ADR 0037's
queries are asked of Z3 alone. With no solver configured nothing above happens and every result is what it
was. A run with cvc5 configured can therefore differ from one without; the results it touched say so
(`proofMethod`, above).

A rung 4 Spacer query gets ten times `resourceLimit`. Z3 counts a Spacer step far cheaper than a step
of the product queries the limit is sized for: the `loop-fusion` sample's proof spends 3 to 5 million
units in under two seconds, and the count moves from run to run.

A query that exhausts either is Unknown with reason `timeout` (`chc-timeout` on rung 4), and the detail ends
with the limit that was hit: `resource limit <n> hit` or `wall-clock limit <n> ms hit`. The budgets are per
query, and a pair asks several (section 5.1), so neither bounds the time a pair takes. The defaults come from
`docs/runs/2026-10-01-timeout-budget.md`.

A query that neither budget has ended once it has run four times `timeoutMs` is interrupted (ticket P2-076). It is
Unknown with reason `timeout` like the others, the ladder goes on to the next rung as it does after a timeout, and
the detail says `interrupted` in place of a limit. Only the one query is ended: nothing caps a rung, a pair or a run.

Every Unknown other than `unbound` also carries `properties.failureRefinement` (ADR 0037; tickets P1-013
and P1-035): `{ newFailures, removedFailures }`, each `{ outcome, model? }`. The backend asks two more
queries over rung 1's product (the pair with its shared fragments as calls, unrolled `k` times), comparing
only whether each side returns or throws, never the value or the heap. `newFailures` asks for an input on
which the legacy side returns and the modern side throws; `removedFailures` is the same with the sides
swapped. A side that reaches an unshared `IrOpaque`, or the bound of a looping pair, has an unknown outcome
(ADR 0014), so each asks first on inputs where neither side does. A model there whose replay has an
untainted outcome on both sides (ADR 0026) is `found`, carrying the model rendered as a Divergent's is; a
tainted one is `unknown`. Only when that query is unsatisfiable does it ask again, letting such a side
return or throw: unsatisfiable is `none-proved`, anything else `unknown`. A query the solver gives up on is
`unknown`, and so is every answer for a pair rung 1 could not encode. Each query gets the pair's resource limit and timeout.
A `found` answer needs an input that reaches no unshared opaque node, so on an Unknown it occurs only where
rung 1's model of the same divergence replayed tainted, or, on a `timeout` Unknown, where rung 1's query
gave up before it found one. A `timeout` Unknown keeps `unknownReason` `timeout` whatever the two queries
answer, and a `found` on it is not a Divergent. The verdict stays EQ003, and neither the rule id, the
exit code nor the fingerprint depends on `failureRefinement`. The census reports the Unknown pairs queried
and the time their queries took, in `loweringCensus.failureRefinement` (`pairs`, `milliseconds`), when
there was at least one.

An EQ002 the solver found, and an Unknown with reason `abstraction`, carries `properties.agreesWhen`
when the search of section 5 admitted a candidate (ADR 0048; ticket P1-022):
`{ smt, text, proposedBy, proofMethod }`. It is a predicate over the pair's shared inputs under
which the pair is proved Equivalent. `smt` is the exact statement, in SMT-LIB over the product's
inputs (`in.<legacy parameter name>`, and `in.null.<Sort>` for a null shadow), for example
`(not (select in.null.System.String in.name))`. `text` is the same in source spelling with the
modern side's parameter names (`name != null`, `count >= 0`); several admitted candidates are
joined by `||`. An unsigned operation casts its operands in `text`, and a widening C# makes
implicitly is not written, so `text` reads as C# for `int`, `long`, `bool` and reference
parameters. The IR does not record that a parameter was declared unsigned or narrower than `int`,
and for such a parameter `text` can read differently from `smt`. `proposedBy` is
`harvested-predicates` and `proofMethod` is `bounded` (ADR 0036). The message ends with
`Equivalent when <text>.`, ahead of a tested Unknown's sentence. A result with no admitted
candidate carries no `agreesWhen` and its message is unchanged. The claim is as modular as an
Equivalent's (ADR 0019): it holds under the `assumedCallees` and `unprovenAssumptions` the result
lists. It says nothing about the inputs outside the condition: not that the pair differs there,
and not that the condition is the weakest one. The verdict, the rule id, the level, the exit code,
the fingerprint, `baselineState`, the review group and the rank do not depend on it. If a result's
counterexample satisfies the condition proved for its pair, the result carries no `agreesWhen` and
the run carries one `warning` notification with the descriptor `contradicted-condition`. The census
reports the pairs searched, the pairs with a condition and the time the searches took, in
`loweringCensus.agreesWhen` (`pairs`, `admitted`, `milliseconds`), when at least one pair was
searched.

The IR text spells a whole-body opaque `opaque body "reason"` (`IrOpaque.WholeBody`), so scope is
read from the IR rather than guessed from the span.

A whole-body opaque's span is the construct that caused it (the `foreach`, the `lock`, the filtered
`catch`), not the method body. A method whose bound body holds a compiler error, an
`IInvalidOperation` or an error-type symbol is `Unknown(Unbound)`, with the diagnostics as its
causes, and is never Equivalent by congruence. Neither scope nor causes is part of the
fingerprint. The causes are the errors in source order, so the result's location is the modern
side's first error, and the message only says which side does not bind: it holds no path, because
the fingerprint hashes it. Three cases have no diagnostic in the method's own span (ADR 0029 as
clarified by ticket P2-085). A syntax error makes every method declared in its file unbound, at the
file's first syntax error, because where each declaration begins and ends is then the parser's
recovery. A constructor of a type whose base type did not resolve is unbound, since its bound body
leaves the base constructor call out. So is an accessor of a property or indexer whose type or
parameter type did not resolve. A method that binds without error is compared as it is bound, even
when it calls a member of a type with errors elsewhere: that callee's own pair is the Unknown, and
the caller lists it under `unprovenAssumptions` (ADR 0019).

Every run writes `run.properties.loweringCensus`: procedures per side, matched pairs, pairs
without `IrOpaque`, whole-body opaque pairs, congruent pairs, and `IrOpaque` counts by reason
per side (ADR 0027). With `--il-fallback` it also counts the pairs the fallback was tried on and
the pairs it replaced (`pairsIlFallbackTried`, `pairsLoweredFromIl`), and every result on a matched
pair carries `properties.lowering` (`operation` or `il`; ADR 0039). It also records skipped projects per side, and, when the run produced
verdicts, Unknown counts by scope (ADR 0029) and the failure-refinement time (ADR 0037).

Only the projects a solution builds are part of the product. For a `.sln`, those are the projects
with a `Build.0` entry for its default configuration (`Debug|Any CPU`, else the first one it
lists); a `.slnx`, or a `.sln` that lists no configuration, builds all of them. The others are
never opened, so they are neither loaded nor skipped, and every run names them once per side in
`run.properties.projectsNotBuilt` (`legacy`, `modern`). The project load rate (ADR 0028) is C#
projects loaded over C# projects built: skipped projects count against it, projects not built do
not (ticket P2-013). A modern project that does not compile but is compared method by method (below)
counts as loaded (ticket P2-085).

Every loaded project has a runtime, read from each side and never assumed (ADR 0040 decision 1;
ticket P2-053). A project's runtime is its compilation's `TargetFrameworkAttribute`: .NET Framework
4.x or .NET (Core), ordered with every .NET Framework version before every .NET (Core) version. A
multi-targeted project is the flavour that is analysed, its last (P2-016). A `netstandard` project,
or one whose target framework is neither, runs on its hosts: the executable and test projects on
the same side that reference it, directly or transitively. A test project is one that references
xunit, NUnit or MSTest. With no host it takes the side's `runtimes` entry from `equiv.config.json`,
`"runtimes": { "legacy": "<tfm>", "modern": "<tfm>" }`, where each value is a .NET Framework or .NET
target framework as a moniker (`.NETFramework,Version=v4.8`) or a short name (`net48`, `net8.0`);
anything else is `CFG009`. Otherwise it is unhosted and keeps its own target framework
(`netstandard2.0`, or `unknown` without the attribute). Every run lists the result in
`run.properties.runtimes` (`legacy`, `modern`): one `{ project, runtime, source }` per loaded
project, by assembly name, where `source` is `attribute`, `host`, `config` or `unhosted`. A project
with several hosts on different runtimes lists them all in `runtime`, in runtime order, separated
by `, `. The runtimes decide which runtime rules apply to each matched pair (section 3; P2-055) and
which runtime each side is executed on (P2-056).

Counts in the census are per lowered body of a matched pair. `procedures` counts, per side, the
matched pairs plus the removed (legacy) or added (modern) procedures. `opaqueByReason` counts the
bodies on each side that hold at least one `IrOpaque` with that reason, sorted by reason. A body is
whole-body opaque when it is one block that holds nothing but `IrOpaque`s flagged `WholeBody`, at
least one: one per cause, so an unbound body with several errors is one whole-body opaque body. A
body whose only instruction is an expression-level `IrOpaque` is not. A pair counts
under `pairsWholeBodyOpaque` when either side is (tickets M3-014 and P2-092). A matched pair whose lowering threw has
no lowered body, so it counts in `procedures` and `matchedPairs` but in neither
`pairsWithoutOpaque` nor `pairsWholeBodyOpaque`, nor in `opaqueByReason` (ticket P2-011).

The census also counts what the solver will see (ADR 0034; ticket M3-030). A lowered matched pair
is *changed* unless it is congruent: both bound fingerprints are equal, neither is runtime-sensitive
inside the pair's runtime interval, and neither body is unbound (ADRs 0024 and 0029; ticket M3-015). `pairsCongruent` counts the
congruent pairs. `changedPairs`, `changedPairsWithoutOpaque` and
`changedPairsWholeBodyOpaque` are `matchedPairs`, `pairsWithoutOpaque` and `pairsWholeBodyOpaque`
restricted to changed pairs; lowerable share is `changedPairsWithoutOpaque / changedPairs`.
`changedReasonSets` maps the sorted, `+`-joined union of both sides' opaque reasons to its number
of changed pairs, with `""` for a pair without opaque, so its counts sum to `changedPairs`.
`runtimeChangeCalls` has `callSites`, `distinctMembers` and `pairsWithAny`, each per side and over
every lowered matched pair, congruent ones included: the `IrCall`s whose callee identity a table row
matches inside the pair's runtime interval (ADR 0040; P2-055), the distinct callee identities among
them, and the pairs whose body on that side has at least one. Package version changes are not in the census; `tools/corpus/corpus.ps1 -Packages`
computes them from each side's restore output.

`externalCallees` (ADR 0035; ticket M3-033) is every BCL member a lowered body calls, not only the
ones `RuntimeChangeTable` already lists: per side, over every lowered matched pair (congruent ones
included, as `runtimeChangeCalls` counts), the distinct call identities whose target assembly is one
of the framework reference assemblies the project compiled against (a reference assembly carries
`ReferenceAssemblyAttribute`, as `ProjectEmitter` already tests for replay), never the solution's own
code or a NuGet package. Each entry pairs a member with its call-site count, sorted by count
descending then ordinally. `tools/corpus/corpus.ps1 -RuntimeDiff <slug>` takes the union of both
sides' most-called entries and runs `tools/runtime-diff` on each; a member it finds divergent becomes
a `runtime-changes.json` row with `source: measured` and a witness.

Every run also writes `run.properties.analysedLinesOfCode`: `legacy` and `modern`, one count per
codebase and never a total (ticket M3-014). The rule is the one in README's "Licence" section,
applied identically to both sides. It counts the lines that hold part of a C# token, in every C#
file the loaded projects compile (generated files included), and a file compiled by more than one
project once. `--dry-run` prints the same two numbers without verifying.

A pair whose verification throws (an encoder bug, a `Z3Exception`), or whose lowering throws
(ticket P2-011), has no result: a crash is
a fact about the tool, not a verdict about the code (ADR 0023). It is recorded as an `error`
entry in `invocations[0].toolExecutionNotifications` naming both identities, the invocation
has `executionSuccessful: false`, the run's `properties.unverified` lists the pair's identity,
and the other pairs are reported as usual. Its baseline result, if any, is carried through as
`unchanged` with `properties.unverified: true`, never as `absent`, so a crash cannot make a
known divergence look fixed.

A project that cannot be loaded is contained the same way, one level up (ADR 0029). A C# project
that fails to load or has unresolved references, and any project that is not C#, is skipped.
"Unresolved references" means the references themselves: a project MSBuild cannot open, or one
whose reference set the compiler rejects (CS0006, CS0518, CS1705, CS8032). On the legacy side it
also means a name no reference provides (CS0012, CS0234, CS0246, CS0400), since the shipped
application has none unless it was loaded wrongly. On the modern side those four do not skip the
project: that is what a migration tool's raw output looks like, so the project is loaded, each
method that does not bind is `Unknown(Unbound)`, every other method is compared, and the exit code
follows the verdicts (ticket P2-085; sample `partly-compiling-modern`). A skipped project
gets a tool-execution notification (`error` for C#, `warning` otherwise), and its procedures are
listed in `properties.unverified`. Their baseline results are carried as `unchanged` with
`properties.unverified: true`. No Added or Removed result is reported for a procedure whose
counterpart project, matched by assembly name, was skipped on the other side. Every other project
is analysed and reported as usual.

Baseline: SARIF `baselineState` (`new`, `unchanged`, `updated`, `absent`) computed from
a result fingerprint (procedure identity + verdict + model hash). The exit code considers
only `new` results unless `--no-baseline` is given. Accepting a divergence as the new
behaviour is done by committing the SARIF file as the baseline, nothing more.

`new` versus `updated` is decided by procedure identity *and* rule id (the verdict's kind,
EQ001-EQ005): a rule-id change for an identity already in the baseline — e.g. Equivalent
(EQ001) regressing to Divergent (EQ002) — is always `new`, so the exit code never misses it.
`updated` is reserved for a same-rule-id fingerprint change (e.g. a different counterexample
on a procedure that was already Divergent); that distinction is not exit-code-significant, so
it does not depend on the "model hash" half of the fingerprint being identical across runs of
the same underlying divergence.

Every flagged result, one whose rule id is EQ002, EQ003 or EQ006, belongs to a review group, and
the groups are ranked, so that a reviewer reads a short list of causes and not every method (ticket
P2-064; ADR 0006's 2026-10-01 clarification). The result carries its group's key in
`properties.reviewGroup` and its group's `rank`, SARIF's own result property (0 to 100, higher is
looked at first). No EQ001, EQ004 or EQ005 result carries either. The key is derived from what the
result already carries and reads every list as a set, so it is the same on every run of the same
inputs:
- EQ006: `runtime-change:` and the `member` of the runtime-changes row the message cites, for
  example `runtime-change:System.String::GetHashCode(`;
- EQ002: `calls:` and the call identities that only one side's trace in the counterexample holds,
  sorted and joined by `|`. Where the migration swapped one member for another, that is the two
  members: `calls:System.Convert::ToInt32(string)|System.Int32::Parse(string)`. When both traces
  call the same members it is `proofMethod:observed`, or `proofMethod:none` for a solver's
  counterexample, which carries no `proofMethod`;
- EQ003: the `unknownReason`, and for `opaque` and `abstraction` a `:` and the opaque reasons behind
  it, sorted and joined by `+`: the reasons of an `opaque` Unknown's causes, and the `reason` of each
  opaque fragment among an `abstraction` Unknown's `abstractions` (an `IrPure` operator adds
  nothing). For example `abstraction:DelegateCreation`, `opaque:Await+Lambda`, `timeout`.

A group is a rule id and a key, and every result in it has the same rank, which comes from the
group's tier and size. The tiers, highest first:
1. a Divergent the real runtimes showed, EQ002 or EQ006: `proofMethod: observed`, or `replay: reproduced`;
2. any other EQ002;
3. any other EQ006;
4. EQ003 with `scope: line`;
5. EQ003 with `scope: method`.

`replay` and `scope` are per result, so a group takes the best tier any of its results is in. The
rank is `20 x (5 - tier) + min(count, 9999) / 500`: tier 1 is 80.002 to 99.998, tier 5 is 0.002 to
19.998, and inside a tier a larger group ranks higher. It depends on the group alone, never on the
run's other groups.

Every run that is not `--lower-only` writes `run.properties.reviewList`: one entry per group,
highest rank first and equal ranks by `group`, then `ruleId`. An entry has `group`, `ruleId`, `rank`,
`count` and `identities`, the procedure identities of its first five results in result order. The
counts sum to the number of EQ002, EQ003 and EQ006 results in the log. That includes a baseline's
carry-overs (`absent`, or `unchanged` with `unverified: true`), which keep the `reviewGroup` they
were written with; one from a log written before this property has the group `ungrouped`. A
`--lower-only` run writes no list, and its results (an `unmatched-overload` Unknown) still carry
`reviewGroup` and `rank`.

`equiv compare` prints the list on stdout after the analysed line counts: `review list: <G> groups
for <R> flagged results`, then the ten highest-ranked groups, one line each, as
`  <ruleId> count=<n> rank=<rank> <group>`. These lines count only `new` and `updated` results, so
against a baseline they list what the run changed, and a group with no such result has no line.
`equiv mcp`'s `compare` returns the same lines in its summary, after the verdict counts, and never
on stdout (ADR 0033). No verdict, rule id, level, fingerprint, `baselineState` or exit code depends
on a group or a rank.

`level` is only meaningful on a result when `kind` is `fail` (SARIF 2.1.0 s3.27.9), so
EQ003-EQ005 results carry `level: none`; the parenthesised value is the rule's
`defaultConfiguration.level`, severity metadata only. Whether a consumer renders Unknown with
a badge is not guaranteed; the gate for Unknown is `--fail-on unknown`. See ADR 0011.

## 7. Test obligations derived from this document

- Soundness harness (property test, `Equiv.Verify.Z3.Tests`): for any generated IR
  procedure P, `verify(P, P)` is Equivalent; for P and a random semantics-changing
  mutation P', the verdict is Divergent or Unknown, never Equivalent. Mutations include
  dropping or changing a map write (the final heap is observable) and duplicating a call
  whose results are compared (calls are not idempotent; ADR 0018). Runs against
  every ladder rung independently. It generates IR, so it covers the encoder and the
  ladder only: a C#-to-IR lowering gap is invisible to it by construction, and the
  section 2 heap gaps are exactly that (ADR 0015). The obligation that covers C#-to-IR is
  the lowering oracle below, which P1-005 and P1-006 each extend with the case that
  catches its own gap.
- Ladder monotonicity (property test): a pair proved on rung n is never refuted on
  rung m; a counterexample from rung 1 replays to Divergent in the IR interpreter.
- Lowering oracle (property test, `Equiv.Frontend.CSharp.Tests`): for generated
  straight-line integer methods, compile and run the C# in memory and run the IR via
  `IrInterpreter` (production code in Core, also used to replay counterexamples);
  outputs agree.
- Differential soundness (property test, M0-012; `DifferentialSoundnessTests` in
  `Equiv.Tests.Integration`): the two obligations above check the encoder against IR and
  the lowering against IR, so neither sees a false Equivalent that enters between C# and
  the verdict. This one closes that loop on generated code. `PairGen` generates a C#
  method and a second one derived from it by one mutation operator, from a preserving
  family or a changing family; both are compiled and run on the CLR, and the real
  frontend and Z3 backend verify the pair. Over each pair and its inputs (CsCheck's, and
  the model of a Divergent verdict):
  1. if any input gives different observables (return value, exception type, field and
     array state), the verdict is not Equivalent;
  2. if the verdict is Divergent, replaying its model in C# gives different observables;
  3. if the operator is preserving, the verdict is not Divergent.

  Rule 1 is the soundness rule; rules 2 and 3 are the decoding and precision rules. A
  changing operator can produce an equivalent mutant, so no rule assumes a mutant differs.
  200 pairs per PR, 5,000 nightly. Each pair is verified twice: once lowered from IOperation,
  and once with both sides forced through the IL lowering (ADR 0039; P1-017). Both verdicts
  are held to all three rules. A model whose run records an ordinary call also fixes that
  call's outputs, which no C# argument can (ADR 0026, clarifications of 2026-09-30). So rule 2
  does not count a replay of such a model that fails to diverge. That includes a closed call
  (ADR 0041): it writes no heap, but its result and `threw` flag are still the model's choice.
- Snapshot tests (Verify): IR dump and SARIF for every sample in `samples/`.
- Congruence (property test, ADR 0024): whenever congruence reports Equivalent on a
  generated or sample pair, the solver on the same pair never reports Divergent.
- Taint (ADR 0026): no Divergent result's differing observable is tainted.
- Containment (ADR 0029): a solution with one unloadable project reports every other project's
  results, and an unbound method is Unknown(Unbound), never congruent. No pair is Equivalent when
  either body holds an error, one test per error kind (`UnboundNeverEquivalentTests`, ticket P2-085). Residual claim (property
  test): for a `line`-scoped Unknown, every generated input on which neither side reaches a listed
  cause gives equal observables in `IrInterpreter`.
- Input conditions (property test, ADR 0048; `ConditionSoundnessTests`, ticket P1-022): on the pairs of
  the differential soundness gate, for a pair whose verdict carries `agreesWhen`, every generated
  input that satisfies the condition gives equal observables on the CLR. The condition is evaluated
  as the C# its `text` is, so a text that is not a predicate over the modern side's parameters fails
  too. 200 pairs per PR, with the gate's seed.
- Every row in the tables above has at least one unit test named after it.
