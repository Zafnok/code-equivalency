# P2-213 Duplicati's replaced callees are declared in the pair's committed config, so a default corpus run uses P2-192
Status: in-progress
Effort: S
Model: Sonnet, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
P2-192 (done, ADR 0059) lets a user declare in the config's `callIdentityRenames` that a legacy member and its
modern replacement are one call, and reports every Equivalent that rests on the declaration
(`unprovenAssumptions`, `properties.callIdentitiesMapped`). Its own rerun of `duplicati-3124` passed a config
kept under `.corpus/` and never committed, with three entries: the remoting `CallContext::LogicalGetData(string)`
and `::LogicalSetData(string,object)` against the solution's `CallContext::GetData(string)` and
`::SetData(string,object)`, and `Duplicati.Library.Utility.HashAlgorithmHelper::Create(string)` against
`System.Security.Cryptography.HashAlgorithm::Create(string)`. With it, 8 of P2-130's 11 false EQ002 became EQ001.

`corpus.ps1 -Compare` passes `tools/corpus/configs/<slug>.json` when it exists (P2-197), and only
`openra-17989.json` does. So P2-212's default run of `duplicati-3124` (`docs/runs/2026-10-15-precision-verdict.md`)
has all 11 of P2-192's results still EQ002, and the 9 of them in its sample are false positives whose cause has a
done fix: 9 of the 26 false positives in Duplicati's sample, the pair's largest cause.

When done, the pair's committed config declares the three entries, and a default corpus run of `duplicati-3124`
reports them as P2-192 did.

## Spec references
ADR 0059, `docs/tickets/done/P2-192-*.md` (Notes: the three entries and their run),
`docs/tickets/done/P2-197-*.md` (the committed pair config and its README source), `tools/corpus/README.md`
"Pair configs", `.claude/skills/equiv-corpus-run`.

## Acceptance criteria (all must hold; nothing beyond them)
1. `tools/corpus/configs/duplicati-3124.json` holds the three `callIdentityRenames` entries, and
   `tools/corpus/README.md` "Pair configs" gives each one's source: the pull request's replacement of the member,
   one for one, as P2-192's Notes traced it.
2. A `tools/corpus/tests` case asserts the file's entries, as `PairConfig_OpenRaStatesItsModsRunOnNet5` does for
   OpenRA's.
3. `duplicati-3124` rerun in compare mode quick, `--jobs 4`; Notes list P2-192's 11 procedures with their new
   results, next to P2-192's table.

## Tests
The `tools/corpus/tests` case.

## Out of scope
New entries beyond the three. The AWS SDK call (P2-205) and the renamed static getter (P2-206).

## Notes
- Filed 2026-10-15 by P2-212 (criterion 6).
- Whether a corpus run may hold a declaration (it is the user's unproven assumption, ADR 0059) is the first thing
  to settle through `equiv-adr`'s bar test: P2-197's config states a fact about the build (a runtime), this one a
  claim about behaviour. Every result that rests on it names it, so the scoreboard's Equivalent rows would carry
  the assumption visibly.
