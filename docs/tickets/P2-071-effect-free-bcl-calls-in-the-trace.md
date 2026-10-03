# P2-071 An effect-free BCL call is not an observable call-trace event
Status: in-progress
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-047

## Goal
P2-047's audit found 3 false Divergents where the only difference is an effect-free BCL call. One
side reads `String.Length` once more. One side allocates an empty `ConcurrentBag<T>` where the other
allocated a different empty collection. One side constructs a parser from an upgraded library's new
class name. ADR 0018 puts every call in the observable trace, and each call's `threw` is free, so the
extra call and the constructor that "may throw" give a Divergent the real runtime cannot produce.
Minimal repro, as a sample pair:

```csharp
// legacy
static int F(string a, string b) { if (a == null || b == null) return 0; return a.Length; }
// modern
static int F(string a, string b) { if (a == null || b == null) return 0; _ = b.Length; return a.Length; }
```

Today this is EQ002. It should be Equivalent. Decide, through `equiv-adr` (a clarification on ADR
0018 is the likely outcome), which BCL members are effect-free and non-throwing: pure getters on a
non-null receiver, and parameterless constructors of collection types. The pure catalogue
(VERIFICATION-MODEL section 3) is the natural home. Such calls leave the trace, and their `threw` is
false.

## Spec references
ADR 0018, ADR 0041 (closed calls reach no heap), VERIFICATION-MODEL section 3 (the pure catalogue),
P2-047's audit.

## Acceptance criteria (all must hold; nothing beyond them)
1. The `equiv-adr` outcome is merged before any code change.
2. `samples/effect-free-bcl-call` (the pair above, and a pair that swaps `new List<int>()` for
   `new Collection<int>()` in a constructor that only stores it) is Equivalent.
3. A variant whose modern side calls a member with an effect (`list.Clear()`) stays Divergent.

## Tests
- Integration test on `samples/effect-free-bcl-call`.
- Unit tests the decision names.

## Out of scope
User-code purity analysis.

## Notes
- Found by P2-047: 3 false positives on Git Extensions.
- Decision (criterion 1, through `equiv-adr`): a new ADR, 0043, not a clarification of 0018. It adds a
  catalogue of members by name, which ADR 0041 had rejected in another form, and one assumption to
  VERIFICATION-MODEL section 1. The catalogue has one getter (`System.String::get_Length()`) and the
  parameterless constructors of 14 collection types declared in metadata. A use of either is no
  `IrCall`. `List<T>` and `Collection<T>` are one family: a new `Collection<T>` converted to an
  interface at once is a new `List<T>` there, assuming no code asks the object for its concrete type.
  Rejected there: a `length.System.String` map, a flag on `IrCall`, a rule over signatures instead of
  a list, one family for every collection, third-party constructors.
- Decision: how a catalogued getter is lowered -> an `IrPure` `get:<identity>` of the receiver.
  Alternatives: a `length.System.String` input map, as an array's length is. Rule: 3. An `IrPure`
  result is tainted (ADR 0026), which the differential gate's rule 2 needs: the solver picks a
  string's length freely, and a replay of that model builds a string of another length.
- Decision: how a catalogued constructor is lowered -> `new.<Sort>` at the body's count, the
  construct P2-001 made for arrays (`HeapLowerer.Fresh`). Alternatives: an `IrPure` of a counter.
  Rule: 4.
- Decision: where the family rule applies -> at the implicit reference conversion whose operand is
  the creation, when the target is an interface. Alternatives: at every use of a `Collection<T>`;
  only when the converted value is the right side of a field assignment. Rule: 4. The second
  alternative does not make the assumption smaller, since a later read of the field in the same body
  sees the same value.
- Decision: where the catalogue lives -> `Equiv.Frontend.CSharp.Lowering.EffectFreeMembers`, beside
  `ClosedCalls`, asked by both lowerings. Alternatives: entries in `PureCatalogue`, whose entries are
  operators keyed by `SpecialType`. Rule: 4.
- Decision: `_ = e;` -> its value, evaluated, and nothing stored. It was an opaque with reason
  `Discard`, so the ticket's pair was Unknown (`opaque`) before this change, not EQ002: the repro's
  extra read never reached the solver. Without this the pair cannot be Equivalent. Alternatives:
  writing the sample as an unused local. Rule: 1 (the ticket's pair, as written).
- Deviation: the IL lowering applies the getter and the constructors and not the family rule. A
  `newobj` carries no conversion, so there is nothing to apply it at. ADR 0043 says so.
  `IlLoweringParityTests.Known` lists the modern `Basket` constructor for it.
- Unit tests of the decision: `EffectFreeMembersTests` (a getter is an `IrPure` and no call, in both
  lowerings; each catalogued constructor reads `new.<Sort>` and is no call, in both lowerings; a
  constructor with an argument, a member that reads the heap, a user's type or getter and a
  source-declared type of a catalogued name are still calls; the family rule and where it does not
  apply; a discard assignment evaluates its value).
- Surprise: three checked-in outputs changed and no verdict did. `samples/unknown-new-throw` and
  `samples/cleanup-modern-syntax` lose `externalCallees` rows (`String::get_Length()`, the `List<int>`
  constructor), and the candidate counterexample of `cleanup-modern-syntax`'s Unknown names `new.*`.
  The snapshots `IrLowererSnapshotTests.SwitchOnTypePatterns` and `.NullCoalescingAssignmentToAField`
  show the pure function and the `new.<Sort>` read where they showed calls.
- Surprise: a Divergent that depended on the value of `String.Length` is now Unknown (`abstraction`).
  ADR 0043 lists it under Consequences. `ClosedCallsTests` used `s.Length` as a closed call and now
  uses `s.Trim()`.
