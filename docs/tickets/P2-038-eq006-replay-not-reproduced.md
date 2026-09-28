# P2-038 Replay reports `not-reproduced` for runtime-changed-API (EQ006) results it cannot observe
Status: todo
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
