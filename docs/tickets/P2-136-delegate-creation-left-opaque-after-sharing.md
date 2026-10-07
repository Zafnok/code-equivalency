# P2-136 The delegate conversions P2-067 left opaque are counted, and the largest cause is removed
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
A lambda or method group converted to a delegate that has no shareable fingerprint is an opaque node
with reason `DelegateCreation`. P2-067 made the shareable ones an `IrPure` and is done, and the
reason is still the largest one without an open owner. P1-028's row
(`docs/runs/2026-10-07-opaque-tail.md`), over the three large runs' 2,246 changed pairs:

| Reason | gitextensions-8522: bodies (legacy / modern), in, alone | gitextensions-9860 | jellyfin-13023 | Sum: in | Sum: alone | Marginal unlock |
|---|---|---|---|---|---|---|
| `DelegateCreation` | 275 / 271, 107, 50 | 233 / 237, 75, 22 | 197 / 197, 156, 53 | 338 | 125 (5.6%) | 257 (11.4%) |

The marginal unlock is the changed pairs that hold this reason and otherwise only reasons an open
ticket owns. The same report's later censuses put it at 135 of 1,743 (7.7%), 133 of them on the two
Git Extensions pairs: Jellyfin's share was lambdas made runtime-sensitive by rows that had no change
point, and P2-113 removed it.

P2-067's split of 213 pairs, made before its own fix, named the causes: a runtime-sensitive lambda
body (41), a capture written after the delegate is created or by another lambda or local function
(15), a lambda whose body differs between the sides (12), a method group of a local function (5),
a lambda calling a local function declared outside it (4). Nobody has counted them since. Count
them, and remove the largest.

## Spec references
`docs/tickets/IOPERATION-COVERAGE.md` row `DelegateCreation` and the shared-fragments paragraph above
the table; ADR 0024 decision 2; ADR 0034 (per-ticket unlock rule);
`docs/tickets/done/P2-067-delegate-creation-owner.md` (its split and its decisions);
`docs/tickets/done/P2-127-soundness-local-function-call-is-an-unverified-callee.md`;
`docs/runs/2026-10-07-opaque-tail.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Before any code, split the `DelegateCreation` opaque nodes of `gitextensions-8522`'s changed
   pairs (a `--lower-only` run) by why the conversion has no shared fingerprint: runtime-sensitive
   body, capture stored after creation, capture written by a lambda or local function, body that
   differs between the sides, method group with an evaluated receiver (`o.M`), method group of a
   local function, lambda that calls a local function, other. For each cause give the nodes, the
   changed pairs it is in, and the changed pairs it alone keeps opaque. Counts in `## Notes` (causes
   and delegate types only).
2. The cause with the most changed pairs alone is removed: the conversion lowers to IR, or shares its
   fragment, without the pair being reported Equivalent on anything the two sides do not both
   compute. Decide the representation with `equiv-decide` and log it; if it changes what a shared
   fragment may hold (ADR 0024 decision 2), go through `equiv-adr`'s bar test first.
3. A test per removed cause in `tests/Equiv.Frontend.CSharp.Tests`, one pair that must stay
   Unknown or Divergent because the two delegates differ, the form added to
   `LoweringOracleTests.LoweredIrAgreesWithCompiledCSharp`'s generator where it lowers, and the
   `IOPERATION-COVERAGE.md` row updated.
4. On a re-run, `changedReasonSets["DelegateCreation"]` on `gitextensions-8522` falls by at least 2%
   of changed pairs, or `## Notes` records why not.
5. Each remaining cause at or above 5% of changed pairs alone is filed as a ticket.

## Files
`src/Equiv.Frontend.CSharp/Lowering/`, its tests, `docs/tickets/IOPERATION-COVERAGE.md`,
`docs/ROADMAP.md` (criterion 5 only).

## Tests
Named in criterion 3.

## Size guard
If criterion 1 shows no cause above a third of the changed pairs alone, remove none: record the
split and stop. More than one cause removed means the ticket was misread.

## Out of scope
The IL fallback. Comparing two lambdas whose bodies differ as a pair of procedures, unless criterion
1 makes that the largest cause, in which case it starts with `equiv-adr`'s bar test. The
`LocalFunction` reason P2-127 added.

## Notes
- Found by P1-028 (`docs/runs/2026-10-07-opaque-tail.md`): the largest unowned reason by marginal
  unlock.
