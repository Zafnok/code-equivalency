# P2-140 The uses of a caught exception are counted, and the largest one lowers
Status: done (PR #476)
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
The exception object a `catch` variable or a `when` filter reads is not modelled: every read of it
is an opaque node with reason `CaughtException` (`IOPERATION-COVERAGE.md`), and no ticket owns the
reason. P1-028's row (`docs/runs/2026-10-07-opaque-tail.md`), over the three large runs' 2,246
changed pairs:

| Reason | gitextensions-8522: bodies (legacy / modern), in, alone | gitextensions-9860 | jellyfin-13023 | Sum: in | Sum: alone | Marginal unlock |
|---|---|---|---|---|---|---|
| `CaughtException` | 125 / 124, 46, 11 | 124 / 124, 35, 8 | 228 / 228, 32, 5 | 113 | 24 (1.1%) | 51 (2.3%) |

The marginal unlock is the changed pairs that hold this reason and otherwise only reasons an open
ticket owns. The later censuses in the same report give 41 of 1,743 (2.4%). It also comes with two
smaller reasons of the same region of the lowering, `call-throw-in-try` (in 21 changed pairs) and
`rethrow` (in 8). Count what bodies do with the exception they catch, and lower the largest use.

## Spec references
`docs/tickets/IOPERATION-COVERAGE.md` rows `CaughtException`, `Try`, `Throw`;
VERIFICATION-MODEL.md, the sections on exceptions and on the call trace; ADR 0034 (per-ticket unlock
rule); `docs/tickets/done/M4-008-*.md` (filters); `docs/runs/2026-10-07-opaque-tail.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Before any code, split the `CaughtException` opaque nodes of `gitextensions-8522`'s changed pairs
   (a `--lower-only` run) by what reads the exception: an argument of a call (a logger, a message
   box), the inner exception of a `throw new T(..., ex)`, a property read (`Message`, `HResult`,
   `InnerException`, another), a `when` filter, `throw ex`, a type test or cast, an assignment to a
   local or field, other; and by what raised it where the body shows that: a call, a `throw` in the
   same `try`, either. For each use give the nodes, the changed pairs it is in, and the changed
   pairs it alone keeps opaque. Counts in `## Notes` (uses and exception type names only).
2. The use with the most changed pairs alone lowers to IR. The exception a `catch` receives becomes
   a value both sides can name: the object a `throw` in the same `try` raised where there is one,
   and otherwise a value that is a function of the call that threw, so that two sides that catch
   after the same calls hold the same exception. Decide the representation with `equiv-decide` and
   log it; a new IR node, a new sort or a change to what a `threw` edge carries goes through
   `equiv-adr`'s bar test first.
3. A test per lowered use in `tests/Equiv.Frontend.CSharp.Tests`, one pair that must stay Divergent
   because the two sides pass different exceptions on, the form added to
   `LoweringOracleTests.LoweredIrAgreesWithCompiledCSharp`'s generator, and the
   `IOPERATION-COVERAGE.md` rows updated.
4. On a re-run, the changed pairs of `gitextensions-8522` that hold `CaughtException` fall by at
   least 2% of changed pairs, or `## Notes` records why not.
5. Each remaining use at or above 5% of changed pairs alone is filed as a ticket.

## Files
`src/Equiv.Frontend.CSharp/Lowering/`, `src/Equiv.Core/` and `src/Equiv.Verify.Z3/` only as
criterion 2's decision requires (`equiv-extend-ir`), their tests,
`docs/tickets/IOPERATION-COVERAGE.md`, `docs/ROADMAP.md` (criterion 5 only).

## Tests
Named in criterion 3.

## Size guard
If criterion 2 needs an accepted ADR and none exists when the split is done, write the ADR, record
the split and stop: the lowering is then its own ticket. More than one use lowered means the ticket
was misread.

## Out of scope
`call-throw-in-try` and `rethrow`, unless the lowered use cannot be written without one of them.
Stack traces, and any property of the exception whose value depends on where it was thrown.

## Notes
- Found by P1-028 (`docs/runs/2026-10-07-opaque-tail.md`): fifth by marginal unlock.
- **Criterion 1: the split (2026-10-11).** Census (`--lower-only`) of `gitextensions-8522` (legacy
  `3f4ed21998af`, modern `5190ba5c1a5f`) at equiv `1f9bb699`, exit 5 (two bodies fail to lower, see Findings),
  130 s: 13,592 matched pairs, 12,654 congruent, 936 changed, 488 without opaque. `CaughtException` is in 40
  changed pairs (4.3%) and is the only reason in 16 (1.7%); the 40 pairs hold 43 nodes, one per `catch` that
  declares a variable (a node is the read of the exception into that variable, so a `catch` counts once
  whatever its body does). The split comes from a second census with a throwaway build that named, in each
  node's reason, every use of the variable, what the `try` holds and the declared type; that build was never
  committed and its output stays under `.corpus/`. A node counts under each use its variable has, so the node
  column sums to more than 43. "Alone" is the changed pairs whose every opaque is a `CaughtException` whose
  variable has that one use.

  | Use of the variable | Nodes | Changed pairs in | Changed pairs alone |
  |---|---|---|---|
  | property read `Message` | 21 | 21 | 9 (1.0%) |
  | inner exception of a `throw new T(..., ex)` | 12 | 11 | 2 (0.2%) |
  | argument of a call | 5 | 5 | 1 (0.1%) |
  | a call on it (`ToString`) | 1 | 1 | 1 (0.1%) |
  | `when` filter | 1 | 1 | 1 (0.1%) |
  | interpolated-string hole | 3 | 1 | 0 |
  | property read `InnerException` | 2 | 2 | 0 |
  | operand of a binary operator | 2 | 2 | 0 |
  | property read `NativeErrorCode` | 1 | 1 | 0 |
  | property read `Data` | 1 | 1 | 0 |
  | captured by a lambda | 1 | 1 | 0 |
  | `throw ex` | 1 | 1 | 0 |
  | type test or cast, assignment to a local or field, never read | 0 | 0 | 0 |

  Two more pairs are alone with a variable that is both a `Message` read and an inner exception, which makes
  the 16. By what the `try` holds: a call and no `throw`, 40 nodes in 37 pairs (15 alone); a call and a
  `throw`, 3 nodes in 3 pairs (1 alone); a `throw` and no call, none. By declared type: `System.Exception`
  33 nodes in 32 pairs, and one node each (`ThemeException` two) for `Win32Exception`,
  `GitConfigurationException`, `UnauthorizedAccessException`, `SystemException`, `ThemeException`,
  `TargetInvocationException`, `TypeInitializationException`, `XmlException` and `IOException`.
- **Criterion 2: what lowers.** The `Message` read. A `catch` whose variable is only ever read as `e.Message`,
  in the handler and outside a `when` filter, a lambda and a local function, names a value; a variable nothing
  reads does too. Any other use leaves the clause opaque as before, which is also what keeps this to one use:
  `Message` does not depend on where the exception was thrown, and anything else that is handed the object
  (a logger, an inner exception, `ToString`) can read its stack trace, which is out of scope.
- Decision: the exception a call threw -> a by-ref input `thrown.System.Exception`, a map of the exception's sort read at that sort's token, which every call and shared fragment reads and writes through the heap pairs it already has; the handler's value is that cell read on the call's `threw` edge. Alternatives: an exception output on `IrCall` (a new definition on a Core record that every backend, the unroller, the interpreter and replay would read), an `IrPure` of the callee and its arguments (not a function of the call's position, so two calls of one callee would throw one object: a false Equivalent), a second call event `caught:<callee>` (a trace event one side may lack). Rule: 4.
- Decision: bar test for that representation -> row 1, a clarification of ADR 0018, added in this PR: it adds no IR node, no sort and nothing to a `threw` edge, and "what a call leaves" already is a function of callee, arguments, heap and position. Alternatives: a new ADR (the size guard's stop). Rule: 1.
- Decision: how a raise hands the exception to the clause -> one hidden SSA variable per naming clause, stored in a block of its own on each raise's edge, before any `finally` on the way runs (a `finally` may call, and its call writes the cell). Alternatives: read the cell in the handler (wrong after such a `finally`), write the thrown object into the cell (a heap write one side may lack). Rule: 3.
- Decision: a raise whose exception is not a value (a null dereference, a division, a cast, a pure operator, a closed call, which has no heap pairs) -> `IrOpaque` with reason `CaughtException` on that raise's edge only, so the handler is no longer opaque for the raises that are values. Alternatives: keep the whole clause opaque when the `try` holds any such raise. Rule: 4.
- Decision: a caught exception is never null -> its variable's null shadow is false, as a `new`'s is. Alternatives: read `null.System.Exception`. Rule: 1.
- Decision: where the pair tests live -> `tests/Equiv.Tests.Integration/CaughtExceptionEquivalenceTests.cs`, since the frontend's test project has no solver; the lowering tests are in `tests/Equiv.Frontend.CSharp.Tests` as criterion 3 says. Alternatives: a solver reference in the frontend tests. Rule: 4.
- **Criterion 4: the re-run (2026-10-11).** Same pair, same census, this branch: 936 changed pairs, 489 without
  opaque (was 488); `CaughtException` in 38 changed pairs (was 40), alone in 15 (was 16). The fall is 2 pairs,
  0.2% of changed pairs, not 2%. Why not: 17 changed pairs hold only clauses that read `Message`, and in 15 of
  them the handler now lowers but the `try` also holds a raise whose exception is not a value, so the body
  keeps one `CaughtException` node on that raise's edge. A third census with a throwaway build that named the
  raise (never committed): `System.NullReferenceException`, the null test of a receiver, is in all 15, and is
  the only such raise in 8 (5 of them alone); a closed call or a pure operator that may throw anything is in
  6, an index out of range in 1, a failed cast in 1. The other 23 pairs hold a clause with another use. So
  the ticket's one use cannot reach 2% on this pair: naming every raise as well would take it to 17 pairs
  (1.8%).
- **Criterion 5.** No remaining use is at or above 5% of changed pairs alone (the largest, the inner exception
  of a `throw new`, is 0.2%), so none is filed. The raises that are not values are under the bar too (naming a
  null dereference's exception would take the reason out of 8 more pairs, 0.9%) and are recorded here, not filed: a null dereference's exception
  would have to be one value per exact type that both sides share, which holds for `Message` only where the
  runtime's text for that type does not name the site (it does for a failed cast).
- **Findings.** Two bodies of `gitextensions-8522` fail to lower at `1f9bb699`, on both sides, before and
  after this change: `ICSharpCode.TextEditor.Document.HighlightColor::.ctor(System.Xml.XmlElement)` and
  `::.ctor(System.Xml.XmlElement,ICSharpCode.TextEditor.Document.HighlightColor)`, each with
  `ArgumentNullException` (parameter `method`). P2-139's census of `gitextensions-9860` at `feee17e1` exited 0.
  Filed as P2-158.
- The lowering oracle's generator gains `try { o.Check(x); } catch (System.Exception t) { z = t.Message != null; }`
  (only from `MethodWithLambda`, so `Method`'s draws are unchanged). The argument is a local on purpose: an
  expression there could overflow inside the `try`, and that exception is not a value.
