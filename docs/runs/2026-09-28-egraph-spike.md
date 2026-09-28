# P1-011 equality-saturation spike: gitextensions-8522 (2026-09-28)

Question: how many changed pairs differ only where a fixed set of algebraic rewrites closes the
difference? Those are the pairs "congruence modulo verified rewrites" would prove on top of ADR
0024's congruence.

**Answer: none. 0 of 1,195 changed pairs (0.0%).** ADR 0028's bar is 5% (60 pairs). No ADR and
no implementation ticket are written. ROADMAP's post-MVP list records the measurement instead
(criterion 3).

## Setup
- Pair: human, gitextensions/gitextensions PR #8522, legacy 3f4ed21998af, modern 5190ba5c1a5f
  (`tools/corpus/pairs.csv`).
- equiv: 516413c (`main`), linux-x64. Loaded through the production frontend (M3-028's composite
  loader), default config.
- Tool: `tools/spikes/egraph/` (e-graph 304 lines, 795 lines in all). Wall-clock 245 s for
  loading, lowering and comparing.
- Command: see `tools/spikes/egraph/README.md`. The run was
  `dotnet <spike> <legacySolution> <modernSolution>` from `.corpus/pairs/gitextensions-8522/pair.json`.
  `--self-test` passes all 10 hand-written cases. Each rule closes its own case, and nothing
  reorders a call operand, a `string` operand or a `checked` sum.

## Method
Each matched pair's two ADR 0024 canonical serialisations (`BoundSerialiser`'s text, the input to
the fingerprint) are parsed back into operation trees. The comparison walks both trees while they
have the same line and the same number of children. At the smallest subtree pair that still
differs, it builds an e-graph of the two subtrees and saturates it under the rule set:
- commutativity and associativity of `+ * & | ^ && ||`;
- `x - y` ↔ `x + (-y)`;
- comparison flips (`a < b` ↔ `b > a`, `a == b` ↔ `b == a`);
- `!(a == b)` ↔ `a != b`;
- double negation;
- `if (c) A else B` ↔ `if (!c) B else A`, and the same for `?:`.

Saturation runs for at most 12 rounds and 20,000 e-nodes, on subtrees of at most 400 lines.

A pair counts as closed when every differing subtree is closed and neither side is
runtime-sensitive, since ADR 0024 would still refuse such a pair. Each rule fires only where it is
an identity of the IR. That means integral or `bool` operators with no user-defined operator
method, and no `checked` associativity. Operands are reordered only when both are pure, because
C# evaluates operands left to right. Guarding the rules can only lower the count, and the
unguarded rules close nothing more: no residual below is an operand reordering.

**Population.** Criterion 1 measures the pairs a full run leaves Unknown(opaque), which is a
subset of the changed pairs. This run measured every changed pair instead, split by whether either
body holds an `IrOpaque`, because a pair can be Unknown(opaque) only if it holds an opaque. Zero
closures over the superset means zero over the subset, so the count is exact. A full solver run
at 516413c was started for the SARIF join, but on this 4-core box it was 5% done after 22 minutes,
and nothing it could report would change the count. M4-007's run of the same pair reported 197
Unknown(opaque) results (`docs/runs/2026-09-27-m4-007-gitextensions-8522/SUMMARY.md`). The
spike's SARIF join (`dotnet <spike> <legacy> <modern> <equiv.sarif>`) was checked on
`samples/business-layer`: its changed-pair count matched the census's (6 = 6), and it found that
sample's one Unknown(opaque) pair.

## Counts

| | pairs |
|---|---|
| Matched pairs | 13,446 |
| Changed pairs (not congruent) | 1,195 |
| ... with identical serialisations, changed only because a side is runtime-sensitive | 735 (61.5%) |
| ... whose serialisations differ | 460 |
| ...... holding an opaque (the superset of Unknown(opaque)) | 319 |
| ...... with no opaque | 141 |
| **Closed by the rule set** | **0 (0.0% of changed pairs)** |
| Closed, but a side is runtime-sensitive | 0 |

The 735 runtime-sensitive pairs are outside anything a rewrite can reach. Their text is already
equal, and ADR 0024 excludes them on purpose: EQ006 and the solver decide them.

## Ten most frequent closing rules

None. No rule closed a whole pair, and no rule closed even one differing subtree inside a pair
that stayed open for another reason.

## Ten most frequent residual differences

Pairs holding each residual. A pair can hold several. There are 60 distinct residuals in all.

| Residual (node kind, and which fields differ) | Pairs holding an opaque (319) | All changed pairs whose text differs (460) |
|---|---|---|
| `Invocation[InvocationExpression]`: symbols differ (a different callee or overload) | 274 | 356 |
| `ObjectCreation[ObjectCreationExpression]` vs `Conversion[ObjectCreationExpression]` | 15 | 48 |
| `Literal[StringLiteralExpression]`: const differ | 11 | 12 |
| `Block`: legacy-only `ExpressionStatement` | 5 | 11 |
| `Block`: legacy-only `Conditional[IfStatement]` | 5 | 8 |
| `PropertyReference[SimpleMemberAccessExpression]`: symbols differ | 5 | 8 |
| `VariableDeclarator`: symbols differ | 7 | 7 |
| `Block`: legacy-only `VariableDeclarationGroup[LocalDeclarationStatement]` | 5 | 7 |
| `Block`: legacy-only `Return[ReturnStatement]` | 3 | 5 |
| `Block`: modern-only `Return[ReturnStatement]` | 2 | 5 |

In the full histogram, only 7 of the 60 residuals touch an operator expression at all, each in
one pair. All seven are semantic edits that no rewrite preserves:
- a call replaced by `==` (one residual) or by `!=` (one);
- an `is` pattern replaced by `&&`;
- a local or a parameter replaced by a `??` expression (two residuals);
- a sum replaced by a single parameter;
- a `?:` whose type changed.

## Reading
This migration changes callees, overloads, literals and statements. It never reorders an operand
or reshapes a condition. The difference the rewrite idea targets (`F(a + b)` against
`F(b + a)` inside an unlowerable construct) does not occur once in 1,195 changed pairs. Equality
saturation over this rule set therefore adds nothing on this pair, and the idea is recorded as
measured, not scheduled. A later corpus pair whose migration does rewrite expressions (an agent
that modernises code, which `tools/corpus/migration-prompt.md` currently forbids) is the only
thing that could change this. The tool can rerun on such a pair unchanged.
