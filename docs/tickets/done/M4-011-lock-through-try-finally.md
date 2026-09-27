# M4-011 `lock` through its `try`/`finally`
Status: done (PR #224)
Effort: S
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M4-003

## Goal
Split out of M4-003, whose size guard tripped on it. `lock (o) { ... }` is still one whole-body opaque with
reason `lock`. With `ref` arguments lowered (M4-003), deleting the whole-body check lowers `lock` through the
CFG's `Monitor.Enter(o, ref lockTaken)` / `finally { if (lockTaken) Monitor.Exit(o); }`, except for one thing:
Roslyn's CFG never assigns the synthesized `lockTaken` local, so the `ref` argument reads an `undefined` value
(an `IrOpaque`). The compiled code sets `lockTaken = false` on every entry to the `lock`, a loop iteration's
included. After this ticket, a local the compiler declares for a region starts at its type's default each time
the region is entered, and `lock` lowers with no opaque.

## Spec references
VERIFICATION-MODEL sections 2 and 3; M4-003's Notes; `docs/tickets/IOPERATION-COVERAGE.md` row `Lock`; the
`equiv-extend-ir` skill.

## Acceptance criteria (all must hold; nothing beyond them)
1. An implicitly declared local of a `ControlFlowRegion` (`ILocalSymbol.IsImplicitlyDeclared`) is stored its
   type's default on every entry to that region. A user-declared local is unchanged.
2. `lock (o) { ... }` lowers through the CFG with no special case. The whole-body `lock` check and the `lock`
   row of `WholeBodyReasonOwners` are deleted.
3. The coverage table row `Lock` is updated. On `business-layer`, `Record` lowers with no opaque. Its README
   and census snapshot are updated.

## Files
`src/Equiv.Frontend.CSharp/Lowering/*`, `tests/**`, `docs/tickets/IOPERATION-COVERAGE.md`,
`samples/business-layer/*`.

## Tests
`LockLowersThroughTryFinally` (the interpreted run with `Monitor.Enter` answering `lockTaken = true` returns the
body's result and calls `Monitor.Exit`), `LockInALoopStartsUntakenEachIteration`, the `EntirelyOpaque`
snapshot moved to another whole-body reason.

## Size guard
If criterion 1 needs more than a store at region entry, stop.

## Out of scope
`Monitor.Enter` modelled beyond an opaque call. `lock` on a `System.Threading.Lock` (C# 13), which binds
`EnterScope` instead.

## Notes
- Decision: "entry to a region" is lowering the region's first block (walking out through every enclosing region that
  starts at that block). Roslyn's regions are entered only through their first block, and `lock`'s local-lifetime region
  starts with the capture of the lock object, so no back edge inside it lands there.
- Decision: the default is `TypeMapper.Default(...)!`. The CFG builder's only implicitly declared local is `lock`'s
  `bool lockTaken`, so the type always has a constant default and there is no reference shadow to store; a branch for a
  type without one would be dead code under the 100% branch gate.
- Decision: the whole-body examples that used `lock` moved to `iterator` (`EntirelyOpaque`, the span test) and to `extern`
  (`no-body`, for the by-ref outs test and the API-equivalence modern-return-sort test, since an iterator cannot have
  `ref` parameters or return a `Task`).
- `Monitor.Exit` has two call sites per `lock` in the census: the `finally` is copied onto the normal and the exceptional
  exit.
- Locally, `webapi-basic`'s legacy solution did not load (System.Web.Http unresolved: its packages were not restored in
  this worktree); unrelated to this ticket, CI restores them.
