# P2-001 `new T[n]` and array initialisers are lowered, not opaque
Status: todo
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P1-006

## Goal
M3-022's census found `ArrayCreation` in the top fifteen opaque reasons of every agent pair (10
bodies over three pairs), and no ticket owned it. Any method that allocates an array is Unknown.
Minimal repro:

```csharp
static int[] Pair(int a, int b) { var r = new int[2]; r[0] = a; r[1] = b; return r; }
static int First(int n) => new[] { n, n + 1 }[0];
```

After this ticket, `IArrayCreationOperation` with one dimension and an `int` length lowers to a
fresh array value: a `System.OverflowException` edge when the length is negative, `length` set to
the length, and every element the default of the element type, or the initialiser's values in order.

## Spec references
VERIFICATION-MODEL section 2 (heap maps); ADR 0015; P1-006 (array maps keyed by the array value);
`docs/tickets/IOPERATION-COVERAGE.md` row `ArrayCreation`; the `equiv-extend-ir` skill.

## Acceptance criteria (all must hold; nothing beyond them)
1. The two repro methods lower with no `IrOpaque`, snapshot-tested.
2. A negative length branches to `IrThrow("System.OverflowException")`, unit-tested.
3. A multi-dimensional or jagged creation stays `IrOpaque("ArrayCreation")`.
4. The coverage-table row names the tests.

## Size guard
If this needs a new IR instruction, stop and route it through `equiv-adr`.

## Out of scope
Multi-dimensional arrays; `stackalloc`; collection expressions.

## Notes
