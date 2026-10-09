# P2-146 Soundness: the marshalling settings an `extern` function takes from its types and its assembly are in no fingerprint
Status: in-progress
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-145

## Goal
P2-145 put what an `extern` function imports, and its attributes, into the text ADR 0024
fingerprints. The runtime's marshaller also reads settings that are on none of the function, its
return value and its parameters. A pair that differs only in one of them has equal fingerprints
and is Equivalent by congruence (ADR 0024 decision 1), on a same-runtime pair as well:

- the assembly's `[DisableRuntimeMarshalling]`, which turns off marshalling for every
  `[DllImport]` in it (a pull request that moves to `[LibraryImport]` adds it);
- the assembly's `[DefaultDllImportSearchPaths]`, which says where the library is looked for
  when the function has none of its own;
- `[BestFitMapping]` on a containing type or on the assembly, which an import with no
  `BestFitMapping` of its own takes;
- the attributes of the types in the signature: `[StructLayout]` and its `Pack`, `Size` and
  `CharSet`, a field's `[MarshalAs]` and `[FieldOffset]`, and `[UnmanagedFunctionPointer]` on a
  delegate type.

Found while working P2-145 by reading what `DllImportData` and the attribute lines hold; none was
reproduced as a wrong result. The first step is the repro.

## Spec references
ADR 0024 (`docs/adr/0024-congruence-by-bound-fingerprint.md`, the 2026-10-07 and 2026-10-08
clarifications), `docs/tickets/done/P2-145-soundness-extern-attributes-are-not-compared.md`,
`src/Equiv.Frontend.CSharp/Fingerprinting/BoundSerialiser.cs` (`Attributes`, `Import`).

## Acceptance criteria (all must hold; nothing beyond them)
1. Each bullet of the Goal is a unit test on `BodyFingerprinter` that either shows two different
   fingerprints today, in which case the bullet is struck from this ticket with the test kept, or
   fails before the fix.
2. For each bullet that failed: the setting is in the text of an `extern` function that takes it,
   and in no other text. A body with no `extern` function, and an `extern` method that names the
   setting itself, keep the text they have; a test asserts both.
3. A field's attributes and a type's layout are a type's meaning, which no fingerprint holds for
   any body. Decide through `.claude/skills/equiv-adr`'s bar test whether they belong to the
   `extern` function's text or to a rule about types, and record which row applied in `## Notes`.
4. The public-corpus results that change are counted in `## Notes`, by rule id before and after,
   from one run of `powershell-19687` in compare mode quick on each side of the change.

## Files
`src/Equiv.Frontend.CSharp/Fingerprinting/BoundSerialiser.cs`, its tests, and the ADR
clarification criterion 3 produces.

## Tests
In `BodyFingerprinterTests`: one test per bullet of the Goal, and
`ABodyWithoutAnExternFunctionKeepsItsText`.

## Size guard
One pull request. If criterion 3 decides a rule about types, stop after criteria 1 and 2 for the
first three bullets and file the rest.

## Out of scope
- Modelling what a native function does.
- The attributes of an ordinary method, of a lambda and of a type where no `extern` function is
  involved.
- Code a source generator emits on a pair that crosses a runtime (P2-118).

## Notes
- Filed 2026-10-08 from P2-145, which names these as what its text does not hold.

### Criterion 1: the repros
- All four bullets reproduced on `main` (`c8bf2024`): 35 test cases failed with equal fingerprints
  before the fix. None is struck. The tests, in `BodyFingerprinterTests`:
  `AnAssemblysDisabledRuntimeMarshallingIsInAnExternFunctionsText`,
  `AnAssemblysImportSearchPathsAreInTheTextOfAnExternFunctionThatNamesNone`,
  `ABestFitMappingOnTheTypeOrTheAssemblyIsInTheTextOfAnImportThatLeavesItOpen` (3 cases),
  `TheLayoutOfATypeInAnExternFunctionsSignatureIsInItsText` (29 cases) and
  `TheTypesInAnExternFunctionsSignatureAreWrittenOnceEachWithTheirFields` (a snapshot of the lines).
  Each is asserted for an `extern` method on one runtime and for a body that declares a local
  `extern` function on one runtime and across one.
- Found by the fourth bullet's cases and fixed by the same lines: a `struct` that becomes a `class`
  of the same name, a field added, removed or moved, an enum's underlying type, and a delegate's own
  signature all left an `extern` function's fingerprint as it was. The signature line names a type
  and nothing of its declaration.

