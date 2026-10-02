# P2-049 Samples for cleanup refactorings: modern syntax and extract method
Status: done (PR #343)
Effort: M
Model: Sonnet, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M3-003, P2-055 (same-runtime pairs, ADR 0040)

## Goal
The samples cover migration shapes (`api-drift`, `removed-null-check`) and three refactorings
(`renamed-locals`, `loop-to-linq`, `loop-fusion`). They do not cover what a "tidy up messy code"
commit usually does: rewrite old syntax into modern C#, and split or merge helper methods. Add two
samples that do. Their `expected.sarif.json` records what `equiv` says today, whatever that is. Every
procedure that is not Equivalent becomes a ticket, so the backlog shows exactly which cleanup
shapes are unsupported.

## Spec references
`samples/README.md`; VERIFICATION-MODEL.md sections 5 and 6; `docs/tickets/IOPERATION-COVERAGE.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. `samples/cleanup-modern-syntax/`: both sides target net10.0 (a same-runtime pair, ADR 0040). One
   method per rewrite, the legacy side in C# 7.3 style and the modern side in C# 14 style, with
   behaviour identical on every input:
   - an `if`/`else if` chain on an `int` becomes a `switch` expression;
   - `string.Format("{0}-{1}", a, b)` on `string` arguments becomes `$"{a}-{b}"`;
   - `x == null ? (int?)null : x.Length` becomes `x?.Length`;
   - `if (x == null) x = d;` becomes `x ??= d;`;
   - `if (o is Foo) { var f = (Foo)o; ... }` becomes `if (o is Foo f) { ... }`;
   - a block-bodied getter becomes expression-bodied;
   - a `foreach` with `if` and `Add` becomes `.Where(...).ToList()`.
2. `samples/cleanup-extract-method/`, also net10.0 on both sides: a legacy method, and a modern side where part of its body moved
   into a new private helper. Also the reverse, a legacy helper inlined into its single caller.
   Both callers are matched pairs, and the helper is Added or Removed.
3. Each sample's README has an "Expected verdicts" table with the verdict `equiv` gives today, and
   for every non-Equivalent row the reason and the owning ticket.
4. Every non-Equivalent procedure gets a new `P2-nnn-*.md` ticket, one per distinct reason, unless an
   open ticket already owns that reason. That includes an Unknown(opaque) whose reason has no owner.
   A Divergent verdict on these behaviour-preserving pairs is a precision bug and says so in its
   title.
5. Both samples run in `SamplesEndToEndTests` with checked-in snapshots.

## Files
`samples/cleanup-modern-syntax/**`, `samples/cleanup-extract-method/**`,
`tests/Equiv.Tests.Integration/SamplesEndToEndTests.cs` (if samples are listed there), new
`docs/tickets/P2-nnn-*.md`, `docs/ROADMAP.md`.

## Tests
`SamplesEndToEndTests` theory rows for both samples.

## Size guard
Any change under `src/` means you are fixing, not measuring: stop. More than 12 procedures in one
sample: split or cut.

## Out of scope
Making any of these verdicts Equivalent. Refactorings the seeder can apply (P2-048).

## Notes
- Result: 9 matched pairs, all behaviour-preserving. 3 Equivalent (`Circle.Diameter`,
  `Tidy.OrDefault`, `Tidy.Measure`, each `bounded`), 3 Unknown and 3 Divergent. Five distinct
  reasons, none with an open owner, so five tickets:
  - `Tidy.Grade`: Unknown(opaque), `switch-pattern` on each relational pattern. P2-093.
  - `Tidy.Join`: Divergent, `String::Format(string,object,object)` against the
    `String::Concat(string,string)` chain P2-086 lowers the interpolated string to. P2-094.
  - `Tidy.LengthOf`: Unknown(opaque), `Conversion` on both sides (`int` to `int?`) and `DefaultValue`
    on the modern side (`default(int?)`). P2-095.
  - `Tidy.Positives`: Unknown(abstraction), the divergence depends on the lambda's delegate. P2-096.
  - `Invoice.Total` and `Invoice.Shipping`: Divergent on the call trace alone, with equal return
    values in the counterexample. One reason, a call to a helper that exists on one side only. P2-097.
- Owners checked and ruled out: P2-068 (a forwarder to a BCL member, not a helper with a body),
  P2-070 (identical source rebinding to another overload), P2-071 (an extra effect-free call),
  P2-081 (a closed call's free `threw`; it would turn `Join` Unknown at best), P2-087 (lifted binary
  operators; `LengthOf` has none).
- Decision: the legacy projects set `LangVersion` 7.3, so the compiler enforces "C# 7.3 style", and
  use a block-scoped namespace. Reversible; the alternative (style by convention only) lets a later
  edit slip modern syntax into the legacy side unnoticed.
- Decision: `Grade` uses relational patterns (`>= 90`), not constants. `same-runtime-cleanup`'s
  `Rank` already covers an `if` chain against a constant-pattern `switch` expression, and it is
  Equivalent, so constants would measure nothing new.
- Decision: both sides of `Positives` keep the same `if (values == null)` guard. Without it the pair
  is not behaviour-preserving (`NullReferenceException` from the loop, `ArgumentNullException` from
  `Where`), which criterion 1 requires on every input.
- Decision: `Circle.Radius` is a public field, not an auto-property, so the sample has no accessor
  pair beyond the `Diameter` getter the criterion asks for.
- Deviation: `samples/README.md` is not in Files. Its sentence "Two samples pair other runtimes" became
  false, so it now lists four.
- `SamplesEndToEndTests.cs` is unchanged: its theories enumerate `samples/` by directory, so both
  samples get the snapshot, baseline round-trip and refinement rows without an edit.
- No change under `src/`. 7 procedures in one sample and 4 in the other.
- Deviation: `IlLowererTests.cs` and `IlLoweringParityTests.cs` are not in Files. Both compare the
  IOperation lowering with the IL lowering of every opaque-free sample method, and each keeps a list
  of the known differences that nothing else may join. Three of the new methods differ, so they are
  listed, with the cause:
  - modern `Tidy.Join`: the IOperation lowering joins an interpolated string with a chain of
    `String::Concat(string,string)` (P2-086), and the compiler emits one
    `String::Concat(string,string,string)`. New in both lists. A `+` chain of three strings has the
    same gap, and no sample had one. It only matters under `--il-fallback`, which is off by default
    (ADR 0039, P1-018), so no ticket is filed.
  - modern `Tidy.Positives`: the lambda, the cause `business-layer` already has.
  - legacy `Tidy.Positives`: `values == null` read through `null.System.Object`, the cause
    `business-layer`'s `Reserve` already has.
- The first CI run failed on `main` not compiling at 46e6636 (`UnboundCodeTests.cs`, fixed by PR
  #340); `main` was merged in. The second failed on the first of the two parity tests above.
