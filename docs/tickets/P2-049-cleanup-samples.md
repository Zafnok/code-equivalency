# P2-049 Samples for cleanup refactorings: modern syntax and extract method
Status: todo
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
