# P2-164 Precision bug: `StringBuilder.Append($"...")` is Divergent between net5.0 and net6.0
Status: todo
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
