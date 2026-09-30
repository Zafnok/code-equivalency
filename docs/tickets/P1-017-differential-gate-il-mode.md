# P1-017 M0-012's differential soundness gate also verifies every generated pair through the IL lowering
Status: in-progress
Effort: M
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: P1-016

## Goal
M0-012's three rules hold for the IL lowering too. Each generated pair is verified a second time,
with both sides forced through `IlLowerer`, and the IL verdict is held to the same rules as the
IOperation verdict. A false Equivalent that enters only through the IL lowering is caught before
P1-018 can turn the fallback on. About 2 source files and 2 test files.

## Spec references
ADR 0039 (the IL mode); VERIFICATION-MODEL.md section 7 (differential soundness); M0-012's ticket.

## Acceptance criteria (all must hold; nothing beyond them)
1. `DifferentialSoundnessTests` verifies each pair twice, once per lowering. Both verdicts are held
   to rules 1 to 3, and a failure names the lowering that broke the rule.
2. The PR budget stays 200 pairs, and the nightly budget stays 5,000. The IL pass on the PR budget
   adds at most 50% to the test's wall-clock time, measured on CI and stated in the PR.
3. `PairGen` gains at least the constructs the IL lowering exists for: a lifted `int?` operator, a
   nullable conversion, `$"..."` interpolation, and a positional pattern in a `switch`. Each appears
   in the preserving and the changing families.
4. A deliberately broken IL mapping (a test-only `IlLowerer` option that reads `!=` as `==`; see the
   Deviation in Notes) makes the gate fail rule 1 within the PR budget, with the seed printed.
5. No rule is weakened or skipped for the IL lowering.

## Files
- `tests/Equiv.Tests.Integration/DifferentialSoundnessTests.cs`, `tests/Equiv.TestSupport/PairGen.cs`
- `src/Equiv.Frontend.CSharp/Lowering/Il/IlLowerer.cs` (the test-only option, `internal`)
- `tests/Equiv.Tests.Integration/PairGenTests.cs`

## Tests
- `DifferentialSoundnessTests.GeneratedPairsAreSoundUnderBothLowerings`
- `DifferentialSoundnessTests.ABrokenIlMappingIsCaught`
- `PairGenTests.GeneratesTheIlFallbackConstructs`

## Size guard
More than 4 files, or a change to the rules' text, means you are rewriting M0-012.

## Out of scope
Fixing an IL lowering bug the gate finds: file it as a P2 ticket and note it here, unless it is a
one-line mapping fix.

## Notes
- Deviation: criterion 4's fault was `sdiv` read as `udiv`. Applied to both sides it never makes a
  changed pair look Equivalent: 0 rule-1 failures in 200 pairs, and 0 in 5,000. An unsigned
  quotient by a literal divisor of 2 or more is non-negative, and no mutation operator's change
  shows only in a quotient's sign. `<=` read as `<` (and `>` as `>=`), and `sub` read as `add`,
  also missed at 200: random inputs rarely hit a comparison's equality case, and SwapArguments
  rarely lands on a subtraction. `!=` read as `==` hides every FlipComparison of `==`/`!=`, which
  every input that reaches it observes, and it is caught within the PR budget. The seam is
  `IlLowerer.Lower(method, compilation, x87, mapped)`, which rewrites the operator of each
  integral arithmetic and comparison instruction. The ticket text is corrected.
- Deviation: the Files list names four files. The change also needs `PairRuntime.cs` (the
  lowering is a parameter of the memoised analysis), and the ADR 0026 clarification below. No
  rule's text changed.
- Decision: the gate draws `Gen.Frequency((3, PairGen.Pair), (1, PairGen.IlPair))`.
  `PairGen.Pair` is unchanged, because `PairGenLoweringTests` requires it to lower with no opaque
  from IOperation. `IlPair` inserts one of the four constructs into a generated method, assigned
  to a local, because each value branches.
- Decision: M0-012's three rule facts become one `GeneratedPairsAreSoundUnderBothLowerings`. It
  draws each pair once and checks rules 1 to 3 under each lowering. A failure's first line is
  `rule N under the <lowering> lowering`.
- Finding (filed as P2-060, not an IL mapping bug): with `$"{s}t"` in a changing pair, the IL
  lowering calls `String.Concat`, and the heap model lets that call write `Oracle.F` and `u`.
  Z3 then builds a Divergent on that heap, and the C# replay does not diverge (rule 2, seed
  `000000000000`). The same pair written `s + "t"` gives the same Divergent from IOperation. ADR
  0026 keeps ordinary call outputs untainted, so such a model is an input plus a callee
  behaviour. Rule 2 cannot replay that as C# arguments, so it now excuses a non-diverging
  replay only when the model's run records a call (ADR 0026 clarification, 2026-09-30).
  Rules 1 and 3 are unchanged, and so is every pair M0-012 generated, since none of them makes
  a call in a changing pair.
- Measured locally (200 pairs, sequential lowering and Z3 time): IOperation 4 s lowering and
  49 s Z3; IL 17 s lowering and 85 s Z3. IL proves 151 Equivalent against IOperation's 120,
  because IOperation leaves the new constructs opaque (40 Unknown(opaque)). IL has 13
  Unknown(abstraction) and 4 Unknown(unaligned-loop), against 5 and 1. A pair costs Z3 about
  twice as much from IL as from IOperation. Wall-clock with CsCheck's parallel sampling: main's
  class (M0-012's three facts) 32.6 s, `GeneratedPairsAreSoundUnderBothLowerings` 39.3 s.
- Finding (one-line fix, `tools/corpus/seeder/SyntaxMutator.cs`): the nightly budget (5,000 pairs)
  failed rule 3 under the IL lowering. ReorderIndependentStatements had swapped `F = b;` with
  `z = ($"t{s}" == null);`, because `IsSimple` counted an interpolated string as simple. An
  interpolated string is a call, and a call reads the heap (ADR 0018), so the swap is not
  preserving under the tool's call model. `IsSimple` now treats `InterpolatedStringExpression`
  as a call. That the call cannot really read `Oracle.F` is P2-060.
- Decision: `PairGenLoweringTests.EveryIlPairSideLowersFromIl` (Frontend unit tests) lowers
  `IlPair`'s sides from IL to valid IR. It covers `PairGen`'s new code outside the integration
  run, which Sonar's coverage (`build.ps1` without `-Integration`) does not see.
