# P2-088 Decide whether an anonymous object stays opaque
Status: in-progress
Effort: S
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
`AnonymousObjectCreation` is an opaque node (P2-024 made it one, with a reason, instead of a crash).
On P2-065's run of `eshop-manual` it alone keeps 2 of 26 changed pairs opaque, 7.7%, which is over
ADR 0028's 5% line, and P2-024 is done, so it has no open owner. The sample is small. On the larger
pairs it is well under the line: `duplicati-3124` 3 of 936 alone (0.3%, in 25), `gitextensions-8522`
6 bodies in all.

This ticket is the owner the rule asks for, and its first job is to decide whether there is work to
do. `IOPERATION-COVERAGE.md` says a real lowering needs a record-like IR sort that does not exist
(P2-027 has the same gap for tuples).

## Spec references
`docs/tickets/IOPERATION-COVERAGE.md` rows `AnonymousObjectCreation` and `Tuple`, P2-024, P2-027,
ADR 0034 (per-ticket unlock rule), `docs/runs/2026-10-01-full-eshop-manual/SUMMARY.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. For the two `eshop-manual` pairs and the 25 `duplicati-3124` changed pairs that carry the reason
   (a `--lower-only` run each), record in `## Notes` where the object goes: passed to a call as an
   argument, returned, read back through its properties in the same method, or captured by a lambda.
2. If at least half are "passed to a call as an argument", lower that case: the creation is a closed
   call whose arguments are the property values in declaration order, identified by the property
   names, so two sides that build the same object from equal values agree. Snapshot test; the
   `IOPERATION-COVERAGE.md` row is updated. Decide the identity scheme with `equiv-decide`.
3. Otherwise change no code: write the split, and close the ticket as "stays opaque until a
   record-like sort exists", naming the ticket or ADR that would add one.

## Files
`src/Equiv.Frontend.CSharp/Lowering/` and its tests, only if criterion 2 applies;
`docs/tickets/IOPERATION-COVERAGE.md`.

## Tests
Named in criterion 2, if it applies.

## Size guard
A new IR sort is out of reach of this ticket: stop and go through `equiv-adr`.

## Out of scope
Tuples (P2-027). Reads of an anonymous object's properties.

## Notes
- Found by P2-065 (`docs/runs/2026-10-01-migrations-verdict.md`).
- **Criterion 1: the split (2026-10-02).** Census (`--lower-only`) of both pairs at equiv `46e6636`, with a
  throwaway build that recorded, for each changed pair whose reason set holds `AnonymousObjectCreation`,
  the span of each such opaque on each side. That build was never committed, and its output stays under
  `.corpus/`. The checkouts are P2-065's. `eshop-manual`: exit 0, 13 s; 28 changed pairs of 119, 11 without
  opaque; 2 changed pairs hold the reason, 1 of them alone (3.6%; the other now also holds
  `rebound-call`). `duplicati-3124`: exit 0, 62 s; 805 changed pairs of 6,275, 412 without opaque; 24
  changed pairs hold the reason (25 in P2-065's census at `ef79ff6`), 6 of them alone (0.7%); 55 bodies
  a side hold it. The two sides of every pair hold the same number of creations, 2 a side on
  `eshop-manual` and 45 a side on `duplicati-3124`, and each site was read in the legacy source.

  | Where the object goes | `eshop-manual` sites (pairs) | `duplicati-3124` sites (pairs) |
  |---|---|---|
  | Passed to a call as an argument | 2 (2) | 42 (23) |
  | Returned | 0 | 1 (1) |
  | The value of a property of another anonymous object | 0 | 2 (the same 1) |
  | Read back through its properties in the same method | 0 | 0 |
  | Captured by a lambda | 0 | 0 |

  So 44 of 47 sites and 25 of 26 pairs are "passed to a call as an argument", and criterion 2 applies.
  What takes the argument: on `eshop-manual` a route-values parameter and a JSON result, both `object`;
  on `duplicati-3124` 21 are an options-dictionary extension method's `object` parameter in the unit
  tests, 10 a REST response writer, 6 the generic `self` parameter of a process runner (which hands the
  object to a lambda of its own, so it is still an argument here), and 5 a JSON serializer. The one pair
  that is not an argument returns one object two of whose properties are themselves anonymous objects.
  No creation is stored in a local and read back. A creation inside a lambda or a query is not counted:
  it is part of that lambda's `DelegateCreation` or the query's `TranslatedQuery` opaque, and has no
  opaque of its own in the method's body.
- Decision: the identity scheme -> `{X,Y}::.ctor(<property types>)`, the anonymous type's constructor as
  any member identity is spelled, with the type, which has no name, spelled by its property names in
  declaration order. Alternatives: the names alone (`new { X = 1 }` and `new { X = "a" }` would be one
  function with two signatures); the compiler's `<>f__AnonymousType0` name (it numbers the types of an
  assembly in order of appearance, so the two sides would disagree). Rule: 1 (it is the `Type::Member(types)`
  shape the encoder and the call-site table already consume).
