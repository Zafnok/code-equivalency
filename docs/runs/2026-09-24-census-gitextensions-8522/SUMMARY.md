# census run: gitextensions-8522

- Pair: human, gitextensions/gitextensions, legacy 3f4ed21998af, modern 5190ba5c1a5f
- Migrated by: human, gitextensions/gitextensions PR #8522
- equiv: 51fd745, mode census, wall-clock 52s, exit 0

## Load
- Projects: legacy 47/47 C# (48 `.csproj` in the solution, 1 not built), modern 42/42 C# (43
  `.csproj` in the solution, 1 not built); skipped: none
- Project load rate: 100% legacy, 100% modern

## Census
| | legacy | modern |
|---|---|---|
| procedures | 13708 | 13556 |
| analysed lines | 186633 | 184691 |

- Matched pairs 13541; without opaque 3824 (28.2%); whole-body opaque 5027 (37.1%); congruent
  0 (n/a; token-identical proxy is not `pairsCongruent`, see below)
- Unchanged share: 74% (`-Unchanged` "unchanged files" proxy; M3-015 has not landed)
- Pair-level unchanged share (1 - changedPairs / matchedPairs): 97.7%. Not the row above; ADR 0034.

Top opaque reasons (up to 15, by legacy count):

| reason | legacy | modern | owning ticket or "none" |
|---|---|---|---|
| no-body | 2084 | 2084 | M4-008 (post-MVP backlog, below 5% bar) |
| Conversion | 1142 | 1141 | none (unattributed; split across M3-010, M4-002, M4-005) |
| ConstructorBodyOperation | 1003 | 1003 | M4-001 |
| DelegateCreation | 956 | 956 | M4-004 |
| ArrayCreation | 939 | 918 | P2-001 |
| switch-pattern | 933 | 932 | M4-005 (post-MVP backlog, below 5% bar) |
| Binary | 769 | 765 | M4-002 |
| Block | 716 | 716 | M4-008 (post-MVP backlog, below 5% bar) |
| foreach-enumerator | 556 | 554 | M4-001 |
| IsNull | 536 | 535 | P2-008 (unscheduled backlog, below 5% bar) |
| using | 360 | 360 | M4-001 |
| ref-argument | 304 | 301 | M4-003 (post-MVP backlog, below 5% bar) |
| EventAssignment | 248 | 249 | P2-005 (unscheduled backlog, below 5% bar) |
| InterpolatedString | 239 | 238 | none |
| DefaultValue | 236 | 236 | P2-003 (unscheduled backlog, below 5% bar) |

## Changed code
- Changed pairs 315 of 13541; without opaque 77; whole-body opaque 71
- Lowerable share (changedPairsWithoutOpaque / changedPairs): 24.4%

Top reason sets (up to 15; "" = no opaque):

| reason set | changed pairs | owning tickets or "none" |
|---|---|---|
| (no opaque) | 77 | already lowerable |
| foreach-enumerator | 29 | M4-001 |
| using | 19 | M4-001 |
| ArrayCreation | 18 | P2-001 |
| ArrayCreation+DelegateCreation | 16 | P2-001, M4-004 |
| switch-pattern | 10 | M4-005 (backlog) |
| async | 9 | M4-006 (backlog) |
| DelegateCreation | 7 | M4-004 |
| IsNull | 6 | P2-008 (backlog) |
| InterpolatedString | 6 | none |
| Binary | 6 | M4-002 |
| ConstructorBodyOperation | 6 | M4-001 |
| Binary+InterpolatedString | 3 | none (InterpolatedString unowned) |
| Conversion | 3 | none |
| ArrayCreation+switch-pattern | 3 | P2-001, M4-005 (backlog) |

| runtime-change calls | legacy | modern |
|---|---|---|
| call sites | 185 | 187 |
| distinct members | 19 | 19 |
| pairs with any | 121 | 123 |

- Package changes: n/a — `corpus.ps1 -Packages` fails on this checkout (P2-015: `Get-ChildItem
  -Recurse -Include` is not applied on this PowerShell version, so it scans every file under the
  repo and throws parsing a non-JSON one).

## Verdicts (full and seeded only)
n/a (`census` mode).

## Tests (full only)
n/a (`census` mode).

## Seeds (seeded only)
n/a (`census` mode).

## Findings
- No `IrLowerer.Destination` crash: exit 0, 0 tool execution notifications, 0 unverified
  procedures. P2-010 criterion 3 / M3-031 criterion 7 holds; no new ticket needed.
- `corpus.ps1 -Packages` cannot run on any pair: P2-015.
