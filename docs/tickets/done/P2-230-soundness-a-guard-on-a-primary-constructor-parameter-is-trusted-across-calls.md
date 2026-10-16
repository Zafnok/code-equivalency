# P2-230 Soundness bug: a guard on a primary constructor's parameter is trusted across a call that rewrites it
Status: done (PR #527)
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
P2-074's guard rule (`ValidPaths.IsGuarded`) treats a parameter as valid where `File.Exists` or `Directory.Exists`
returned true for it, when nothing in the method assigns it. A primary constructor's parameter is captured by the
whole type: any member can assign it, and a call between the guard and the use can run that member. Found by P2-207,
which reads its own parameter rules only for the method that declares the parameter. A unit probe confirmed it on
`main` at `068d8d79`:

```csharp
class C(string p)
{
    void Reset() { p = "a|b"; }
    void M() { if (File.Exists(p)) { Reset(); File.Delete(p); } }
}
```

`File.Delete(p)` is not flagged, though `p` is `"a|b"` there. No corpus pair has a primary constructor today, so no
run result rests on it.

When done, a primary constructor's parameter is not read by the guard rule (or only where P2-217's rule for a field
would read it), and the shape above is flagged.

## Spec references
`docs/tickets/done/P2-074-*.md`, `P2-207-*.md` (`ValidPaths.IsWrittenInPlace`), `P2-217-*.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. The shape above is flagged, and a guard on an ordinary method's parameter still clears its use.

## Tests
The unit test above, and the existing guard tests unchanged.

## Size guard
Anything past the guard rule: stop, that is not this ticket.

## Out of scope
Other rules' handling of captured variables.

## Notes
- Filed 2026-10-16 by P2-207 (found in passing).
- Decision: the guard rule reads a symbol only when its declaration lies inside the member that reads it
  (`ValidPaths.IsGuarded`), rather than reading a primary constructor's parameter by P2-217's quiet-between rule for
  fields. Every write of such a symbol is then under the root `LocalValues.Writes` scans; a primary constructor's
  parameter is declared on the type, so it is never read, while a lambda's parameter, declared inside the member,
  still is (the stricter `IsWrittenInPlace` check would have dropped it). The exclusion also covers a primary
  constructor's parameter read in a field initializer; no corpus pair has a primary constructor, so no result moves.
