# M3-022 Census of the public corpus, and ordering the precision work by it
Status: done (PR #139)
Effort: S
Model: Sonnet, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M3-014, M3-024

## Goal
ADR 0027 decision 3 and ADR 0028. Measure lowering on public migration pairs now, instead of at
M4-007. Evaluate ADR 0028's unchanged-share and lowerable-share rules against their fixed
thresholds, and use the histogram to order the M4 precision tickets. Fix nothing. (Until
2026-09-23 this ticket named an unspecified real pair. ADR 0028 replaced it with the corpus.)

## Acceptance criteria (all must hold; nothing beyond them)
1. Run the `equiv-corpus-run` skill in `census` mode on:
   - the `gitextensions-8522` human pair;
   - at least three Poly-MigrationBench repos migrated by an agent (agent pairs), chosen by the
     skill's selection rule.

   A repo that does not restore or load on this box is replaced by the next one in the selection
   order, with the reason recorded.
2. Each pair has `docs/runs/<yyyy-mm-dd>-census-<slug>/SUMMARY.md`, written in the skill's template:
   - procedure and matched-pair counts, and the analysed line counts of each side (M3-014);
   - `projectsSkipped` with reasons (M3-024);
   - `pairsWithoutOpaque`, `pairsWholeBodyOpaque` and `pairsCongruent` (if M3-015 has landed), each
     as a count and a percentage of matched pairs;
   - unchanged share, lowerable share and project load rate, as ADR 0028 defines them;
   - the top fifteen opaque reasons with counts, each mapped to the ticket that removes it, or
     "none";
   - wall-clock time.

   Raw SARIF and checkouts stay in `.corpus/`. No third-party source text is committed.
3. `docs/runs/<yyyy-mm-dd>-census-verdict.md` applies ADR 0028's rules to the Git Extensions pair
   and to the median of the agent pairs, and states the outcome: continue, re-scope or stop. If the
   outcome is re-scope or stop, stop here and tell the user. That needs a new ADR, not more
   tickets.
4. Reorder the M4 list in `docs/ROADMAP.md` by pairs unlocked per effort point (S=1, M=2, L=4).
   Soundness dependencies still win. Ticket ids are not renumbered; the list order is the order.
   An M4 ticket below ADR 0028's 5% bar moves to the post-MVP backlog, and M4-007's `Depends on:`
   line drops it.
5. For every reason in the top fifteen that no ticket covers, write one `P2-nnn-<slug>.md` with a
   minimal repro sketched in its Goal, in your own code, never copied from the corpus.
6. No changes under `src/` or `tests/`.

## Size guard
If you are editing engine code, stop; that is a new ticket.

## Out of scope
Verification. Fixes. Any code not listed in `tools/corpus/`.

## Notes
- Result: **incomplete: human pair pending** (`docs/runs/2026-09-23-census-verdict.md`). Git
  Extensions 74.0% unchanged, and no census because it crashed. Agent median: 100% unchanged,
  22.1% lowerable, 100% load rate. All three agent pairs are pure retargets, with no `.cs` file
  changed.
- Deviation (PR #139 review): criterion 4's reorder is not applied. Its lowerable numbers come
  from byte-identical bodies the solver never sees, so the M4 list and M4-007's `Depends on:` are
  unchanged. The reorder waits for a Git Extensions census after P2-011, P2-010 and P2-012.
  P2-001 to P2-009 stay as an unscheduled backlog.
- Git Extensions produced no census: `IrLowerer.Destination` throws `KeyNotFoundException` and the
  run exits 1 (P2-010; containment P2-011). Only its unchanged share could be measured.
- `chrismckelt/WebMinder` was skipped: its agent migration does not build (`System.Web`, ASP.NET
  MVC 5). `lethek/SignalR.Extras.Autofac` replaced it.
- Toolchain: nothing loaded on this box as the skill describes. Long paths, submodules, this repo's
  MSBuild files leaking into `.corpus/`, missing reference assemblies below 4.7.2 (and the VS 2026
  installer rejecting the 4.6.1 component), no .NET SDK resolver in Build Tools, a pinned SDK 5 in
  Git Extensions, and NuGet warnings reported as load failures. All of it was worked around inside
  `.corpus/` and the run environment, with no machine change. Each workaround is in P2-014 or
  P2-012.
- Decision: agent migrations ran as Claude Code subagents on Sonnet 5, given the fixed prompt
  plus one leading `Directory:` line (the subagent does not start in the pair's directory). After
  `.corpus/` was isolated, each running agent got one environment note: revert any workaround for
  the leaked MSBuild files.
- Decision: "pairs unlocked" per M4 ticket = share of matched pairs whose legacy body holds a
  reason the ticket owns, median over the agent pairs. That is an upper bound, because the SARIF
  counts bodies per reason and not reason sets per body. `Conversion` is left unattributed because
  the census cannot split it by kind. Per-point ranking uses the median, as ADR 0028 does.
- Decision: "top fifteen" for criterion 5 is taken per pair (ties broken by reason name), and a P2
  ticket is written for every uncovered reason in the union: P2-001 to P2-009. P2-010 to P2-014 are
  the run's other findings, filed as the skill requires.
- Deviation: the skill says to ask before installing a targeting pack. The user approved it, the
  installer rejected the component (exit 87), and the run used the reference-assembly NuGet
  packages instead.
- 2026-09-24, M3-031: this ticket's "incomplete: human pair pending" verdict is superseded by
  `docs/runs/2026-09-24-census-verdict.md` (**continue**), which reruns the census on the same
  pairs under ADR 0034 and reorders `docs/ROADMAP.md`'s M4 list.
