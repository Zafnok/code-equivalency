# P2-146 Soundness: the marshalling settings an `extern` function takes from its types and its assembly are in no fingerprint
Status: todo
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
