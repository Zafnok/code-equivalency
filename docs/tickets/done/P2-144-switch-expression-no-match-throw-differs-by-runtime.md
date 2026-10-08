# P2-144 A switch expression that matches no arm throws another exception type after a migration from .NET Framework, and such pairs are Equivalent by congruence
Status: done (PR #436)
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
A switch expression with no arm for the value throws. The compiler throws
`System.Runtime.CompilerServices.SwitchExpressionException` where the reference assemblies have the
type (.NET Core 3.0 and later) and `System.InvalidOperationException` where they do not (.NET
Framework). The source is the same on both sides, the two sides reach the throw on the same inputs,
and the exception type, which is an observable (VERIFICATION-MODEL section 1), differs.
`SwitchExpressionException` derives from `InvalidOperationException`, so a handler for the base
type sees no difference and a handler or a test for the exact type does.

P2-137 counted the callee pair `System.InvalidOperationException::.ctor()` to
`System.Runtime.CompilerServices.SwitchExpressionException::.ctor()` on `gitextensions-8522` (main
of 2026-10-07): 41 results name it in `properties.reboundCalls`. 39 of them are Equivalent with
`proofMethod` `congruence`, because the fingerprint of the bound tree does not hold the throw and
the body is not runtime-sensitive, and 2 are changed pairs, both with `switch-pattern` in the
reason set as well. So on an input that matches no arm, 39 results say Equivalent where the thrown
type differs. Whether any of the 39 can be reached with such an input was not checked: a switch
over every member of an enum still has the throw for a value outside the enum.

## Spec references
VERIFICATION-MODEL sections 1 and 3; ADR 0024 (congruence and runtime-sensitive bodies); ADR 0040
(a runtime rule applies only if the pair crosses it); ADR 0008 and
`src/Equiv.Core/RuntimeChanges/runtime-changes.json`; ADR 0042;
`docs/tickets/done/P2-137-rebound-call-forms-counted-and-catalogued.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Before any code, over `gitextensions-8522` (`--lower-only`): the matched pairs whose bodies hold
   a switch expression with the compiler's throw, split by whether an arm always matches (a
   discard or `var` arm, or the patterns cover the type's every value), and by congruent or
   changed. Counts in `## Notes`.
2. Decide, through `equiv-adr`'s bar test, what the throw is on a pair that crosses from .NET
   Framework to .NET: a runtime rule (EQ006, as a `runtime-changes.json` row or its equal for a
   compiler-made call), so that such a body is runtime-sensitive and not congruent; or one
   exception type on both sides, recorded on the result as an assumption. Record the decision where
   the bar test says.
3. A sample pair, the same switch expression with no discard arm on .NET Framework 4.8 and on .NET
   10: its verdict is the one criterion 2 decides, and it is not Equivalent by congruence unless
   the decision says the two types are one.
4. A switch expression whose arms always match is unaffected: a sample method of it stays
   Equivalent by congruence.
5. On a re-run of `gitextensions-8522`, `## Notes` records the results by rule for the pairs of
   criterion 1, next to 39 Equivalent and 2 Unknown.

## Files
`src/Equiv.Frontend.CSharp/` (the fingerprint and the lowering of the switch expression's throw),
`src/Equiv.Core/RuntimeChanges/` if criterion 2 chooses a row, their tests, `samples/`, the ADR
criterion 2 names.

## Tests
A unit test of the fingerprint and of the lowering, and the sample of criteria 3 and 4.

## Size guard
A switch statement has no such throw and is out. Any change to when a call counts as rebound (ADR
0042's decision) is a different ticket.

## Out of scope
The `switch-pattern` opaque reason (P2-122).

## Notes
- Found by P2-137, criterion 1, whose out-of-scope list says to file it when the two sides reach
  the throw on the same inputs.

### Criterion 1: the pairs that hold a switch expression (2026-10-08)
- Run: `equiv compare --lower-only` of `gitextensions-8522`, default config, on `main` at `40057429`,
  before this ticket's change. A census has no results, so the run also wrote, through a local
  patch that is not in this PR, one line per matched pair whose declaration holds a switch
  expression: identity, congruent or not, reason set, rebound pairs, and each expression's class.
  The class is Roslyn's own answer (`ISwitchExpressionOperation.IsExhaustive`: the compiler emits
  the throw only when it is false), next to whether an arm is a discard or `var` with no `when`
  clause, and whether the compiler reports the expression as not exhaustive once every warning
  suppression is lifted.
- 58 matched pairs hold a switch expression: 53 congruent and 5 changed. In every one of them
  every expression covers every value. No pair holds an expression that can match no arm.

| An arm always matches | Congruent | Changed | Pairs |
|---|---|---|---|
| A discard or `var` arm, in every expression of the pair | 51 | 4 | 55 |
| An expression with no such arm whose patterns cover the type's every value (all three switch on a tuple) | 2 | 1 | 3 |
| Some expression can match no arm | 0 | 0 | 0 |

- 41 of the 58 name the callee pair `System.InvalidOperationException::.ctor()` to
  `System.Runtime.CompilerServices.SwitchExpressionException::.ctor()` in their rebound calls: 39
  congruent and 2 changed, the counts P2-137 gave. The other 17 record no such call site; 16 of
  them hold a local function, which is not lowered in place.
- So the Goal's worry does not hold on this pair. The 39 results that are Equivalent by congruence
  throw nothing on any input: the compiler proves each expression exhaustive and emits no throw.
  What named the two constructors was Roslyn's control-flow graph, which ends every switch
  expression with the throw, exhaustive or not, and the lowering followed it. The switch over every
  member of an enum that the Goal mentions would keep its throw, and none of the 58 is one: every
  expression here that switches on an enum has a discard or `var` arm.

### Criterion 2: a runtime rule
- Decision: the throw of a switch expression that matches no arm, across .NET Core 3.0 -> a
  runtime rule (EQ006), as a `runtime-changes.json` row for `SwitchExpressionException`'s
  constructors with `changedIn: netcoreapp3.0`, and the bound fingerprint names the constructor
  the compiler calls. Alternatives: one exception type on both sides, recorded on the result as an
  assumption. Rule: `equiv-adr`'s bar test, first row. ADR 0024 already says identical text that
  binds differently must change the fingerprint, ADR 0040 that a runtime rule has a change point,
  and ADR 0042 that a runtime-changed callee is in no rebound pair; this applies them to a call the
  compiler makes. Recorded as a dated clarification on ADR 0024, with VERIFICATION-MODEL section 3
  and the table's header. The other choice would need a new ADR: it narrows what section 1
  observes, and it is the assumption no run can discharge that ADR 0042 rejected.
- Decision: which expressions have the throw -> those whose
  `ISwitchExpressionOperation.IsExhaustive` is false. Alternatives: every expression with no
  discard or `var` arm; the compiler's not-exhaustive warnings. Rule: 1. It is the compiler's own
  decision whether to emit the throw. An arm test alone would cost the 3 pairs of the table's
  second row their congruence, and a warning can be suppressed and does not report an unmatched
  `null` where nullable annotations are off.
- Decision: the constructor is written in the serialisation's `context=`, where the binding of an
  interpolated string is -> the two sides' texts differ when their compilers call different
  constructors, and two sides that both lack the type (`netstandard2.0` on two hosts) stay
  congruent. Alternatives: only a runtime-sensitive flag taken from the pair's interval. Rule: 3.
- Decision: the row's `source` -> `curated`; `docs/runtime-changes-review.md` lists it with the
  hand-picked rows. Alternatives: `documented`, which means a row from a compatibility page.
  Rule: 1.
- No `Release:` footer: no rule id, property, exit code or file format changes, and a new row is
  what M3-033 and P2-113 added without one. A verdict changes only for a pair that crosses .NET
  Core 3.0 and holds an expression that can match no arm: where an input reaches the throw,
  Equivalent by congruence or Unknown by a rebound call becomes EQ006.

### Criteria 3 and 4: the sample and the tests
- `samples/switch-expression-no-match`, .NET Framework 4.8 against .NET 10, the same text on both
  sides: `Weight` (arms for 1 and 2 only) is Divergent with EQ006, citing the row, and has no
  rebound call; `WeightOrZero` (a discard arm) and `Sign` (`true` and `false`) are Equivalent by
  congruence. `SamplesEndToEndTests` pins each.
- Fingerprint: `BodyFingerprinterTests` (the constructor named, runtime-sensitive only across .NET
  Core 3.0, the suppressed row, a side without the type, and which expressions have no throw).
  Lowering: `SwitchExpressionLoweringTests`.
- The lowering changed for an exhaustive expression too. The criteria do not name that, but
  criterion 4 needs it: Roslyn's graph gives such an expression a no-match block behind the
  failing edge of its last arm's test. A `var` arm's test is an opaque, so that edge was live, and
  with the row the block behind it would have been a runtime-changed call on a path no input
  takes. The last test is now a jump to its arm and the block is not lowered. Two snapshots changed
  for that (`IrLowererSnapshotTests.SwitchExpression` and `SwitchOnTypePatterns`: the branch on a
  constant `true` and the throw behind it are gone), and `IlLowererTests` lost a known difference,
  since the IL never had the block.
- The IL lowering is unchanged. The compiler's IL for the throw calls a helper it generates, which
  the IL lowering leaves opaque, so `Weight` joined `IlLoweringParityTests`' known list on both
  sides. The row's prefix covers every constructor of the type, the one that takes the unmatched
  value included.
- The sample snapshots of `same-runtime-cleanup` and `cleanup-modern-syntax` lose the
  `SwitchExpressionException` constructor from `externalCallees`: each has a switch expression
  with a discard arm, and no lowered body calls the constructor now.

### Criterion 5: the re-run
- Run: the same census with this change, same checkout and config.
- Deviation: criterion 5 asks for the results by rule, and a `--lower-only` run has no verdicts.
  Both runs were censuses because the box was shared with a timed run. What a census decides is
  below: a congruent pair is Equivalent by congruence (EQ001) in any verifying run, and the rule of
  the 5 changed pairs is the solver's and was not measured.

| | Before | After |
|---|---|---|
| Matched pairs that hold a switch expression | 58 | 58 |
| Congruent (Equivalent by congruence in a verifying run) | 53 | 53 |
| of them naming the two constructors as a rebound call | 39 | 0 |
| Changed | 5 | 5 |
| of them naming the two constructors as a rebound call | 2 | 0 |
| Pairs with a runtime-changed call to a `SwitchExpressionException` constructor | 0 | 0 |

- Every expression is exhaustive, so no pair meets the new row and none loses its congruence.
  Next to P2-137's 39 Equivalent and 2 Unknown: the 39 are still congruent and no longer carry the
  callee pair, and the 2 changed pairs' reason sets lose `rebound-call`
  (`rebound-call+switch-pattern` is now `switch-pattern`, and `Tuple+rebound-call+switch-pattern`
  is `Tuple+switch-pattern`). `switch-pattern` (P2-122) is what still keeps those 2 from a proof.
- The whole census: 13,541 matched pairs, 12,682 congruent and 859 changed, before and after.
  Pairs without opaque are 10,167 before and 10,171 after (4 congruent pairs whose only opaque was
  the rebound constructor). Bodies holding `rebound-call` are 172 per side before and 131 after.
  Runtime-change call sites are 906 per side before and after.

### Other
- `.corpus/` was prepared, fetched and restored in this worktree and is not committed. The two
  runs took 819 and 494 seconds on a box that was also running a timed run and three other
  sessions.
