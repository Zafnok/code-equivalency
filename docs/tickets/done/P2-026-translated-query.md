# P2-026 LINQ query syntax (`TranslatedQuery`) has no lowering, and one crashes the frontend
Status: done (PR #241)
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M2-004, M4-004

## Goal
M4-007's first real run found `TranslatedQuery` in Git Extensions' opaque reasons (8
occurrences), and no ticket or `IOPERATION-COVERAGE.md` row owns it. Minimal repro:

```csharp
static int SumEvens(int[] xs) { int s = 0; foreach (var x in from n in xs where n % 2 == 0 select n) s += x; return s; }
```

A related, worse case crashed the frontend outright on the same run: lowering
`GitCommands.UserRepositoryHistory.RecentRepoSplitter::SplitRecentRepos` threw
`InvalidCastException: Unable to cast object of type 'Microsoft.CodeAnalysis.CSharp.Syntax.FromClauseSyntax' to type 'Microsoft.CodeAnalysis.CSharp.Syntax.StatementSyntax'`.
That method's query expression is not the top-level repro above; find the exact shape (a `from`
clause nested somewhere the frontend's statement walker does not expect) and add it as a second
repro/regression test here before fixing it, since criterion 3 of `equiv-corpus-run`'s hard rules
is "record, don't fix" during the run itself, but this ticket is the fix.

## Spec references
`docs/tickets/IOPERATION-COVERAGE.md` (no `TranslatedQuery` row yet); `DelegateCreation`'s row
(M4-004; a query clause with a lambda-like body is one fragment, same idea); the `equiv-extend-ir`
skill.

## Acceptance criteria (all must hold; nothing beyond them)
1. A whole `from`/`where`/`select` query expression lowers as one opaque fragment per
   `DelegateCreation`'s existing shared-fragment rule (a query is already named as an example of
   such a fragment in that row's Status text) — verify this actually happens for the repro above,
   or fix the frontend so it does, whichever `equiv-decide` picks; add the
   `IOPERATION-COVERAGE.md` row for `TranslatedQuery` pointing at that mechanism.
2. The `RecentRepoSplitter` crash no longer reproduces: add its query shape (or a reduced version
   of it) as a regression test, and confirm the frontend lowers or opaques it instead of throwing.
3. Snapshot test for the repro.

## Size guard
If the crash's root cause is a general statement-walker bug that also affects non-query syntax,
stop and write an ADR instead of widening this ticket.

## Out of scope
Query continuations (`into`), `group by`, `join`.

## Notes
- Criterion 1 already held: the repro's query lowers as one fingerprinted `TranslatedQuery` fragment through M4-004's mechanism, reading only `xs` (and its null shadow). No lowering change; only tests and the coverage row.
- Crash root cause: `FragmentFingerprinter.WrittenByFunctions` data-flow-analyses every lambda and local function of the graph, casting any non-expression syntax to `StatementSyntax`. A second `from` clause's collection-selector lambda has the `FromClauseSyntax` itself as its syntax. It is query-specific (a lambda's or local function's syntax is otherwise always an expression or a statement), so the size guard did not trip. The walk only runs when some fragment in the graph captures a variable, which is why the top-level repro never hit it; `SplitRecentRepos` has `from caption in orderedRepos.Keys from repo in orderedRepos[caption] ...` in a local function plus captured-variable lambdas elsewhere in the method.
- Decision: a query-clause lambda's writes are the whole enclosing `QueryExpressionSyntax`'s `WrittenInside`, a superset of the clause's, so over-approximation only withholds fingerprints and stays sound (`ALambdaCapturingAVariableANestedFromClauseWritesHasNoFingerprint` checks it discriminates).
- Regression test reduced from the real method (fetched from gitextensions/gitextensions `src/app/GitCommands/UserRepositoryHistory/RecentRepoInfo.cs`): both `ANestedFromClauseLowersWithoutThrowing` cases threw the exact `InvalidCastException` before the fix.
