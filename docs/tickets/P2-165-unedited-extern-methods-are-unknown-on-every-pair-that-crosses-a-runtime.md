# P2-165 An unedited `extern` method is Unknown on every pair that crosses a runtime
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-145, P2-146

## Goal
ADR 0054 decision 4 gives an `extern` method no fingerprint on a pair whose runtimes differ, so
every matched one is EQ003 (`opaque`, `no-body`), and a body that declares an `extern` local
function is not congruent. The ADR's Consequences say the Unknown count of a migration rises by the
number of its imports. P2-124's rerun measured it on an upgrade
(`docs/runs/2026-10-12-upgrade-verdict.md`):

| Pair | `no-body` Unknowns | Share of changed pairs | In files the pull request edits |
|---|---|---|---|
| gitextensions-9860 (net5.0 to net6.0) | 53 of 605 | 8.8% | 1 of 53 (the method itself is not edited) |
| jellyfin-13023 (net8.0 to net9.0) | 2 of 122 | 1.6% | 0 |

On `gitextensions-9860` it is the largest opaque reason, and every one of the 53 is a `[DllImport]`
declaration whose text is the same on both sides. The native function a `[DllImport]` names does
not change with the .NET version. What can change is the runtime's marshaller and, on a migration
from .NET Framework, the defaults it applies.

Decide, through `equiv-adr` (this changes decision 4 of an accepted ADR, so it is an amendment or a
new ADR, not a detail), when an `extern` pair that crosses a runtime may be Equivalent by
congruence. The starting proposal: fingerprint an `extern` method on every pair as ADR 0054 does on
one runtime (the import, its attributes, and P2-146's settings from outside the function), and make
the fingerprint runtime-sensitive only when a runtime-change row inside the pair's interval names a
marshalling behaviour the declaration uses. The first deliverable is the list of such behaviours:
read the breaking-change pages for interop from .NET Framework 4.8 to .NET 10 and say, for each,
which declarations it reaches (for example a `string` or `StringBuilder` parameter with no
`CharSet`, `SetLastError`, `PreserveSig`, a `bool` with no `[MarshalAs]`, COM interface parameters,
`[BestFitMapping]`). If the list cannot be made complete, say so in the ADR and keep decision 4
for the intervals it cannot cover.

## Spec references
ADR 0054 (all of it), ADR 0040 (pair kinds, the interval rule), ADR 0024 (congruence), ADR 0034
(changed pairs), `runtime-changes.json`, `docs/tickets/done/P2-145-soundness-extern-attributes-are-not-compared.md`,
`docs/tickets/done/P2-146-soundness-interop-settings-outside-the-function.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. The ADR change is written and accepted through `equiv-adr`, with the list of marshalling
   behaviours that differ per interval and where each came from.
2. A sample pair (identical file, legacy `net5.0`, modern `net6.0`) with one `[DllImport]` the list
   does not reach is congruent, and one the list reaches stays EQ003 or becomes EQ006 under its row.
3. A .NET Framework 4.8 to .NET pair keeps decision 4 for every declaration the list reaches, with a
   test.
4. An edited `[DllImport]` on a pair that crosses a runtime is not Equivalent.
5. `## Notes` gives, from one quick run of `gitextensions-9860` through `equiv-corpus-run`, the
   `no-body` Unknown count before and after.

## Tests
Integration tests on the samples of criteria 2 to 4. A table test over the new rows.

## Out of scope
`[LibraryImport]` partial methods on a pair that crosses a runtime (P2-107 left them as they were;
list what the run of criterion 5 shows). Modelling the native function.

## Notes
- Found by P2-124 (criterion 5: an opaque reason at or above 5% of a pair's changed pairs with no
  open ticket).
