# P2-031 Verifying crashes when the two sides' `this` is typed at different points in the hierarchy
Status: todo
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