- Decision (`equiv-adr` bar test): closing a call whose arguments are not inert -> a dated clarification
  on ADR 0041, not a new ADR and not a deviation. The ADR's reason, that a callee reaches a heap map only
  through a reference it follows or user code it runs, is applied to a callee the ADR did not spell out:
  a constructor the compiler writes, which only stores its arguments. The inert-type rule is unchanged
  for every other callee. VERIFICATION-MODEL sections 1 and 3 carry the rule.
- Decision: what "passed to a call as an argument" is -> the creation's parent operation is an argument,
  through conversions (the one to `object` is on 38 of the 44 measured sites). An object stored in a local
  and passed later stays opaque. Alternatives: follow a local to its uses (a def-use walk for a shape
  measured 0 times). Rule: 4.
- Decision: the call keeps a `threw` edge, as every closed call has (`String.Concat` included), although
  this constructor cannot throw. Alternatives: an `IrCall` with no `threw` output (a second shape of
  closed call for the encoder and the validator). Rule: 4. Both sides get the same flag for equal
  values, so it does not separate them; P2-081 owns a Divergent that rests on a `threw` the real member
  cannot give.
- **Criterion 2.** `AnonymousObjectLoweringTests.AnAnonymousObjectPassedToACallIsAClosedCall` is the
  snapshot. The other tests of that class pin the callee's spelling, the order of evaluation, that an
  inferred name and a written one are one object, and each shape that stays opaque (returned, a local,
  a receiver, an array element, a branch of `?:`, nested in another object).
  `AnonymousObjectEquivalenceTests` (Integration) is `equiv-extend-ir`'s soundness pair: the same object
  from equal values is Equivalent, and other values or other names are not.
- **The censuses after the change (2026-10-02).** Same checkouts, this branch. `eshop-manual`: exit 0,
  8 s. `duplicati-3124`: exit 0, 54 s. Matched, congruent and changed pairs are the same before and after.

  | | `eshop-manual` | `duplicati-3124` |
  |---|---|---|
  | Changed pairs | 28 | 805 |
  | Changed pairs holding the reason, before to after | 2 to 0 | 24 to 1 |
  | `changedReasonSets["AnonymousObjectCreation"]` before to after | 1 to 0 | 6 to 0 |
  | Bodies holding the reason (legacy / modern), before to after | 2 / 2 to 0 / 0 | 55 / 55 to 2 / 2 |
  | `changedPairsWithoutOpaque` before to after | 11 to 12 | 412 to 418 |
  | Lowerable share (ADR 0034) before to after | 39.3% to 42.9% | 51.2% to 51.9% |

  The pair left on `duplicati-3124` is the returned object. Returning one, or reading one back, still
  needs a record-like sort for reference and mixed elements, which `IrTuple` (P2-027) is not; no ticket
  owns that, and at 1 of 805 changed pairs (0.1%) ADR 0028 does not ask for one.
- **What this does not change.** A pair is lowerable, not decided: the creation is a call in the trace
  whose result the solver chooses per position, as any closed call's is. No full run was made.
- The anonymous type's sort is still spelled `""` (P2-024's note), so every anonymous type shares one
  sort and one `cast..System.Object` map. That is coarser than C#, never finer: two objects of different
  anonymous types may be equal in a model, which can cost a proof but not make one. A getter `::get_X()`
  is shared the same way; reads are out of this ticket's scope.
- The IL lowering sees the creation as an ordinary `newobj` and asks the inert-type rule, so it does not
  close it. The two lowerings already differ on every construct only one of them lowers.
- `main` did not compile when this was written (`UnboundCodeTests` calls `IrLowerer.Lower` without the
  side runtime P2-055 added; PR #340 fixes it). The targeted test runs here had that fix applied locally
  and not committed.
