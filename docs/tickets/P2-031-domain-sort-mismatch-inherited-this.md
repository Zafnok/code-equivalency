# P2-031 Verifying crashes when the two sides' `this` is typed at different points in the hierarchy
Status: in-progress
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
M4-007's first real run crashed on this shape of pair far more than any other single crash cause:
92 pair-level failures on Git Extensions, of which 47 are the exact message pattern
`domain sort <T> and parameter <U> do not match` (or `Sorts |...| and |...| are incompatible`)
where `<T>` is a derived class and `<U>` is its base — for example
`domain sort ICSharpCode.TextEditor.TextView and parameter ICSharpCode.TextEditor.AbstractMargin do not match`,
`domain sort GitUITests.Avatars.AvatarPersistentCacheTests and parameter GitUITests.Avatars.AvatarTestBase do not match`,
`domain sort GitUI.HelperDialogs.FormRemoteProcess and parameter sort GitUI.HelperDialogs.FormProcess do not match`.
The identical bug reproduced on the tiny `pmb-shiningrush__serviceant` corpus pair too — there the
matcher additionally paired the wrong two methods across an unrelated namespace:
`Verifying YiBan.Common.BaseAbpModule.Tests.Events.InProcessServiceBus_Test::GenericRequest_ShouldResponse() against YiBan.Common.BaseAbpModule.Tests.Events.InProcessServiceBus_Test::GenericRequest_ShouldResponse() failed: domain sort |...TestEventDataT`1| and parameter |ServiceAnt.Handler.TransportTray`1| do not match`.
Minimal repro (two classes in an inheritance relationship, a method inherited or overridden that
takes/returns the base type, called on the derived instance):

```csharp
class Base { public virtual int F(Base b) => 1; }
class Derived : Base { public override int F(Base b) => 2; public int G() => F(this); }
```

## Spec references
Whichever module computes a procedure's or a `this`/parameter's domain sort from its declaring or
static type (search for "domain sort" and "Sorts" error text in `src/Equiv.Verify.Z3`); ADR 0018
(inherited calls); `docs/tickets/M4-001-foreach-using-constructors.md`'s note on `InstanceReference`
lowering a base call to the containing type (may be the same code path).

## Acceptance criteria (all must hold; nothing beyond them)
1. Find the exact point where a call's static parameter type (a base class) and its argument's
   actual sort (a derived class) are compared for exact equality instead of being related through
   the class hierarchy (an upcast/subsort relationship) — this is very likely the same root cause
   across every occurrence in the run, not 47 separate bugs.
2. The minimal repro above verifies (Equivalent, since both sides are the same code) instead of
   throwing.
3. A regression test reproducing the exact `ServiceAnt`/`TransportTray` cross-pairing shape too, if
   it turns out to be the same bug and not a separate matcher bug (record whichever it is as a
   Decision in Notes).
4. This is the highest-value fix from this run: after landing, re-running `full` mode on Git
   Extensions should turn most of its 92 pair-level crashes into verdicts. Note the before/after
   crash count in this ticket's Notes when done.

## Size guard
If the fix needs a general subtyping/variance model for Z3 sorts (not just a narrow
upcast-at-call-boundary fix), stop and write an ADR instead.

## Out of scope
Multiple inheritance/interface dispatch beyond a single base class chain.

## Notes
- Root cause (criterion 1): not calls. A call's function is declared per argument-sort list
  (`TraceEncoder.Function`, `PureEncoder.Function`), so a derived-typed argument just names a
  different function. The mismatch is `HeapLowerer.Member`: a field (or inlined auto-property, or
  field-like event) is a map keyed by its *declaring* type's sort (`HeapInputs.Receiver`), but the
  receiver was lowered at its own static type. Roslyn wraps an argument in an implicit-reference
  `IConversionOperation` (which already reads `cast.<From>.<To>`, M3-010) but never a receiver, so
  `this.X` / `derived.X` for an `X` declared on a base keyed a `Base`-sorted map with a `Derived`
  term. `IrValidator` catches it in Debug; Release hands it to Z3, which throws
  `domain sort <Derived> and parameter <Base> do not match`. One fix point for every occurrence.
- Fix: `Member` upcasts the receiver through the same `cast.<Derived>.<Base>` map an explicit
  conversion reads when its sort differs from the map's key sort. The null check still runs on the
  un-cast operand's shadow (`this` and variables) and otherwise reads `null.<Base>` at the cast value,
  exactly as an explicit `((Base)d).X` would, so an explicit and an implicit upcast lower alike.
  No subtyping model for sorts was needed; the size guard did not trip.
- Decision: the ServiceAnt case is the same bug, not a matcher bug. Both sides are the same method
  (`YiBan.Common...` is ServiceAnt's own test namespace at e36009c, not a cross-namespace pairing);
  the crash is `testEventData.TransportEntity`, an auto-property declared on `TransportTray<TEntity>`
  read through a `TestEventDataT<TestEventData>` receiver. Regression:
  `InheritedThisEquivalenceTests.AGenericBaseAutoPropertyReadThroughAGenericDerivedReceiverVerifies`
  (fails without the fix, passes with it).
- Crash counts (criterion 4): before (M4-007, `docs/runs/2026-09-27-m4-007-*`): Git Extensions 92
  pair-level crashes, 47 of them this family; ServiceAnt 1, this family. After: not measured in this
  PR. A `full` Git Extensions run is ~2h37m plus a fresh `.corpus/` fetch and legacy build on this
  box, so the re-run is left to the next corpus run. Expected after: at most 45 on Git Extensions
  (the other causes are filed as P2-032, P2-033, P2-034 and P2-026, so "most of the 92" was never
  reachable from this ticket alone) and 0 on ServiceAnt.
