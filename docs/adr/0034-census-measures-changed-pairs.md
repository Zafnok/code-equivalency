# ADR 0034: The census measures what the solver will see: changed pairs, their reason sets, and what congruence cannot vouch for

Status: accepted (2026-09-23). Supersedes ADR 0028 decision 5 in part: the lowerable-share row is
evaluated on changed pairs, not on all matched pairs.

## Context
M3-022's census (PR #139) could not reach a verdict. Git Extensions, the only pair with changed
code, crashed in lowering (P2-010). The three agent pairs are pure retargets: every `.cs` file is
byte-identical, because the migration prompt forbids modernising code that compiles. Their "22.1%
lowerable share" counts bodies that M3-015 will prove Equivalent by congruence, so Z3 never sees
them. ADR 0028's lowerable-share denominator, all matched pairs, therefore measures the wrong
population. The census also counts bodies per opaque reason, not the set of reasons per body, so
"pairs unlocked by an M4 ticket" cannot be computed. On a retarget, the trust question is runtime
and package behaviour (one agent raised NHibernate from 5.2.7 to 5.5.2), and the census measures
neither.

## Decision
The census reports four additions, and ADR 0028's two lowerable-share rules are evaluated on
changed pairs. A **changed pair** is a matched pair that is not congruent. After M3-015,
"congruent" means `pairsCongruent`'s definition (ADR 0024). Until then a pair counts as congruent
only if the two bodies' syntax token sequences are identical once trivia is ignored **and** neither
lowered body calls a member that `RuntimeChangeTable` matches. That second condition mirrors ADR
0024's runtime-sensitivity for the part the census can already see.
1. `changedPairs`, `changedPairsWithoutOpaque`, and `changedPairsWholeBodyOpaque`. **Lowerable
   share** becomes `changedPairsWithoutOpaque / changedPairs`. The 15% and 5% thresholds are
   unchanged.
2. `changedReasonSets`: a histogram over changed pairs, keyed by the sorted union of both sides'
   opaque reasons. A ticket's **pairs unlocked** is the number of changed pairs whose reason set
   is a subset of the reasons the ticket removes, plus those that earlier tickets in the order
   already remove. It is exact, not an upper bound.
3. `runtimeChangeCalls`: per side, the call sites in lowered bodies whose `CallIdentity` matches
   `RuntimeChangeTable`, the distinct members among them, and the matched pairs holding at least
   one (congruent pairs included). This measures how often EQ006 fires on code congruence would
   otherwise vouch for.
4. `packageVersionChanges`: the NuGet packages whose resolved version differs between the two
   sides, and those present on only one side. `tools/corpus/corpus.ps1 -Packages <slug>` computes
   it from each side's restore output. It stays out of SARIF until the data shows whether equiv
   should report it.

Evaluation: an agent pair with zero changed pairs is excluded from the lowerable-share median.
If fewer than three agent pairs remain, the lowerable-share rules are applied to Git Extensions
alone, and the verdict file says so.

ADR 0028's unchanged-share row keeps its definition, including the byte-identical-files proxy
before M3-015. That proxy is computed from `git diff` and still works when a census run crashes,
as Git Extensions' did. The verdict file reports `1 - changedPairs / matchedPairs` next to it,
labelled as the pair-level figure, so the two are never read as one number.

## Why
- The solver's precision only matters on code the migration changed. Counting identical bodies
  inflates the share on retargets and would deflate it on no corpus pair. A pure retarget scores
  "22% lowerable" and zero changed pairs at once.
- Reason sets make the M4 order a computation instead of an estimate. PR #139 had to rank tickets
  by upper bounds and leave `Conversion` (153 bodies) unattributed.
- For pure retargets, EQ006 exposure and package drift are nearly all the value equiv can add. The
  census should show that value, or show that it is missing.
- The thresholds do not move, only the population they are applied to. This ADR is written before
  any Git Extensions census exists, so it cannot be fitted to that data. It is written after PR
  #139's agent data, though, and the evaluation paragraph is shaped by it: three pure retargets
  would leave the agent median empty. In practice that means Git Extensions alone decides the
  lowerable-share rules until an agent pair changes code, even though ADR 0028 calls agent pairs
  the production use case. The unchanged-share rule still uses the agent median.

## Rejected
- **Keep ADR 0028's denominator and write "n/a" for retargets.** That leaves the one number that
  orders M4 computed over bodies the solver never sees.
- **Wait for M3-015 instead of the token proxy.** M3-015 is L and needs M3-009 first, and the Git
  Extensions rerun would wait on it. The proxy's error goes both ways, and this ADR accepts that:
  - Token-different bodies that M3-015's rename maps and `api-equivalences.json` rewrites make
    equal count as changed. This overcounts changed pairs, which makes the share conservative.
  - Token-identical bodies whose names bind to different symbols on the two runtimes (a new
    overload or extension method in .NET 10) count as unchanged although their fingerprints
    differ. This undercounts changed pairs, and the census cannot see it without binding, which
    is M3-015's job.
  - Excluding pairs that call a runtime-changes member from congruence removes the undercount
    that ADR 0024's runtime-sensitivity would otherwise add.
- **Change the migration prompt so agents modernise code.** That would make agent pairs look like
  rewrites no user asked for. Retargets are the real agent output, and the census should measure
  them as they are.
- **Report package drift as a SARIF result now.** That is a new product surface (a rule id), and
  deciding on one needs the data this ADR collects first.
- **Replace ADR 0028's unchanged-share proxy with the pair-level token proxy.** The stop rule
  would then depend on a census run succeeding, and Git Extensions' did not.

## Consequences
- ADR 0028's status records that decision 5 is superseded in part by this ADR. Its unchanged-share,
  load-rate, Unknown-share and seeded-recall rows are untouched.
- M3-030 implements items 1 to 4. It also updates the census paragraph in VERIFICATION-MODEL.md
  and the `equiv-corpus-run` skill's SUMMARY.md template and verdict step. Those docs change when
  the keys exist, not before.
- M3-031 reruns the census on Git Extensions and scores it under this ADR. It needs M3-030,
  P2-010, P2-011 and P2-012.
- The `run.properties.loweringCensus` shape grows. It is still a property bag in the existing
  SARIF (ADR 0027), not a parallel schema.
- M3-022's verdict stays "incomplete: human pair pending" until M3-031.
