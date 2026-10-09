# P2-149 Soundness: a type's layout can change what an ordinary body does, and no fingerprint of such a body holds it
Status: done (PR #446)
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-146

## Goal
P2-146 put the declarations of the types in an imported function's signature into that function's
text, because they say how its arguments are marshalled. It left a type's declaration out of every
other text (ADR 0024, clarification of 2026-10-09, "Why a type's lines belong to the function").
A body with no `extern` function can also depend on how a type is laid out, and the bound tree of
such a body names the type and its members and nothing of their attributes. A pair that differs only
in one of the following has equal fingerprints and is Equivalent by congruence (ADR 0024 decision 1),
on a same-runtime pair as well:

- `[FieldOffset]` on a field of a `[StructLayout(LayoutKind.Explicit)]` type the body reads or
  writes. Two fields at one offset are one storage location; at different offsets they are two;
- `[StructLayout]` and its `Pack` and `Size`, and the order of a type's fields, where the body takes
  `sizeof` of the type, calls `Marshal.SizeOf` or `Unsafe.SizeOf`, or reads it through a pointer or a
  span of bytes;
- `[InlineArray(n)]`, whose length is the attribute's argument;
- `[UnmanagedFunctionPointer]` on a delegate type the body passes to
  `Marshal.GetFunctionPointerForDelegate`, and the assembly's `[DisableRuntimeMarshalling]` where
  the body calls through a `delegate* unmanaged`.

Found while working P2-146 by reading what `BoundSerialiser` writes for a field reference, a
`sizeof` and a function-pointer call; none was reproduced as a wrong result. The first step is the
repro.

## Spec references
ADR 0024 (`docs/adr/0024-congruence-by-bound-fingerprint.md`, decision 1 and the clarification of
2026-10-09), ADR 0019 (a verdict assumes its callees, and a member's change is caught by its own
pair; a type has no pair), `docs/tickets/done/P2-146-soundness-interop-settings-outside-the-function.md`,
`src/Equiv.Frontend.CSharp/Fingerprinting/BoundSerialiser.cs` (`Symbols`, `Member`, `Marshalled`,
`Declared`).

## Acceptance criteria (all must hold; nothing beyond them)
1. Each bullet of the Goal is a unit test on `BodyFingerprinter` that either shows two different
   fingerprints today, in which case the bullet is struck from this ticket with the test kept, or
   fails before the fix.
2. Before any change to `BoundSerialiser`: decide through `.claude/skills/equiv-adr`'s bar test what
   a body's text holds of a type it uses, and record which row applied in `## Notes`. The decision
   says, at least: which operations make a body depend on a type's layout (every use of the type,
   or the ones the Goal lists); whether the solver path needs the same fact, since a pair whose
   fingerprints differ is then lowered and the IR has no layout either; and what a type from a
   reference is taken to be.
3. The decision of criterion 2 is implemented. A body that does none of the operations the decision
   names keeps the text it has, whatever layout the types it uses have; a test asserts this.
4. The public-corpus results that change are counted in `## Notes`, by rule id before and after,
   from one run of `powershell-19687` in compare mode quick on each side of the change.

## Files
`src/Equiv.Frontend.CSharp/Fingerprinting/BoundSerialiser.cs`, its tests, and the ADR or
clarification criterion 2 produces.

## Tests
In `BodyFingerprinterTests`: one test per bullet of the Goal, and
`ABodyThatUsesNoLaidOutTypeKeepsItsText`.

## Size guard
One pull request after the decision. If criterion 2 decides that the lowering must change as well,
stop after the fingerprint and file the rest.

## Out of scope
- An imported function's text (P2-146).
- A type from a reference.
- Attributes that change no layout and no marshalling.

## Notes
- Filed 2026-10-09 from P2-146.

### Criterion 1: the repros
- All four bullets reproduced on `main` (`17e4cf66`): 16 test cases failed with equal fingerprints
  before the fix. None is struck. The tests, in `BodyFingerprinterTests`:
  `AFieldOffsetIsInTheTextOfABodyThatReadsOrWritesTheField` (4 cases),
  `ATypesLayoutIsInTheTextOfABodyThatTakesItsSizeOrReadsItAsBytes` (5 declarations, each under 14
  bodies), `AnInlineArraysLengthIsInTheTextOfABodyThatUsesIt` (3 cases) and
  `WhatAFunctionPointerTakesFromADelegateTypeAndFromTheAssemblyIsInTheBodysText` (4 cases). Each
  asserts the fingerprint on one runtime and across one, and that the snippet compiles.
