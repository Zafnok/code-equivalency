# P2-164 Precision bug: `StringBuilder.Append($"...")` is Divergent between net5.0 and net6.0
Status: done (PR #508)
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-086, P2-139

## Goal
An interpolated string passed straight to `StringBuilder.Append` or `AppendLine` binds two ways.
Before .NET 6 the string is built first (`string.Concat` or `string.Format`) and
`StringBuilder::Append(string)` takes it. From .NET 6 the call binds to
`StringBuilder::Append(ref StringBuilder.AppendInterpolatedStringHandler)`, and the compiler emits
the handler's constructor and one `AppendLiteral` or `AppendFormatted` call per part, each writing
into the builder. The text appended is the same. Minimal repro, as a sample pair (identical file;
legacy `net5.0`, modern `net6.0`):

```csharp
static void Header(System.Text.StringBuilder sb, string id) { sb.Append($"index {id}\n"); }
```

Today this is EQ002: the two call traces differ (ADR 0018), each handler call's `threw` flag is
free, and the counterexample has the modern side throw inside the handler. It is a false positive.
P2-086 and P2-139 lower an interpolated string bound to `string.Format` or to
`DefaultInterpolatedStringHandler` to one `Concat` chain; they count "a handler other than the
default one" as a form and found none on the pairs they measured, because the argument of `Append`
was not looked at as one. P2-124's rerun of `gitextensions-9860` has 5 such EQ002, in unedited
files, which is half of that pair's EQ002 (`docs/runs/2026-10-12-upgrade-verdict.md`).

Lower `Append` and `AppendLine` with an `AppendInterpolatedStringHandler` argument to what the
pre-.NET 6 binding lowers to: the P2-086 `Concat` chain of the parts, then
`StringBuilder::Append(string)` (or `AppendLine(string)`), for the hole shapes P2-086 and P2-139
accept. Every other shape stays as it is. The evaluation order differs between the two bindings
when a hole runs code that reads the builder (the handler has already appended the parts before
it); decide through `equiv-decide` which holes are admitted, and log the `Decision:` line.

## Spec references
`docs/tickets/IOPERATION-COVERAGE.md` rows `InterpolatedString`, `InterpolatedStringHandlerCreation`
and `Invocation`; `docs/tickets/done/P2-086-interpolated-string-owner.md` (the representation and
its evaluation-order argument); `docs/tickets/done/P2-139-interpolated-string-left-opaque-after-p2-086.md`;
ADR 0018 (call traces); `docs/VERIFICATION-MODEL.md` section 3.

## Acceptance criteria (all must hold; nothing beyond them)
1. `samples/append-interpolated-string` (the pair above, plus one method with an `int` hole and one
   that calls `AppendLine`) has no EQ002, and each method is Equivalent.
2. A method whose hole reads the same builder (`sb.Append($"{sb.Length}")`) is not Equivalent by
   this rule: it keeps the result it has today.
3. The `IFormatProvider` overloads (`Append(IFormatProvider, ref AppendInterpolatedStringHandler)`)
   are not lowered by this rule.
4. The lowering oracle (`LoweredIrAgreesWithCompiledCSharp`) covers the admitted shapes under both
   bindings.
5. `docs/tickets/IOPERATION-COVERAGE.md` says what is lowered.

## Tests
Integration test on the sample. Unit tests for criteria 2 and 3. The oracle case of criterion 4.

## Out of scope
Custom handlers other than `StringBuilder.AppendInterpolatedStringHandler`. Format and alignment
clauses P2-086 does not accept. `string.Format` written by hand (P2-094).

## Notes
- Found by P2-124: 5 EQ002 on `gitextensions-9860`, all in files the pull request does not edit:
  `GitCommands.Patches.PatchManager::GetSelectedLinesAsNewPatch(GitCommands.GitModule,string,string,int,int,System.Text.Encoding,bool,byte[],string)`,
  `GitCommands.Patches.PatchManager::ToPatch(System.Collections.Generic.IEnumerable<global::GitCommands.Patches.Chunk>,GitCommands.Patches.SubChunkToPatchFnc)`,
  `GitCommands.Patches.Chunk::ToPatch(GitCommands.Patches.SubChunkToPatchFnc,System.Text.StringBuilder)`,
  `BugReporter.ErrorReportUrlBuilder::Build(BugReporter.Serialization.SerializableException,string,string,string)` and
  `BugReporter.Serialization.SerializableException::ToString()`.
- Also from P2-124, thorough mode only: the row `System.Text.StringBuilder::AppendLine(ref ` (changedIn `net6.0`,
  "holes that read or change the builder see a different state") gives 1 EQ006 from the IL pass, in
  `GitCommandsTests.Git.GitBlameCommitTests::TestToString()`, where no hole touches the builder. Criterion 2 is the
  case the row is about; say in `## Notes` whether the row still fires on a body this ticket lowers.
- **What the graph holds (2026-10-15).** Under C# 10 Roslyn's control flow graph makes the receiver a flow capture,
  then the handler a flow capture of `new AppendInterpolatedStringHandler(literalLength, holeCount, builder)`, then one
  bare `AppendLiteral` or `AppendFormatted` call on that capture per part, then the call `Append(ref capture)`. Today the
  last call lowers as an opaque with reason `ref-argument` (the capture is no local), the parts as calls, and that is
  the EQ002. A hole that branches (`a ?? b`) is a capture of its own made in blocks between the handler and its part.
- **The hole shapes on the five methods**, read from the modern side's source (`37797ea4dd74`): `Chunk.ToPatch` holds
  a field of `this` and two locals; `ErrorReportUrlBuilder.Build` a call of `Uri.EscapeDataString` after a text part;
  `SerializableException.ToString` auto-property getters (`{Type}: {Message}`, and `{inner.Type}` in a loop);
  `GetSelectedLinesAsNewPatch` a `??` after a text part; `PatchManager.ToPatch` two static getters after a text
  part. So only one of the five has holes that run no code: in the other four a hole runs code after the handler has
  written the parts before it into the builder.
- Decision: which holes are admitted -> an append is taken when its string is one P2-086 and P2-139 cover with no
  clause, and either (a) every hole is a `string` or an integer that only reads a local, a parameter, a constant or a
  field of `this` (P2-086's read), whatever the builder is: nothing runs and nothing throws between the first write
  and the last; or (b) the builder is a local declared with `new StringBuilder(...)` or `new()`, every use of which is
  the receiver of one of its members (a result that is the builder again dropped or itself such a receiver), none in
  a lambda or local function, that no hole names, and the call is in no `try` statement: no code a hole runs can reach
  the builder, and when a hole throws out of the body nothing is left that can read the parts already written.
  Alternatives: (a) alone (sound, takes 1 of the 5); every hole that does not name the builder (a hole reaches a
  builder held in a field or handed to a callee, and a throwing hole leaves a caller's builder half written); every
  P2-086 shape (the same, and it would make criterion 2's method Equivalent). Rule: 3 (each condition is one a unit
  test pins, and criterion 2's method fails both). By the source, (a) takes `Chunk.ToPatch` and (b) takes `Build`,
  `SerializableException.ToString` and `GetSelectedLinesAsNewPatch`; `PatchManager.ToPatch` stays, since its builder
  is handed to `chunk.ToPatch` before the append. No rerun was made, so this is a reading, not a count.
- Decision (`equiv-adr` bar test): no IR node, no verdict change in meaning and no line in VERIFICATION-MODEL section 1
  -> a lowering rule, in section 3 and `IOPERATION-COVERAGE.md`. (b) leans on the builder's members handing it to no
  other code, a fact about `StringBuilder` of the kind P2-139 read off `AppendFormatted`, and on a builder nothing can
  reach being unobservable once the body has thrown, which is reachability, not an assumption about hole code.
- Decision: where the rule is decided -> on the call as it binds (`SemanticModel.GetOperation` of the graph's call
  syntax), whose parts carry their holes as written, and the lowering then reads the graph's own part calls, a
  branching hole as its capture. Alternatives: decide on the graph, where a branching hole is only a capture and a
  hole that names the builder can hide behind one. Rule: 1.
- Decision: the representation -> the handler's capture and its part calls lower to nothing, and the call to the call
  of `Append(string)` or `AppendLine(string)` (same name, the one `string` overload) on the receiver with the parts
  joined by P2-086's `Concat` chain, the chain's call site the interpolated string (the handler creation's syntax) as
  on the legacy side. The modern IR is then the legacy IR up to block layout and temporary names: the same calls with
  the same arguments in the same order, which `BothBindingsMakeTheSameCalls` checks by running both through the
  interpreter, with a builder that is not null and with one that is. Alternatives: an `IrPure` per append (a
  catalogue entry, and `Append(string)` is not pure). Rule: 1.
- Decision: the oracle (criterion 4) -> two generated statements in `MethodWithLambda`, drawn as one entry: `z = (new
  StringBuilder().Append($"p{s}q{x}") != null);` (shape (a)) and a block that appends `$"p{s}q{o}r{x}"` and
  `AppendLine($"{x}")` to a local builder and reads its `Length` (shape (b), with the sealed `Cell` hole whose
  `ToString()` adds 1 to `G`). `CompiledRunOracle` answers the builder's constructor, `Append(string)`,
  `AppendLine(string)` and `get_Length`, and `AssertReached` asks for both appends, so under the default version the
  handler binding must have lowered. At weight 2 the pinned seed no longer draws `(F, x) = (`, so the entry has weight
  1. Alternatives: a third generator (a second compile per case). Rule: 4.
- Deviation: `docs/VERIFICATION-MODEL.md` (section 3), `tests/Equiv.TestSupport/LoweringOracleGen.cs` and
  `samples/README.md` are touched besides the code, its tests and the coverage table: `equiv-extend-ir` step 1,
  criterion 4 and the sample list need them. `IOPERATION-COVERAGE.md` gains rows for `InterpolatedStringHandlerCreation`,
  `InterpolatedStringAppendLiteral`, `InterpolatedStringAppendFormatted` and
  `InterpolatedStringHandlerArgumentPlaceholder`, which the new sample's modern side is the first to hold
  (`SampleLoweringTests.EveryOperationKindInTheSamplesHasACoverageRow`).
- `StringBuilder sb = new();` binds its initializer as an implicit conversion of the creation, not as the creation;
  the rule accepts that conversion only when it has no operator method, so a user-defined conversion that could hand
  back a shared builder is not taken (`ABuilderOtherCodeMayReachKeepsTheHandler`).
- **Criterion 1.** `samples/append-interpolated-string` (`Header`, `Count` with an `int` hole, `Line` with
  `AppendLine`): each is EQ001, `bounded`, exit 0. With the rule switched off the same run gives three EQ002.
- **The row (`System.Text.StringBuilder::Append(ref ` and `AppendLine(ref `).** It no longer fires in the first pass on
  a body this ticket lowers, since the modern call is then `Append(string)` or `AppendLine(string)`. The IL pass, which
  runs only on pairs the first pass leaves Unknown (ADR 0049), still reads `Append(ref ...)` in the IL, so on a lowered
  body it fires only when something else keeps the pair Unknown; the append itself no longer does. `GitBlameCommitTests.TestToString()` is not lowered: two of its holes are
  `DateTime` values, which P2-086 and P2-139 do not cover, so its EQ006 stays. Filed with `PatchManager.ToPatch` as
  P2-211.
