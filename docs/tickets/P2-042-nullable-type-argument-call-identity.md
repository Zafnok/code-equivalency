# P2-042 A generic call's identity differs on a nullable-annotated type argument
Status: in-progress
Effort: S
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
Found while doing P2-032. `CallIdentityFactory.Constructed` (`src/Equiv.Frontend.CSharp/Lowering/CallIdentityFactory.cs`)
suffixes a generic call's identity with its type arguments' `ToDisplayString()`, whose default format includes the
nullable reference modifier. A legacy call `Enumerable.Empty<string>()` (no `#nullable enable`) and its modern form
`Enumerable.Empty<string?>()` therefore become two different uninterpreted functions, so a pair that differs only in
nullable annotations on a type argument cannot be proved Equivalent. P2-032 fixed the same leak in sort names
(`TypeMapper.MetadataName`) and did not touch call identities.

## Spec references
`src/Equiv.Frontend.CSharp/Lowering/CallIdentityFactory.cs` (`Constructed`); `TypeMapper.Unannotated` (P2-032) is
the display format that drops the modifier.

## Acceptance criteria (all must hold; nothing beyond them)
1. A generic call whose type arguments differ only in nullable reference annotations (including on an array
   element or nested type argument) gets the same call identity on both sides.
2. A legacy/modern pair `static IEnumerable<string> M() => Enumerable.Empty<string>();` against its
   `#nullable enable` form with `string?` verifies Equivalent end to end.

## Size guard
One display format change in `CallIdentityFactory`, plus tests.

## Out of scope
Sort names (done in P2-032); procedure identities (`RoslynIdentity` uses `FullyQualifiedFormat`, which already omits
the modifier).

## Notes
- Renumbered from P2-041 (filed by #246); P2-041 is the as-array ticket (#249), P2-043 the derivation-length one (#254).
- Decision: reuse `TypeMapper.Unannotated` (made `public` on the internal class) rather than a second copy of the
  format in `CallIdentityFactory`; it is `CSharpErrorMessageFormat` (the parameterless `ToDisplayString()` default)
  less the nullable modifier, so every other identity spelling is unchanged.
- Decision: the end-to-end test sits in `NullableArrayEquivalenceTests` next to P2-032's, reusing its lowering helper.
