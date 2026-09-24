# census run: pmb-tomasjohansson__adapters-shortest-paths-dotnet

- Pair: agent, TomasJohansson/adapters-shortest-paths-dotnet, legacy e3722e971d86, modern agent
  migration (reused from M3-022, no new migration per M3-031 criterion 1)
- Corpus list: Poly-MigrationBench @ b0a91412d64a14b7fd61319591bd4b00a574f4c2
- Migrated by: Claude Code subagent, Sonnet 5, 2026-09-23 (M3-022)
- equiv: 51fd745, mode census, wall-clock 11s, exit 4

## Load
- Projects: legacy 7/13 C# (13 `.csproj` in the solution, 0 not built, 6 skipped), modern 13/13 C#
  (0 skipped); skipped (legacy): `Programmerare.ShortestPaths.Test`,
  `Programmerare.ShortestPaths.Adaptee.YanQi.Test`, `Programmerare.ShortestPaths.Adaptee.Bsmock.Test`,
  `Programmerare.ShortestPaths.Adaptees.Common.Test`, `Programmerare.ShortestPaths.Example`,
  `Programmerare.ShortestPaths.Adapter.QuikGraph.Test` — each: "Your project file doesn't list
  'win' as a RuntimeIdentifier"
- Project load rate: 53.8% legacy, 100% modern (nominal; see Findings — the modern side's own
  procedure count does not match a 100% load)

M3-022 (2026-09-23, same repo, same commit) reported 100% load rate and 312 matched pairs.

## Census
| | legacy | modern |
|---|---|---|
| procedures | 5 | 0 |
| analysed lines | 4090 | 7439 |

- Matched pairs 0; without opaque 0; whole-body opaque 0; congruent 0 (n/a)
- Unchanged share: 100% (`-Unchanged` proxy; file-level, unaffected by the load anomaly below)
- Pair-level unchanged share (1 - changedPairs / matchedPairs): n/a (matchedPairs is 0)

Top opaque reasons: none (0 matched pairs).

## Changed code
- Changed pairs 0 of 0; without opaque 0; whole-body opaque 0
- Lowerable share (changedPairsWithoutOpaque / changedPairs): n/a: no changed pairs

| runtime-change calls | legacy | modern |
|---|---|---|
| call sites | 0 | 0 |
| distinct members | 0 | 0 |
| pairs with any | 0 | 0 |

- Package changes: n/a — `corpus.ps1 -Packages` fails (P2-015).

## Verdicts (full and seeded only)
n/a (`census` mode).

## Tests (full only)
n/a (`census` mode).

## Seeds (seeded only)
n/a (`census` mode).

## Findings
- 6 legacy test projects fail restore ("doesn't list 'win' as a RuntimeIdentifier"), reducing the
  loaded legacy project count from 13 to 7.
- Despite that, and despite `projectsSkipped: {legacy: 6, modern: 0}` implying the modern side
  loaded fully, the census extracted only 5 legacy and 0 modern procedures (320
  `run.properties.unverified` entries), against 312 matched pairs in the 2026-09-23 run on the
  same commit. Recorded, not diagnosed further here (the skill's "record, don't fix" rule);
  filed as P2-016. This pair contributes 0 changed pairs either way (a pure retarget, consistent
  with M3-022), so it does not change M3-031's changed-pair totals or the M4 reorder.
- This pair is excluded from the lowerable-share median in any case (ADR 0034: zero changed
  pairs).
