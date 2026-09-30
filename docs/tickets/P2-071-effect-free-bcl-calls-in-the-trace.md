# P2-071 An effect-free BCL call is not an observable call-trace event
Status: todo
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