- The second bullet reproduces with no attribute at all: a field added to a plain struct, or two of
  its fields swapped, left the fingerprint of `sizeof(S)` as it was.

### Criterion 2: the bar test
- The first row of `equiv-adr`'s table: a clarification. ADR 0024 decision 1 already claims that
  equal texts "run the same operations in the same order"; this applies it to operations whose
  meaning is in a type's declaration and not in the bound tree. Recorded as ADR 0024's
  clarification of 2026-10-09 (P2-149), written before `BoundSerialiser` changed.
- Which operations: not every use of a type. The ones that read memory: a reference to a field
  that has a `[FieldOffset]`, `sizeof`, an operation whose type is a pointer, a function pointer or
  an inline array, and a call into `System.Runtime.InteropServices` or
  `System.Runtime.CompilerServices` or one that takes a pointer. A call through a function pointer
  and a call into those namespaces also take the assembly's `[DisableRuntimeMarshalling]`.
- The solver path: it needs the same fact and does not have it. Where the lowering makes the
  operation an opaque, the fragment's fingerprint now carries the declaration
  (`FragmentFingerprinterTests.TheDeclarationUnderASizeOfIsInItsFragmentsFingerprint`). Where it
  lowers the operation (a field of an explicit layout, a call such as `Marshal.SizeOf<S>()`) it is
  not covered. By the size guard this stops at the fingerprint; the rest is P2-150.
- A type from a reference: its name alone, as in P2-146.
- Deviation: criterion 3 said "a body that uses no type with a layout attribute keeps the text it
  has". The second bullet of the Goal needs no attribute (the order of a plain struct's fields
  under `sizeof`), so the two cannot both hold. The unit of the decision is the operation, not the
  attribute; criterion 3 now says so.
- Decision: which callees are handed a type to read -> every member declared under the two namespaces. Alternatives: a list of members (`Marshal.SizeOf`, `Unsafe.SizeOf`, ...), every generic call into a reference. Rule: 4, and P2-146's reason: a list kept by hand goes stale, and a missing member is a false Equivalent.
- Decision: where the lines go -> after the operation's line, under the name `layout`, each type once per text, written from `Visit`, so a body, an implementing part and a fragment all have them. Alternatives: a trailer after the body, written in three places. Rule: 4.
- Decision: what a type is written as -> what P2-146 writes (`Marshalled`, `Declared`): all its attributes, its fields by position and type. Alternatives: only the layout attributes. Rule: 4.
- Decision: the test for the fourth bullet -> one theory that asserts the delegate's attribute and the assembly's in the same four bodies. Alternatives: two tests. Rule: 5.
- Changed in passing: `ABodyWithoutAnExternFunctionKeepsItsText` (P2-146) used a type with a
  `[FieldOffset]` whose field the body reads. That body now has the type's lines, as this ticket
  decides, so the test's type has a sequential layout instead; what it asserts is unchanged.

### Criterion 4: the corpus
- `powershell-19687` (net8.0 on both sides), compare mode quick, `--jobs 4`, one run per column on
  2026-10-09, exit 1 both. Before is `main` at `17e4cf66`; after is this branch at `1cd20eab`.
  193 s and 187 s of wall-clock time.

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
  | `changedPairs` | 37 | 37 |

  Compared result by result, by location: none changed its rule id, its `proofMethod` or its
  `unknownReason`, none left and none is new. The pull request edits no type's declaration and no
  assembly attribute, so both sides gained the same lines.
- Not measured: how many of the pair's bodies have a new line. The SARIF does not hold a text.
- The scoreboard is not touched: no file under `docs/runs/` is added here.

### What is left
- P2-150: the lowering (above).
- P2-151: a type handed as a type argument to a generic member of the solution that takes its
  size. The callee's text is the same for every argument and the caller's names the argument only.
  Read off the rule, not reproduced.
