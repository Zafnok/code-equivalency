# P2-108 `System.IntPtr` and `nint` are one type in an identity, on every target
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
A byte-identical method with a `System.IntPtr` parameter does not match between a legacy net48
project and a modern net10.0 project. `equiv compare` reports EQ005 (removed) for
`Equiv.Samples.TryRethrow.Hooks::Install(System.Func<global::System.IntPtr>,System.IntPtr)` and
EQ004 (added) for `Equiv.Samples.TryRethrow.Hooks::Install(System.Func<nint>,nint)`. From .NET 7
and C# 11 Roslyn treats `System.IntPtr` and `nint` as the same type and displays it as `nint`;
on .NET Framework it stays `System.IntPtr`. `RoslynIdentity.ParameterTypeName` takes the display
string as it comes, so one type has two spellings and the pair is never compared. The same is
expected of `System.UIntPtr` and `nuint`. Git Extensions did not show it because its modern side
is net5.0. Give the two types one spelling each, on every target and at every depth of a
parameter type, so the pair matches.

Repro, on both sides of a pair shaped like `samples/identical` (old-style csproj on v4.8, SDK-style
net10.0):

```csharp
public static class Hooks
{
    public static int Install(System.Func<System.IntPtr> get, System.IntPtr h)
    {
        int r = 0;
        try { r = Hook(get(), h); }
        catch (System.Exception e) { throw e; }
        return r + 1;
    }

    public static int Hook(System.IntPtr a, System.IntPtr b) { return a == b ? 1 : 0; }
}
```

## Spec references
ARCHITECTURE.md frontend step 2 (procedure identity); `src/Equiv.Frontend.CSharp/RoslynIdentity.cs`;
`src/Equiv.Frontend.CSharp/Lowering/CallIdentityFactory.cs` (a call identity is built from the
same string, plus type arguments spelled by `TypeMapper.Unannotated`); P2-072 Notes (where this
was found); P2-042 (the last identity-spelling fix, for nullable type arguments).

## Acceptance criteria (all must hold; nothing beyond them)
1. `System.IntPtr` and `nint` give the same identity text, and so do `System.UIntPtr` and `nuint`,
   whichever the source writes and whichever framework and language version the project targets.
   Which spelling is kept is an `equiv-decide` call, logged in Notes.
2. That holds wherever the type sits in a parameter type: bare, behind `ref` or `out`, as an array
   element, as a pointer target, as a tuple element, and as a generic type argument at any depth.
3. It holds for a call identity too: the callee's parameters and the type arguments
   `CallIdentityFactory` appends.
4. A new sample `samples/native-int-identity` holds the repro above (v4.8 against net10.0). On it
   `equiv compare` reports no EQ004 and no EQ005, and `Install` and `Hook` are each one matched
   pair.
5. The verdict each of the two pairs then gets is recorded in the sample's README and
   `expected.sarif.json` as it is, Equivalent or not. A verdict that is not Equivalent does not
   fail this ticket: Notes name its cause and the follow-up ticket filed for it (see Out of scope).
6. No identity that holds neither type changes: every existing snapshot and `expected.sarif.json`
   is byte-identical after the change.

## Tests
- `RoslynIdentityTests.IntPtrAndNintSpellTheSameOnEveryTarget` (theory: `IntPtr`, `nint`,
  `UIntPtr`, `nuint`; a compilation where they are the same type and one where they are not)
- `RoslynIdentityTests.NativeIntegerSpellingHoldsAtEveryDepth` (the positions of criterion 2)
- `CallIdentityFactoryTests.NativeIntegerTypeArgumentSpellsTheSameOnEveryTarget`
- `NativeIntIdentitySampleTests.IdenticalMethodsMatchAcrossRuntimes` (integration, the sample)
- `NativeIntIdentitySampleTests.SampleVerdictsSnapshot` (Verify)

## Out of scope
The bodies. Once the pair matches, `a == b` on `IntPtr` binds to the user-defined
`IntPtr.op_Equality` on net48 and to a built-in numeric operator on net7 and later, so `Hook` may
well not be Equivalent, and `Install` with it. The same goes for `IntPtr` arithmetic, conversions
and `IntPtr.Zero`. Making those bodies equal is a follow-up ticket, filed from criterion 5 if the
verdict calls for one. The rethrowing catch in `Install` (P2-072 found no bug there).

The `global::` that stays inside a generic type argument (`System.Func<global::System.Uri>`). It is
spelled the same on both sides for every other type, so it breaks no match, and removing it changes
every such identity and its snapshots. It only has to stop making the two native integer types
differ.

The sort the two types lower to. The replay driver's handling of them.

## Notes
