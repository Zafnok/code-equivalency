# P2-118 Decide how code a source generator emits is compared and reported
Status: done (PR #465)
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-066

## Goal
`equiv` enumerates every method in a compilation, including the ones a source generator adds. The
regex generator emits a matcher for each `[GeneratedRegex]`, and what it emits depends on the target
framework. On P2-066's `jellyfin-13023` (net8.0 to net9.0), where no regex pattern was edited, the
namespace `System.Text.RegularExpressions.Generated` holds 137 results: 120 congruent, and 17 that
nobody wrote and nobody can review:
- 4 EQ002: `WhiteSpaceRegex_0.RunnerFactory.Runner::TryFindNextPossibleStartingPosition(System.ReadOnlySpan<char>)`
  and three `Utilities::IndexOfNonAsciiOrAny_<hash>(System.ReadOnlySpan<char>)`;
- 8 Unknown (4 `unaligned-loop`, 4 `abstraction`), 4 `unmatched-overload`, 1 Removed.

The generated helpers are also named by a hash of their content, so a helper whose text changes is
Removed on one side and unmatched on the other. Separately, each `[GeneratedRegex]` partial method
is a whole-body opaque pair with reason `no-body` (30 on this pair), because its body is in the
generated half.

Decide, through `equiv-adr`, what `equiv` does with generated code. Candidates: compare the
declaration that drives the generator (the pattern and options of `[GeneratedRegex]`) and treat the
emitted matcher as its implementation, congruent when the declaration is; keep comparing the emitted
code but report it in its own review group, ranked last; or skip generated trees and say so in
`run.properties`. Then implement the decision for the regex generator.

## Spec references
ADR 0018 (what is observable), ADR 0029 (nothing is dropped silently), P2-064 (review list groups),
P2-098 (generated build-info constants), `docs/runs/2026-10-03-upgrade-verdict.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. The decision is recorded as the bar test says (an ADR, or a clarification on an existing one).
2. A sample pair with one unchanged `[GeneratedRegex]` method, legacy `net8.0` and modern `net9.0`,
   has no EQ002 and no Unknown that names generated code.
3. The same sample with the pattern edited on the modern side is not Equivalent.

## Tests
Integration tests on the two samples.

## Out of scope
Generators other than the regex one: list in `## Notes` which others the corpus pairs use.

## Notes
- Found by P2-066 on `jellyfin-13023`. P2-116 owns the three generated methods that crash lowering.
- Decision (criterion 1, `equiv-adr` bar test, 2026-10-10): a new ADR, 0055, accepted. It narrows ARCHITECTURE.md's frontend step 2, adds a field to the Core contract `FrontendAnalysis` and a run property to the SARIF, and changes what an Equivalent claims for a `[GeneratedRegex]` member, so it is the bar test's last row. A `[GeneratedRegex]` member the regex generator implemented is fingerprinted by its signature and its bound attribute on every runtime pair; the matcher types the generator emits are no procedures and are counted in `run.properties.generatedProcedures`. Alternatives: keep comparing the emitted code in a review group ranked last; skip generated trees and only say so. Rule: `equiv-adr` bar test, last row.
- Deviation: `equiv-adr` step 1 says an ADR is proposed in its own PR before the change. The ADR is written as accepted in this PR, with the code: the ticket's criteria are the decision and its implementation together, and one ticket is one PR. ADR 0047 (P2-068) and ADR 0053 (P1-030) were merged the same way.
- Decision: how generated code is recognised -> by the generator's own `[GeneratedCode("System.Text.RegularExpressions.Generator", ...)]` on the symbol. Alternatives: the path of the generated tree, the namespace `System.Text.RegularExpressions.Generated`. Rule: 1 (the attribute is what the generator writes on everything it emits, and a `Compilation` does not say which trees a generator added).
- Decision: which attributes are in the text -> `[GeneratedRegex]` alone. Alternatives: every attribute of the member, as P2-107 writes. Rule: 3. The member also carries the generator's `[GeneratedCode]`, whose version differs between any two runtimes, and no other attribute is read by the generator.
- Decision: partial properties -> included (the `get` accessor of a `[GeneratedRegex]` partial property, C# 13). Alternatives: methods only, as P2-107 chose for the implementing-part rule. Rule: 1. P2-107 left properties out because they were not in its measurement; here the declaration is the attribute, and it reads the same on a property.
- Decision: the runtime rule for the declaration -> runtime-sensitive when a `runtime-changes.json` row for `System.Text.RegularExpressions.Regex::.ctor` applies inside the interval, with no argument read for a row's precondition. Alternatives: read the attribute's pattern and options for the `caseInsensitivePattern` precondition. Rule: 4. The one row today changed in .NET 7, and no pair with `[GeneratedRegex]` on both sides crosses it.
- Decision: the run property -> `run.properties.generatedProcedures` `{ legacy, modern }`, written only when a count is not zero. Alternatives: always written; one entry per generator. Rule: 4. Always writing it would change every sample's snapshot for a zero.
- Decision: the samples -> two, `generated-regex-unedited` and `generated-regex-edited`, .NET 8 against .NET 9. The first cannot be named `generated-regex`: its namespace would be `Equiv.Samples.GeneratedRegex`, and `[GeneratedRegex]` inside it binds to the namespace (CS0616). Rule: 3.
- Decision: `IlSamples` leaves the four sides out. It compiles a side's sources with no generator, where a `[GeneratedRegex]` method is CS8795. Alternatives: a hand-written implementing part, as P2-107's sample has. Rule: 3. The criterion is about what the real generators emit for two runtimes.
- `no-body` in `WholeBodyReasonOwners` keeps P2-118 as its owner and stays, as `unbound` stays with M3-024. It is by design for an `extern` method (ADR 0054) and for a `[GeneratedRegex]` member (ADR 0055). What is still a gap under that reason: a partial method whose implementing part somebody wrote, or another generator wrote, lowers to one opaque (P2-107's limit); no ticket owns lowering the implementing part.
- Not rerun: `jellyfin-13023`. The criteria ask for the two samples. ADR 0055's Consequences state what the rerun is expected to show (137 fewer results, 30 fewer `no-body` Unknowns); P2-124 reruns the upgrade pairs and depends on this ticket.
- Other generators the corpus pairs use (Out of scope), read on 2026-10-10 from the modern commit of each pair in `tools/corpus/pairs.csv` with `git grep`, without a checkout:
  - `jellyfin-13023`: the regex generator only (29 `[GeneratedRegex]` declarations that start a line). Its other analyzer packages (StyleCop, IDisposableAnalyzers, BannedApiAnalyzers, MultithreadingAnalyzer) emit no code.
  - `powershell-19687`: the interop generator (68 `[LibraryImport]`). Same-runtime pair, so P2-107 and ADR 0054 already decide it; across runtimes it stays Unknown (ADR 0055 decision 5).
  - `gitextensions-9860`, `gitextensions-11372`, `gitextensions-11284`: none. The `ThisAssembly` class P2-098 met comes from the `GitInfo` package, which writes a source file from MSBuild, not from a Roslyn generator.
  - No `[LoggerMessage]`, `[JsonSerializable]` or `[GeneratedComInterface]` in any of the five.
  - Not read: `gitextensions-8522`, `duplicati-3124`, `openra-17989` and the three `eshop-*` pairs, whose legacy side is .NET Framework.
