# P2-016 `adapters-shortest-paths-dotnet`'s modern side loads zero procedures
Status: in-progress
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
In the M3-031 rerun (2026-09-24), `pmb-tomasjohansson__adapters-shortest-paths-dotnet`'s census
reported `procedures: {legacy: 5, modern: 0}`, `matchedPairs: 0`, `run.properties.unverified` with
320 entries, and exit code 4. `projectsSkipped` was legacy 6 / modern 0: the 6 legacy test
projects fail restore ("Your project file doesn't list 'win' as a RuntimeIdentifier"), which is a
plausible, separate finding, but does not explain why the modern side — 0 projects skipped, same
13-project solution — analysed no procedures at all, or why the legacy side (7 projects that did
restore) produced only 5. M3-022 (2026-09-23, same repo, same commit) reported 100% load rate and
312 matched pairs.

## Spec references
`docs/runs/2026-09-24-census-pmb-tomasjohansson__adapters-shortest-paths-dotnet/SUMMARY.md`;
VERIFICATION-MODEL's `run.properties.unverified` definition; M3-024, P2-013 (project loading).

## Acceptance criteria (all must hold; nothing beyond them)
1. Root-cause why the modern side's procedure count dropped to 0 and the legacy side's to 5,
   despite `projectsSkipped: {legacy: 6, modern: 0}` implying most projects loaded. Compare
   against the environment this ticket was filed from (a fresh worktree's `.corpus/refasm`,
   `NoWarn`, `TargetFrameworkRootPath`) versus M3-022's, since the same repo pinned at the same
   commit scored differently under each.
2. Fix the cause, or, if it is environmental (not a product bug), say so and record what
   isolates it.
3. A rerun of the `census` mode on this pair after the fix matches M3-022's `matchedPairs` order
   of magnitude (not necessarily the exact count, since M3-030 and P2-013 changed what counts).

## Size guard
If the cause is in `corpus.ps1`'s restore or environment setup, fixing it there is in scope (it is
what this ticket is about). If it turns out to be an `Equiv.Frontend.CSharp` loading bug, that is
still in scope, but stop and write a new ADR-bar check first if the fix would touch matching
semantics rather than loading.

## Out of scope
The `win` RuntimeIdentifier restore failure on the 6 legacy test projects — file that as its own
finding if it is not already covered by an existing ticket.

## Notes
- Filed from M3-031's rerun; that ticket recorded the gap (`(no opaque)` census: `changedPairs 0`
  either way, since this is a pure-retarget agent pair) and moved on rather than debugging it, per
  the corpus-run skill's "record, don't fix" rule.
- 2026-09-25 root cause (criterion 1): a product bug in `Equiv.Frontend.CSharp`, exposed by the
  environment. Every legacy library project multi-targets (`net20;net40`, QuikGraph
  `netstandard2.0;net35;net40`), and MSBuildWorkspace loads one compilation per flavour, all under
  one assembly name. Each declaration was therefore on the legacy side 2-3 times, and
  `StableIdentityMatcher` put all 396 shared identities in Ambiguous: 0 pairs, 0 Added (so modern
  `procedures` 0, which counts pairs + Added), and legacy 5 = the net20-only `DotNet20.HashSet`
  members (Removed). The census does not count Ambiguous, so nothing showed it. Reproduced with the
  M3-031 worktree's `.corpus` and `-Env` (legacy 5, modern 0, 0 pairs; 15 legacy compilations).
- Why M3-022 scored differently on the same commit: its box had no usable net20/net35 reference
  assemblies (`.corpus/refasm/root` from an early `-Prepare`, and no `-Env`/`TargetFrameworkRootPath`
  yet; P2-014 was filed from that run). Those flavours bound no corlib, so they never produced clean
  duplicate identities, leaving one usable flavour per project. Once P2-014's `-Prepare` supplied
  net20-net481 and `-Env` pointed at them, every flavour bound and the duplicates appeared. The
  `NoWarn` difference is unrelated. Re-running today's code on M3-022's `.corpus` without `-Env`
  skips nearly every legacy flavour (CS0518 "System.Object is not defined").
- Decision: collapse target-framework flavours in the frontend, before matching: of the procedures
  (and endpoints) that share an assembly name and an identity, keep only those from the last
  compilation holding it (flavours load in `TargetFrameworks` order, conventionally ending at the
  newest framework, the one nearest a migration's target). Duplicates within one compilation and
  across assemblies (a linked file, e.g. ShortestPaths' `Test/Utils/*.cs`) stay Ambiguous. A
  declaration only one flavour compiles stays that side's own. Keyed on assembly name because
  `LoadedSolution` carries compilations only; `CodeLines` already dedupes flavours by file path.
- ADR bar: no new ADR. VERIFICATION-MODEL section 4 reserves Ambiguous for overload mapping; a
  per-flavour duplicate is not an overload, so this applies the spec to a case it did not spell
  out. The matcher and its rule are unchanged (Size guard: loading, not matching semantics).
- Criterion 3 rerun (2026-09-25, M3-031 worktree's `.corpus` and `-Env`, `--lower-only`): procedures
  legacy 401 / modern 396, matchedPairs 396 (M3-022: 312), pairsWithoutOpaque 302, whole-body opaque
  33, congruent 376, changedPairs 20 (11 without opaque), projectsSkipped legacy 6 / modern 0,
  unverified 320 (the skipped test projects' procedures and their modern counterparts), exit 4 (the
  skips). The 20 changed pairs are new information, since M3-031 could not see any pair; not
  investigated here (nothing beyond the criteria).
- P2-018's premise ("the modern side extracted 0 procedures") was a symptom of this bug: that side
  extracted all its procedures, and they all went Ambiguous. Its general guard may still be wanted;
  its Goal text is left as written.
- Out of scope, filed as P2-021: the 6 legacy test projects' "doesn't list 'win' as a
  RuntimeIdentifier".
