# P2-107 On a same-runtime pair, a whole-body opaque pair with identical source is not "changed"
Status: done (PR #425)
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-055, P2-058

## Goal
ADR 0034 counts a pair as changed when it is not congruent, and a whole-body opaque pair is never
congruent. On a migration that is a fair default. On a cleanup pair it hides the result:
`powershell-19687` reports 140 changed pairs, and 128 of them (82 `unbound`, 46 `no-body`) have
the same source text on both sides and no edit from the pull request. The lowerable share reads
4.3% where the 12 edited pairs give 50.0%, and the review list leads with 128 results nobody
needs to review. Decide, through `equiv-adr`, whether such a pair counts as unchanged when both
sides run on the same runtime and the body's syntax and every symbol it binds are identical, and
what verdict it gets.

## Spec references
ADR 0034 (changed pairs), ADR 0040 decision 2 (a same-runtime pair crosses no rule), ADR 0018
(congruence); P2-072 (a pair an audit took for identical source and was not: two files of one name,
only one of them in the solution, which limits how "identical" may be established); `docs/runs/2026-10-02-cleanup-verdict.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. The `equiv-adr` outcome is merged before any code change.
2. If the decision is to count them as unchanged: Notes record `changedPairs` on a rerun of
   `powershell-19687` next to the 140 it has now, and no pair the pull request edited leaves it.
3. A pair that crosses a runtime keeps the counting it has now.
4. The soundness argument is written down: what makes two whole-body opaque bodies the same
   function, and why P2-072's case is not a counterexample.

## Tests
- the unit tests the decision names
- `CensusTests.AnIdenticalOpaqueBodyOnOneRuntimeIsNotAChangedPair`, if the decision is to change

## Out of scope
Line-scoped opaques. Cross-runtime pairs.

## Notes
- `equiv-adr` bar test: first row. ADR 0024 already decides that identical bound code is
  Equivalent by congruence, and ADR 0034 that a changed pair is one that is not congruent. The
  case neither spelled out is a method whose code is not at the declaration `equiv` reads. So the
  outcome is a dated clarification on each (2026-10-07), not a new ADR: no verdict's meaning, rule
  id, SARIF shape or component boundary changes, and neither Decision is reversed.
- The decision. The definition of a changed pair does not change. On a same-runtime pair a partial
  method whose defining declaration has no body is fingerprinted by its implementing part (its
  bound body, plus the bound attributes of the method and of the local functions that body
  declares), so an unedited one is congruent: Equivalent with `proofMethod: congruence`, and not a
  changed pair. `unbound` pairs keep the counting they have, and so does every other `no-body`
  method (a record's primary constructor) and every pair that crosses a runtime.
- Criterion 4, the soundness argument, is ADR 0024's clarification: "What makes the two bodies the
  same function" and "Why P2-072's case is not a counterexample". In short: the fingerprint is
  taken from each side's own compilation (the symbol the matched identity resolves to there, its
  implementing declaration, and the symbols that binds), never from a file, a path or a text
  comparison across the two checkouts, which is what P2-072's audit did.
- What the 128 pairs are. The Goal says a whole-body opaque pair is never congruent. That holds
  for two reasons only: the same run has 306 `iterator` bodies, whole-body opaque on both sides,
  and all of them are congruent, because the fingerprint is of the bound tree and not of the IR.
  The 46 `no-body` pairs are all `[LibraryImport]` partial methods (19 files under
  `engine/Interop/Windows`, `CorePsPlatform.cs`, `FileSystemProvider.cs`, `Clipboard.cs`,
  `GetComputerInfoCommand.cs`, `ClearRecycleBinCommand.cs`), read from the 2026-10-02 run's SARIF.
- Why `unbound` is left alone. A name that does not bind has no symbol, so "every symbol it binds
  is identical" says nothing about it, and what it would bind to depends on the rest of the
  solution. ADR 0029 decision 2 stays as written. P2-106 is the ticket that lowers that count.
- The unit tests the decision names, all in `BodyFingerprinterTests`:
  `APartialMethodIsFingerprintedByItsImplementingPartOnOneRuntime`,
  `APartialMethodHasNoFingerprintOnAPairThatCrossesARuntime` (criterion 3),
  `ADeclarationWhoseCodeIsNotABoundBodyHasNoFingerprintOnOneRuntime`,
  `ARecordsPrimaryConstructorHasNoFingerprintOnOneRuntime`,
  `AnExternLocalFunctionIsFingerprintedByItsAttributes` and
  `APartialMethodsAttributesAreInItsFingerprint`.
- Decision: where "same runtime" is read -> `SideRuntime.Interval.IsEmpty` inside `BodyFingerprinter`. Alternatives: a flag the frontend passes in, a check in `CompareCommand.IsCongruent`. Rule: 4.
- Decision: which partial members -> ordinary methods only. A partial constructor also runs its type's initializers and a partial property's accessors were not in the measurement, so both keep having no fingerprint. Alternatives: every member with an implementing part. Rule: 4.
- Decision: the bind test for the implementing part -> `IrLowerer.UnboundCauses`, made `internal`, so the fingerprint and the lowering can never disagree on what `unbound` is (and P2-106's change to it applies to both). Alternatives: a second diagnostics check in the fingerprinter. Rule: 1.
- Decision: how an attribute is written -> one line per attribute, `AttributeData.AttributeConstructor` and then `AttributeData` itself (its class and its bound constructor and named arguments, constants as values). Alternatives: a renderer of our own over `TypedConstant`. Rule: 4. The compiler gives a partial method the attributes of both parts, so they are written once.
- Decision: the ticket's `CensusTests.AnIdenticalOpaqueBodyOnOneRuntimeIsNotAChangedPair` -> `LoweringCensusTests` in `Equiv.Tests.Integration`, on `samples/same-runtime-cleanup`. No class is called `CensusTests`. Alternatives: `Equiv.Cli.Tests`' `LoweringCensusTests` on hand-built IR, which would pass before the change. Rule: 3.
- Decision: the sample's method -> `Native.Beep` in `same-runtime-cleanup`, a partial method written by hand in the shape the interop generator emits (an implementing part that calls a local `extern` function). Alternatives: a real `[LibraryImport]`. Rule: 3. `IlSamples` compiles every sample's sources without generators, where a `[LibraryImport]` method is CS8795. The real attribute was run once through the sample before the rewrite: the generator's implementing part was found and the pair was congruent.
- Criterion 3 is `BodyFingerprinterTests.APartialMethodHasNoFingerprintOnAPairThatCrossesARuntime` (net48 to net10.0, net8.0 to net9.0): no fingerprint, as before, so nothing after it can change.
- Limit, recorded in the ADR: a partial method's congruent result lists no assumed callees, because its lowered body is one opaque. Lowering the implementing part would list them, on every runtime pair, and would also remove the `no-body` opaque. It is not this ticket.
- Found, not fixed here (both are older than this ticket and need their own):
  - The fingerprint of an ordinary body does not hold the attributes of the local functions it declares. Two bodies that differ only in the `[DllImport]` of a local `extern` function fingerprint equal, on any runtime pair, and are congruent. This ticket writes those attributes for an implementing part only.
  - `ProcedureEnumerator` leaves out `extern` methods, and a partial method whose implementing part is `extern` counts as one. A changed `[DllImport]` on such a method produces no result at all.
- Criterion 2, the rerun of `powershell-19687` (2026-10-07, compare mode quick, one run per column, exit 1 both):

  | | 2026-10-02 run (`46e6636`, on record) | `origin/main` `c59fa0fc`, without P2-106 | this branch |
  |---|---|---|---|
  | `changedPairs` | 140 | 148 | 102 |
  | of which `unbound` | 82 | 82 | 82 |
  | of which `no-body` | 46 | 46 | 0 |
  | `changedPairsWithoutOpaque` | 6 | 10 | 10 |
  | `changedPairsWholeBodyOpaque` | 128 | 129 | 83 |
  | lowerable share | 4.3% | 6.8% | 9.8% |
  | `pairsCongruent` | 33748 | 33741 | 33787 |
  | `pairsWholeBodyOpaque` | 434 | 434 | 434 |
  | EQ001 / EQ002 / EQ003 | 33748 / 5 / 135 | 33744 / 6 / 139 | 33790 / 6 / 93 |

  The 46 pairs that left the changed set are exactly the 46 `no-body` results, each now EQ001 by
  congruence. No pair joined it, and no other result changed its rule id. All 12 pairs the pull
  request edited are still changed (the same 12 identities, each with the verdict it had in the
  middle column). The middle column is P2-106's "before" run of the same evening on `origin/main`,
  read from its SARIF, so the box ran the baseline once and not twice.
- `changedPairs` is 148 on today's `main`, not the 140 of 2026-10-02: 8 more pairs are not
  congruent than at `46e6636`. None of the 8 is `unbound` or `no-body`, none is this ticket's, and
  this ticket did not look into them; P2-110 reruns the cleanup pairs.
- With P2-106 the 82 `unbound` pairs are that ticket's to count. The 46 here do not depend on it.
- Decision: the owner of `no-body` in `WholeBodyReasonOwners` -> P2-118, the owner `docs/runs/2026-10-07-opaque-tail.md` gives that reason. Alternatives: a sample with no whole-body opaque, which could not show the case. Rule: 3. The table had no row because no sample had such a body; `WholeBodyReasonOwnerTests` failed on the first CI run and this row is the fix.
