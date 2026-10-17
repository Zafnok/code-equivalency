# P2-233 Soundness bug: a guard is trusted in a local function that is called before the guard runs
Status: in-progress
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
P2-074's guard rule (`ValidPaths.IsGuarded`) treats a parameter or local that nothing assigns as valid where
`File.Exists` or `Directory.Exists` returned true for it, walking up from the use through every enclosing operation.
It does not stop at a local function. A local function is in scope in its whole block, so it can be called before an
early-exit guard that comes earlier in the block than its declaration. Found by P2-208, whose own rules stop there;
a unit probe confirmed it on `main` at `c74d3068`:

```csharp
class C
{
    object M(string p)
    {
        var r = L();
        if (!File.Exists(p)) return null;
        return r;
        object L() => new StreamReader(p);
    }
}
```

The call `L()` gets a fingerprint (its code is taken as not runtime-sensitive, `LocalFunctionCallLoweringTests`'s
`Opaques`), though `new StreamReader(p)` runs before the guard; without the `if` it gets none. A lambda is not
affected: it runs only after the statement that makes it. P2-217's guarded member read and P2-207's last write already
refuse a use in a lambda or local function.

When done, the guard rule does not cross a local function's boundary (or reads only guards inside it), and the shape
above gets no fingerprint.

## Spec references
`docs/tickets/done/P2-074-*.md`, `P2-217-*.md` (`ValidPaths.PassedBefore`), `P2-171-*.md` (a local function call's
fingerprint).

## Acceptance criteria (all must hold; nothing beyond them)
1. The shape above is flagged (its call has no fingerprint), and a guard inside the local function, or on a use in the
   method itself, still clears it.

## Tests
The unit test above, and the existing guard tests unchanged.

## Size guard
Anything past the guard rule: stop, that is not this ticket.

## Out of scope
Other rules' handling of local functions.

## Notes
- Filed 2026-10-16 by P2-208 (found in passing).
- Decision: the guard rule skips only the one step that crosses from a local function to the block declaring it, not
  every guard above the function. Calls (and any delegate made from the function) can only be in that block, so an
  `if` around the block, or an early exit before the block in an outer one, still runs first; only the block's own
  early exits can come after a call. A guard before both the call and the declaration in that block is given up
  (not read), as nothing short of reading every call site shows it runs first.
