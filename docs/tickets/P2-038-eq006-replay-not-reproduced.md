# P2-038 Replay reports `not-reproduced` for runtime-changed-API (EQ006) results it cannot observe
Status: in-progress
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M4-009

## Goal
M4-007's `--execute` run on Git Extensions replayed 275 Divergents; 15 of the 16 `not-reproduced`
results are `EQ006` (a call to a member in the runtime-changes table), not solver counterexamples.
Per `docs/VERIFICATION-MODEL.md`, `not-reproduced` "is a soundness or modelling finding and gets a
ticket", but for these it is more likely the replay that cannot see the difference: it calls each
side once, under the invariant culture, with model-derived inputs, so a divergence that needs a
culture (ICU against NLS comparison), a per-process hash seed, a default encoding or a regex
engine detail cannot show.

Identities (all with `replayOutcomes` equal on both sides):
`GitUI.Script.ScriptOptionsParser::DependsOnSelectedRevision(string)`,
`GitCommands.EnvironmentAbstraction::SetEnvironmentVariable(string,string)`,
`GitCommands.GitModule::TryFindGitWorkingDir(string)`, `GitCommands.GitSshHelpers::UnsetSsh()`,
`GitCommands.PathUtil::IsLocalFile(string)`, `GitCommands.PathUtil::Combine(string,string)`,
`GitCommands.PathUtil::GetDisplayPath(string)`, `System.StringExtensions::SubstringUntil(string,char)`,
`System.StringExtensions::SubstringUntilLast(string,char)`, `System.StringExtensions::SubstringAfter(string,char)`,
`System.StringExtensions::SubstringAfterLast(string,char)`,
`GitUIPluginInterfaces.BuildServerIntegration.BuildServerSettingsHelper::IsRegexValid(string)`,
`GitUIPluginInterfaces.RepositoryHosts.GitProtocolExtensions::IsUrlUsingHttp(string)`,
`Git.hub.Repository::GetHashCode()` (both sides `threw`), and
`ICSharpCode.TextEditor.Document.DocumentFactory::CreateFromFile(string)` (both sides `threw`).

Minimal repro of the class:

```csharp
static int Find(string s, string t) => s.IndexOf(t);   // culture-sensitive: differs under tr-TR / ICU
```

## Spec references
`docs/VERIFICATION-MODEL.md` replay and differential-testing sections (the latter already re-runs
under `tr-TR` "when either body calls a member of the runtime-changes table"; replay does not);
ADR 0035 decision 2; M4-009; the runtime-changes table.

## Acceptance criteria (all must hold; nothing beyond them)
1. Decide, via `equiv-decide`, between: (a) replay also runs the model's input under `tr-TR` when
   either body calls a runtime-changes member, as differential testing does; (b) an `EQ006` result
   whose replay cannot observe a difference is `not-applicable`, not `not-reproduced`, because
   `EQ006` is a claim about the API, not a model counterexample; or both.
2. After the change, none of the identities above reports `not-reproduced` merely because the
   replay did not vary culture. Any that still do get their own ticket.
3. The two `threw`/`threw` cases (`GetHashCode`, `CreateFromFile`) are investigated separately: a
   replay where both sides throw usually means the driver failed to construct the receiver or the
   argument, and should be `not-constructible`.

## Size guard
Replay only. Do not touch the runtime-changes table or the `EQ006` rule.

## Out of scope
The one solver-derived `not-reproduced` result (P2-037).

## Notes
- Decision: which of (a) tr-TR replay and (b) `not-applicable` for EQ006 -> both. Replay runs the model's input
  under the invariant culture and, when either body calls a runtime-changes member, also under `tr-TR` (the culture set
  differential testing already uses, now one shared helper); a divergence under either culture is `reproduced`. When
  every culture agrees and the counterexample's call trace holds a runtime-changed callee (exactly what makes the result
  EQ006, `VerdictRule`), the replay is `not-applicable` with both outcomes as `replayOutcomes`, since EQ006 claims the
  API differs, not that this model input shows it. Alternatives: (a) only, which leaves the ordinal members
  (`SubstringUntil(string,char)`, `SetEnvironmentVariable`) `not-reproduced`; (b) only, which never reproduces the
  culture-dependent ones. Rule: 1 (mirror differential testing's culture set and VerdictRule's EQ006 test).
- Decision: a replay where both sides throw the same exception while the model's runs do not both throw ->
  `not-constructible`, reason `both sides threw <type>, which the model's runs do not: the receiver or an argument is not
  the model's`. It is checked before `not-applicable`. Alternatives: only when neither model run throws; compare exception
  types with the model. Rule: 4 (the smaller test that covers `GetHashCode` on `new T()` and `CreateFromFile` on a path
  that does not exist).
- Decision: `Replayer.Replay` takes the Divergent's counterexample and both bodies besides the plan, rather than the plan
  carrying flags computed in the frontend. Alternatives: `ReplayPlan.RuntimeSensitive`/`Eq006` fields. Rule: 4 (the CLI
  already holds both, and differential testing takes the bodies the same way).
- Criterion 2: all fifteen identities are EQ006 (the M4-007 summary lists them as such), so after this change each is
  `reproduced` (a divergence under `tr-TR`), `not-applicable` (equal outcomes) or, for the two below,
  `not-constructible`; none can be `not-reproduced`. No follow-up ticket is needed. The corpus was not re-run: `--execute`
  is Windows-only (ADR 0035) and this change was made on Linux; the next `equiv-corpus-run` on Windows shows the split.
- Criterion 3: both `threw`/`threw` cases are the driver's inputs, not the model's. `Git.hub.Repository::GetHashCode()`
  runs on `new Repository()`, whose string fields are null, where the model gives them values (heap reads of `this`
  are not rebuilt by replay); `DocumentFactory::CreateFromFile(string)` gets `"s<id>"`, a path that does not exist,
  where the model's file read is an uninterpreted call. Both are now `not-constructible` with the reason
  `both sides threw <type>, which the model's runs do not: the receiver or an argument is not the model's`.
