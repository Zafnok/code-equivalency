# P2-032 Verifying crashes on a nullable-annotation mismatch between an array type and its `?` form
Status: todo
Effort: S
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
M4-007's first real run found a second, distinct crash-message family on Git Extensions (34 occurrences, counting one
`T` against `T?` case), separate from P2-031's inheritance case: `domain sort |T[]| and parameter
|T[]?| do not match` and `Sorts |T[]| and |T[]?| are incompatible`, for `T` = `string`, `byte` and
`GitUI.Hotkey.HotkeySettings`. The two sides of a pair (or a parameter and its argument) disagree
only in nullable-reference-type annotation on an array type, not in the element type itself.
Minimal repro (legacy has no `#nullable enable`, modern does — the actual shape this migration
hits):

```csharp
// legacy
static string[] First(string[] a, string[] b) => a;
// modern (nullable enabled)
static string[]? First(string[]? a, string[]? b) => a;
```

## Spec references
Whichever module maps a `IArrayTypeSymbol` (or its nullable-annotated form) to a Z3 sort (search
for "Sorts" and "do not match"/"incompatible" error text in `src/Equiv.Verify.Z3`); likely the
same normalisation gap as the equivalent non-array case that presumably already works (a bare
`string` vs `string?` domain sort match must already succeed elsewhere, since this migration is
riddled with nullable annotations and most of it does not crash — find out why arrays are
different).

## Acceptance criteria (all must hold; nothing beyond them)
1. A nullable-annotated array type and its non-annotated form map to the same sort (nullability of
   the elements or of the array reference itself should already be handled by the existing
   shadow-based null tracking, not by the sort).
2. The minimal repro verifies instead of throwing.
3. Note in this ticket's Notes whether the bug is specific to arrays or is a narrower instance of
   a wider "nested generic/array nullable annotation" gap — if wider, this ticket's fix should
   still land, but file a follow-up if scope remains.

## Size guard
Array types only, one level of nesting (`T[]` vs `T[]?`, not `T[][]` or nullable-of-array-of-
nullable-element). If the fix requires touching the general sort-equality algorithm, that's fine
as long as it doesn't require a new ADR-worthy design.

## Out of scope
Jagged/multidimensional array nullability, nullable generic collection types (`List<T>?`) unless
the same one-line fix happens to cover them.

## Notes
