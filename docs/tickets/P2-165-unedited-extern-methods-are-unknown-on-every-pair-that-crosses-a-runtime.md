# P2-165 An unedited `extern` method is Unknown on every pair that crosses a runtime
Status: in-progress
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
   (The list reaches no declaration between `net5.0` and `net6.0`, so the reached one is shown on the
   same file from `netcoreapp3.1`; see the Deviation line in Notes.)
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

### Criterion 1: the decision
- Bar test (`equiv-adr`): the fourth row, a new ADR. It supersedes a decision of an accepted ADR
  (ADR 0054 decision 4). ADR 0058, accepted in this pull request with its implementation, as P2-118
  did with ADR 0055; ADR 0054's Status line says decision 4 is superseded in part.
- The list: the Interop category of the breaking-change pages for .NET Core 3.0 and .NET 5 to .NET 10,
  read 2026-10-14, fifteen items (3.0 has no Interop category; 5.0 three, 6.0 one, 7.0 one, 8.0 six,
  9.0 one, 10.0 three), each traced to its item page. ADR 0058 decision 3 has the table: id,
  `changedIn`, what changed, the page, and which declarations it reaches. Five reach none (a language
  feature, a property a member row covers, two compile-time rules of the source generator, and
  JavaScript interop). `docs/runtime-changes-review.md` already listed the same fifteen; each line now
  ends in ` — marshalling: <id>`, and `ReviewFileAndTableAgree` ties the two.
- The list cannot be made complete at the .NET Framework boundary: the `fx-core` page has no Interop
  category, and the marshaller differs there (the last error is cleared before a `SetLastError` call
  on .NET, not on .NET Framework, per `DllImportAttribute.SetLastError`'s remarks; built-in COM and
  WinRT). So, as this ticket's Goal allows, decision 4 stays for every interval that reaches .NET
  Framework or below the table's `coveredFrom` (`netcoreapp3.0`).
- Found while reading: ADR 0054's Why puts the `SetLastError` clearing at ".NET 6 and later". The API
  reference puts it at the Framework boundary. ADR 0058's Why records the correction; no verdict
  depends on it, since that boundary keeps decision 4 either way.
- Decision: the entries are a `marshalling` list beside `rows`, not rows. A row is a member prefix
  matched against a call; a declaration is not a call.
- Decision: `reach` errs wide. `netcoredeps` and `cet` describe the deployment and the native code,
  so they reach every import; `comObject` takes in every interface and `object`. A row that describes
  one operating system applies on all, since `equiv` does not know where the code runs.
- Decision: a reached pair stays EQ003 (`no-body`), not EQ006: an EQ006 carries an input on which a
  runtime-changed call decides the outcome, and an `extern` pair has none (ADR 0054 decision 3).
  `properties.notCongruent` names the entries as runtime rules `extern:<id>`; the uncovered case keeps
  the rule `extern`.
- Decision: an `InternalCall` keeps decision 4 on every crossing pair: its runtime's own table of
  internal calls is on no page.

### Criteria 2 to 4: the samples and tests
- Deviation: criterion 2 asks for a reached import on the `net5.0` to `net6.0` pair, but the only
  entry in (net5.0, net6.0] is `staticAbstractInterfaces`, which reaches nothing. The sample
  `extern-import-runtime` is that pair (both imports congruent, the edited one Unknown), and its
  `netcoreapp3.1/legacy` variant compiles the same files from .NET Core 3.1, which crosses .NET 5's
  `suffixProbing`: the import that is not spelled exactly stays EQ003 and the one spelled exactly is
  congruent. Criterion 2's text is corrected to say so.
- Criterion 3: `net48/legacy` compiles the same files for .NET Framework 4.8; all three imports are
  EQ003, `no-body`, the reached one and the one no entry reaches.
- Criterion 4: `Changed.F` imports `a.dll` on one side and `b.dll` on the other, EQ003 on all three
  pairs; `BodyFingerprinterTests.AnEditedImportAcrossRuntimesHasAnotherFingerprint` asserts the
  fingerprints differ between .NET 5 and .NET 6.
- The table test over the new rows: `RuntimeChangeTableTests.TheShippedMarshallingListIsTheInteropItems`
  (each entry's id, change point and reach), `MarshallingWithinIsTheEntriesTheIntervalCrosses` and
  `TheMarshallingListCoversOnlyDotNetIntervalsFromCoveredFrom`; in the frontend,
  `AnExternMethodAcrossRuntimesIsRuntimeSensitiveByTheEntriesThatReachIt` (every reach kind, the
  uncovered intervals and `InternalCall`) and `TheComObjectReachTakesInACOMObjectAnywhereInTheSignature`.
