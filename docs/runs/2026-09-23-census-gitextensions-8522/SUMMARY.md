# census run: gitextensions-8522

- Pair: human, gitextensions/gitextensions, legacy 3f4ed21998af, modern 5190ba5c1a5f
- Corpus list: n/a (human pair from `tools/corpus/pairs.csv`)
- Migrated by: human (PR #8522, .NET Framework 4.6.1 to .NET 5)
- equiv: 87b730e, mode census, wall-clock 37 s, exit 1 (unhandled exception, no SARIF written)

## Load
- Projects: legacy n/a/48 C#, modern n/a/43 C#; skipped: n/a. The run crashed in lowering, after
  loading and before the SARIF was written, so skipped projects were never reported.
- Project load rate: n/a

## Census
| | legacy | modern |
|---|---|---|
| procedures | n/a | n/a |
| analysed lines | n/a | n/a |

- Matched pairs n/a; without opaque n/a; whole-body opaque n/a; congruent n/a (M3-015 not landed)
- Unchanged share: 74.0% ("unchanged files" proxy: 1,377 of 1,577 legacy `.cs` files,
  184,406 of 249,359 legacy `.cs` lines)
- Lowerable share: n/a

Top opaque reasons (up to 15): none. The run produced no census.

## Findings
- `IrLowerer.Destination` throws `KeyNotFoundException` ("the given key '3' was not present"),
  from `LowerBlocks` → `Fill` → `Terminate`, on some procedure of this pair. The whole run aborts
  with exit 1, so no pair gets a census: P2-010.
- One procedure's lowering exception ends the whole run instead of that procedure being reported
  and skipped (ADR 0029's containment ladder has no method rung for lowering): P2-011.
- The legacy side targets `net461`. This box's VS 2026 Build Tools has no 4.6.1 targeting pack, and
  its installer rejects `Microsoft.Net.Component.4.6.1.TargetingPack` (exit 87). The run used the
  `Microsoft.NETFramework.ReferenceAssemblies` packages through `TargetFrameworkRootPath` instead:
  P2-014.
- The modern checkout's `global.json` pins SDK 5.0.202 with no roll-forward. It was given
  `"rollForward": "latestMajor"` inside `.corpus/` so the .NET build host resolves SDK 10.0.401.
  The legacy and modern checkouts also needed `git submodule update --init` (`Externals/`) and
  `core.longpaths`: P2-014.
- Modern restore fails on NuGet audit warnings (NU1903) under the repository's own
  warnings-as-errors; restored with `NuGetAudit=false`: P2-014.
