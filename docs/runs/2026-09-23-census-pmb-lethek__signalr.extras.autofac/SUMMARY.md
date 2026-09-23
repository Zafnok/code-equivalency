# census run: pmb-lethek__signalr.extras.autofac

- Pair: agent, lethek/SignalR.Extras.Autofac, legacy 3a4ac841ad23, modern agent migration (local commit 6e4d0bbc9735)
- Corpus list: Poly-MigrationBench @ b0a91412d64a
- Migrated by: Claude Code subagent / Sonnet 5 / 2026-09-23, prompt `tools/corpus/migration-prompt.md`
- equiv: 87b730e, mode census, wall-clock 8 s, exit 4 (projects skipped)

## Load
- Projects: legacy 2/5, modern 2/5; skipped on both sides: `ExampleUsingOWIN` (CS0234, packages not
  restored), `ExampleUsingIIS` (CS0246, packages not restored), `_build` (CS0400, `net6.0` build
  script, not restored). None of the three has a `Build.0` entry in the solution, so neither the
  solution's restore nor its build touches them.
- Project load rate: 40%

## Census
| | legacy | modern |
|---|---|---|
| procedures | 25 | 26 |
| analysed lines | 389 | 394 |

- Matched pairs 25; without opaque 2 (8.0%); whole-body opaque 11 (44.0%); congruent n/a (M3-015 not landed)
- Unchanged share: 100% ("unchanged files" proxy: 33 of 33 legacy `.cs` files, 927 of 927 lines)
- Lowerable share: 8.0%

Top opaque reasons (all 14; bodies holding at least one opaque with the reason):

| reason | legacy | modern | owning ticket or "none" |
|---|---|---|---|
| Conversion | 9 | 9 | M3-010 (implicit reference and boxing), M4-005 (downcasts), M4-002 (floating-point and decimal) |
| using | 6 | 6 | M4-001 |
| IsNull | 4 | 4 | none → P2-008 |
| ConstructorBodyOperation | 3 | 3 | M4-001 |
| EventReference | 3 | 3 | none → P2-004 |
| foreach-enumerator | 2 | 2 | M4-001 |
| ArrayCreation | 1 | 1 | none → P2-001 |
| DelegateCreation | 1 | 1 | M4-004 |
| EventAssignment | 1 | 1 | none → P2-005 |
| PropertyReference | 1 | 1 | M3-010 |
| ref-argument | 1 | 1 | M4-003 |
| switch-pattern | 1 | 1 | M4-005 |
| TypeOf | 1 | 1 | none → P2-002 |
| undefined | 1 | 1 | none → P2-009 |

## Tests (agent's report)
- legacy: not run; modern 11 pass, 0 fail, 0 skip
- Passed on legacy, failed on modern: none known
- The agent retargeted the two built projects only and left both example projects on
  .NET Framework 4.8.1.

## Findings
- Before the run environment set `NoWarn=NU1701;NU1702;NU1903` for restore, the modern side failed
  as a whole (`UnsupportedSolution`) because NuGet warning NU1701 (package restored for
  .NET Framework) is reported as a load failure on every project: P2-012.
- The three skipped projects are outside the solution's build configuration, yet they count
  against the load rate and make the run exit 4: P2-013.
- 21 procedures are listed in `run.properties.unverified` (the skipped projects' procedures); six
  tool notifications; one EQ004 result.