### Criterion 2: where the settings are written
- Decision: the lines follow the function's own attribute lines, in `BoundSerialiser.Attributes`,
  for a function whose `GetDllImportData` is not null. One place covers an `extern` method
  (ADR 0054), a local function in a body, in an implementing part and in a fragment. An
  `InternalCall` has no such line: the marshaller never sees it.
- Decision: a setting is a line only where it exists. An import in an assembly with none of the
  three attributes, whose signature names no type of the solution, has the text it had, so
  P2-145's exact-text assertions stand unchanged.
- Decision: `[BestFitMapping]` is taken from the assembly and from the type that declares the
  function, both written whenever the import leaves `BestFitMapping` or `ThrowOnUnmappableChar`
  open. The runtime reads the type's and falls back to the assembly's. Writing both is a superset
  and costs congruence only on a pair that edits the one not read. Rejected: every enclosing type,
  which the runtime does not read.
- Decision: a type is written when it has a declaration in the solution
  (`DeclaringSyntaxReferences`). A type from a reference has no lines. Roslyn does not give a
  metadata type's `[StructLayout]` as an attribute, and what such a type holds is outside both
  solutions, as a callee there is.
- Decision: a type's attributes are all written, not only the layout ones. A list of the attributes
  the marshaller reads would have to be kept complete by hand (`[NativeMarshalling]`, `[ComImport]`,
  `[Guid]`, `[InterfaceType]`, `[InlineArray]`), and a missing one is a false Equivalent. The cost is
  an Unknown on an imported function when a pair edits an unrelated attribute of a type it takes.
- Decision: fields are written by position and type, without their names. A name changes no
  layout, and a renamed field then keeps the function congruent.
- `ABodyWithoutAnExternFunctionKeepsItsText` asserts criterion 2's second sentence for a body that
  uses a laid-out type and for an `InternalCall`. The two tests for the search paths and for
  `[BestFitMapping]` assert that an import which names the setting itself has the text it has
  without the assembly's or the type's.

### Criterion 3: the bar test
- The first row of `equiv-adr`'s table: a clarification. ADR 0024's clarification of 2026-10-07
  already says an `extern` function's attributes are its code because they "say how each argument
  is marshalled", and a type's layout and a field's `[MarshalAs]` are how an argument of that type
  is marshalled. So they belong to the `extern` function's text. Recorded as ADR 0024's
  clarification of 2026-10-09. The size guard's stop did not apply.
- Not decided here, and filed as P2-149: a rule about types. A type's layout can change what an
  ordinary body does (two fields at one `[FieldOffset]`, `sizeof`, `[InlineArray]`), and no text of
  such a body holds it. That is the Out of scope line "a type where no `extern` function is
  involved". Read off what the text holds, not reproduced.

### Criterion 4: the corpus
- `powershell-19687` (net8.0 on both sides), compare mode quick, `--jobs 4`, one run per column on
  2026-10-09, exit 1 both. Before is `main` at `c8bf2024`; after is this branch at `b51fb74f`.
  176 s and 193 s of wall-clock time.

  | | before | after |
  |---|---|---|
  | results | 34183 | 34183 |
  | EQ001 | 34149 | 34149 |
  | EQ002 | 6 | 6 |
  | EQ003 | 28 | 28 |
  | EQ004 / EQ005 / EQ006 | 0 / 0 / 0 | 0 / 0 / 0 |
  | `matchedPairs` | 34183 | 34183 |
  | `pairsCongruent` | 34146 | 34146 |
  | `pairsWholeBodyOpaque` | 663 | 663 |
  | `no-body` bodies, each side | 340 | 340 |
  | `changedPairs` | 37 | 37 |

  Compared result by result, by identity: none changed its rule id, its `proofMethod` or its
  `unknownReason`, none left and none is new. The pull request edits no imported function, no type
  one takes and no assembly attribute, so both sides gained the same lines.
- Not measured: how many of the pair's imported functions have a new line. The SARIF does not hold
  a text.
- A first before run exited 4 after 14 s and is void: the checkouts were fetched fresh into this
  worktree and the three steps `tools/corpus/README.md` lists for this pair had not been done.
- The scoreboard is not touched: no file under `docs/runs/` is added here. The README's two links
  to this ticket follow it into `done/`.
