# P2-096 A filter loop and `Where(...).ToList()` are compared, not abstracted
Status: todo
Effort: L
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: P2-049

## Goal
P2-049's sample `cleanup-modern-syntax` rewrites a filter loop into LINQ:

```csharp
// legacy
var result = new List<int>();
foreach (var value in values) { if (value > 0) { result.Add(value); } }
return result;
// modern
return values.Where(value => value > 0).ToList();
```

Today `Tidy.Positives(List<int>)` is Unknown(abstraction): "the divergence depends on
delegate:...". The legacy side is a loop of `GetEnumerator`, `MoveNext`, `get_Current` and `Add`
calls. The modern side is two calls, `Enumerable::Where` and `Enumerable::ToList`, whose results are
uninterpreted. Nothing relates the two, so the solver's candidate counterexample rests on what the
lambda's delegate is taken to do, and the verdict is Unknown, not Divergent (ADR 0026). This is a
very common cleanup, and the existing sample `loop-to-linq` avoids it:
its modern side writes out the loop `Count` runs, because "the call itself would be a callee the
verifier cannot see into".

Decide, through `equiv-adr`, whether `equiv` models LINQ-to-objects operators, and how. Candidates:
a summary per operator in the shipped equivalence table (ADR 0020), which rewrites a `Where` and
`ToList` chain to the loop it runs; a contract the loop is proved against; or leaving it Unknown and
saying so in VERIFICATION-MODEL. The observable call trace (ADR 0018) is the hard part: the loop's
enumerator and `Add` calls are trace events and the LINQ calls are others, so the decision has to
say which of them stay observable.

## Spec references
ADR 0018 (the call trace), ADR 0020 (the API equivalence catalogue), ADR 0026 (abstraction and
replay), ADR 0041 (closed calls), VERIFICATION-MODEL sections 3 and 5 (loops; rung 4 is how
`loop-to-linq` is proved), `samples/loop-to-linq/README.md`, P2-071 (effect-free BCL calls in the
trace).

## Acceptance criteria (all must hold; nothing beyond them)
1. The `equiv-adr` outcome is merged before any code change. It names the operators covered. At
   least `Where` followed by `ToList` over a `List<T>` or an array is decided one way or the other.
2. If the outcome is to model them: `Tidy.Positives(List<int>)` in `samples/cleanup-modern-syntax`
   is Equivalent, and the SARIF names what was applied (`properties.equivalencesApplied`, or the
   property the decision names). The sample's snapshot and README row are updated.
3. If the outcome is to model them: a variant whose lambda is `value >= 0` on the modern side is
   Divergent, or Unknown with a reason the decision names, and never Equivalent.
4. If the outcome is to leave it Unknown: VERIFICATION-MODEL says so, the sample's README row names
   the ADR instead of this ticket, and no code changes.

## Tests
- Integration test on `samples/cleanup-modern-syntax` and on the variant of criterion 3.
- Unit tests the decision names, in the affected test projects.

## Size guard
More than the operators the decision names: stop. Deferred execution observed by the caller (a
`Where` result that is returned without `ToList`, or enumerated twice): stop and file a ticket.

## Out of scope
`IQueryable` and translated queries (P2-026). Query syntax. PLINQ. Operators that take an
`IEqualityComparer` or an `IComparer`.

## Notes
- Found by P2-049 (`samples/cleanup-modern-syntax`, `Tidy.Positives`). Review group `abstraction`.
- Both sides of the sample keep the same guard for a null list. Without it the pair is truly
  Divergent: the loop throws `NullReferenceException` and `Where` throws `ArgumentNullException`.
