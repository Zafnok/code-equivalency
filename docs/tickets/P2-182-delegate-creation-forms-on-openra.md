# P2-182 The `DelegateCreation` forms that keep OpenRA's changed pairs opaque are counted, and the largest one lowers
Status: in-progress
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
P2-136 counted the forms behind `DelegateCreation` on `gitextensions-8522`, lowered the largest, and
found no other form at 5% of that pair's changed pairs. The reason has no open owner.

P2-130's run of `openra-17989` (equiv `e98fd1b9`, compare mode quick,
`docs/runs/2026-10-14-full-openra-17989/SUMMARY.md`), the pair's first run to a result, has
`DelegateCreation` alone in 31 of its 321 changed pairs (9.7%), above ADR 0028's 5%: the pair's
largest opaque reason set. It is in 9 Unknown results (8 `opaque`, 1 `abstraction`), and `delegate` is
the largest abstraction kind of the pair's `abstraction` Unknowns (43 of 99).
`IOPERATION-COVERAGE.md` row `DelegateCreation` names what stays opaque: a lambda whose capture is
stored after it, a conversion with no fingerprint, and a method group whose receiver is evaluated.

When done, the form that keeps the most of OpenRA's changed pairs opaque is lowered, or decided to
stay opaque, with the count that chose it.

## Spec references
`docs/tickets/done/P2-136-*.md` and `P2-067-*.md`, ADR 0025, `IOPERATION-COVERAGE.md` row
`DelegateCreation`, `.claude/skills/equiv-extend-ir`, `.claude/skills/equiv-corpus-run`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Count, on `openra-17989` at `main`'s head, the `DelegateCreation` nodes of the 31 changed pairs by
   form (P2-136's split), in and alone.
2. Lower the largest form, or record with `equiv-decide` why it stays opaque and lower the next.
3. Rerun `openra-17989` in compare mode quick and record, in Notes, `DelegateCreation` alone next to 31.

## Tests
`DelegateLoweringTests` for the lowered form, including two delegates that differ and must not be one
function.

## Size guard
A new IR node or sort: stop and route it through `equiv-adr`.

## Out of scope
Runtime-sensitive lambdas, which are their side's own function by ADR 0025.

## Notes
- Filed 2026-10-14 by P2-130 (criterion 6): an opaque reason at or above 5% of `openra-17989`'s changed
  pairs with no open owner.
- **Criterion 1: the split (2026-10-17).** Census (`--lower-only`) of `openra-17989`, legacy `fef7a018f2b2`,
  modern `5e74e58b2204`, equiv `82ba9c14` (`main`'s head), exit 0, 105 s, no project skipped. At this commit
  the census holds 297 changed pairs of 10,120 matched (P2-130's 321 were at `e98fd1b9`), 158 of them
  without opaque. `changedReasonSets["DelegateCreation"]` is 30 (P2-130: 31), and 37 changed pairs hold
  the reason at all. Each `DelegateCreation` opaque node of those 37 pairs was classified by a throwaway
  build that put, in each node's reason, every one of `FragmentFingerprinter`'s refusals that applies to
  the conversion, whether it is a method group with an evaluated receiver, or whether it was lowered as a
  function and made opaque again by `SsaBuilder`'s stored-after test (P2-136's method). That build was
  never committed, and its output stays under `.corpus/`. A node is one conversion on one side.

  | Form (P2-136's split) | Nodes | Changed pairs it is in | Changed pairs it alone keeps opaque |
  |---|---|---|---|
  | Lambda whose capture is stored after it | 135 | 23 | 17 |
  | Lambda that writes a variable it captures (a capture written by a lambda, the same one) | 43 | 14 | 4 |
  | Lambda whose capture another lambda or local function writes | 26 | 6 | 0 |
  | Method group with an evaluated receiver (`o.M`): a local 6, a field 4 | 10 | 4 | 3 |
  | Method group with an evaluated receiver that is runtime-sensitive | 1 | 1 | 0 |
  | Method group of a property's value that writes a variable declared outside it | 1 | 1 | 0 |
  | Runtime-sensitive body, method group of a local function, lambda that calls a local function, struct's `this` | 0 | 0 | 0 |
  | Total | 216 | 37 | 24, and 6 pairs with two forms at once |

  A changed pair counts under "alone" when its reason set is `DelegateCreation` and every such node in
  it has that one form. By delegate type, over the 216 nodes: `Func<TResult>` 98, `Func<T, TResult>` 44,
  `Action` 44, `Action<T>` 16, `Func<T1, T2, TResult>` 10, `Action<T1, T2>` 4; 204 are lambdas and 12
  method groups. The stored-after lambdas are OpenRA's widget and lint code: a lambda in a `foreach` body
  that captures a local the body declares, or the iteration variable itself, which the next iteration
  stores again. C# makes such a local a new variable each iteration (the compiled code allocates a new
  closure for it), so that store is to another variable, which the lambda never sees; `SsaBuilder`
  followed the loop's back edge and counted it.
- Decision: which form is lowered -> the lambda whose capture is stored after it (17 of the 30 pairs
  alone; the next form has 4). Alternatives: none reaches it. Rule: the criterion.
- Decision (criterion 2, `equiv-adr` bar test): a store the next iteration makes to a local the loop
  body declares is not "a local assigned after the fragment" -> row 1 of the bar test, a dated
  clarification of ADR 0024 decision 2; no new ADR. No Core contract, SARIF shape, rule id, verdict
  meaning or gate changes, and no IR node or sort is added. Alternatives: a new ADR. Rule: `equiv-adr`
  bar test.
- Decision: how the lowerer knows a store is to another instance -> a scope a loop enters afresh (a
  `LocalLifetime` region that declares a local a lambda names, whose first block is on a cycle and has
  every predecessor outside the region; `RenewedScopes`) renews its locals at entry with a step of
  `SsaBuilder` (`Renew`), and `StoredAfter` stops following a path for a variable once the path passes
  its renewal. A scope entered any other way keeps the old test, which is always sound. Alternatives:
  a Roslyn data-flow test in the frontend in place of the SSA one (a second analysis of what the
  lowering already stores); skipping the test for every local a loop body declares (unsound for a
  `goto` back to the scope's first label). Rule: 4.
- Decision: two iterations' closures -> the delegate also reads its scope's activation counter, a
  64-bit variable stored 0 at the start and one more at each entry of the scope, so the closures of two
  iterations are two values, as `Delegate.Equals` has them (two closures of one site differ in their
  target). Alternatives: no counter, which makes two iterations' closures of equal captures one value,
  so a pair whose sides differ only in declaring the local inside or outside the loop could be
  Equivalent; a new IR node for a closure's allocation (the size guard). Rule: 1 (what the encoder needs
  to tell them apart is an argument).
- Decision: shared fragments and local-function calls -> the same `StoredAfter` applies to them, with
  no counter: a fragment is a call event with its trace position (ADR 0024 decision 2), so two
  iterations' calls are already two events. Alternatives: keep the old test for them with a flag on the
  step (a second rule for one fact). Rule: 4.
