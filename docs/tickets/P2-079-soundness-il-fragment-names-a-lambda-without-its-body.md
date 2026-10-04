# P2-079 Soundness: the IL lowering shares an opaque that names a lambda without its body, so two different lambdas prove Equivalent
Status: in-progress
Effort: M
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: P1-016, P1-017

## Goal
`--il-fallback` reports a false Equivalent. `IlFragment.Of` fingerprints an opaque ILAst instruction
by its text, with each member spelled as its declaring type and documentation ID. A lambda, a local
function and a closure class are compiler-generated members whose names are ordinals
(`<set_X>b__5_0`), and their bodies are elsewhere. So two sides whose lambdas differ get the same
fingerprint, ADR 0024 shares the fragment as one call, and the pair proves. The IOperation
fingerprint (`FragmentFingerprinter`) serialises each lambda's bound body and refuses a fragment
that refers to a local function declared outside it, or that is runtime-sensitive (M3-015's rule).
The IL fingerprint does neither: its runtime check covers only the calls in the fragment itself,
never the lambda's body. `IlLowerer.Function` has the same gap for a named method group: it lowers
`ldftn` to a constant from the method's call identity and ignores `RuntimeChanged`.

Repro (written for this ticket, two .NET Framework 4.8 projects that differ only in the lambda):

```csharp
public sealed class Holder
{
    private int _x;
    public Lazy<int> Value { get; private set; }
    public int X
    {
        get => _x;
        set { _x = value; Value = new Lazy<int>(() => _x + 1); }   // modern: _x + 2
    }
    public void Subscribe(Action<Func<int>> register)
    {
        register(() => _x + 1);                                    // modern: _x + 2
    }
}
```

Without `--il-fallback`, `set_X` and `Subscribe` are Unknown(opaque: `DelegateCreation`). With it, both
are `EQ001`, `proofMethod: bounded`, `lowering: il`. When the modern side targets net10.0 the same
pair is Unknown(abstraction), but only because the delegate constructor's identity is spelled
`(object,nint)` there and `(object,System.IntPtr)` on .NET Framework. Both sides still get one
fingerprint. That is an accident, not a guard.

P1-018's hand check (`docs/runs/2026-10-01-il-fallback-verdicts.md`) found that 20 of the 21 pairs
the fallback moved to Equivalent on Git Extensions rest on this. 19 hold a lambda or local function
the proof never read, and in two of those the code inside it differs between the sides. One passes
`System.Char::IsLetter`, a `runtime-changes.json` row, as a method group. No behavioural difference
was found in any of the 21 by reading the source, so the verdicts are unproved, not shown wrong.

## Spec references
ADR 0039 (the IL lowering follows the IOperation lowering's refusals; "lambdas and local functions
stay opaque"), ADR 0024 decision 2 (shared opaque fragments), M3-015 (runtime-sensitive fragments),
ADR 0019 (a verdict lists the callees it assumed), `src/Equiv.Frontend.CSharp/Lowering/Il/IlFragment.cs`,
`src/Equiv.Frontend.CSharp/Lowering/Il/IlLowerer.Heap.cs` (`Function`), `src/Equiv.Frontend.CSharp/Lowering/Il/IlKeys.cs`
(`IsCompilerGenerated`), `src/Equiv.Frontend.CSharp/Fingerprinting/FragmentFingerprinter.cs`,
`docs/tickets/IL-COVERAGE.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. The repro above is a test in `tests/Equiv.Frontend.CSharp.Tests` (the two fragments get no shared
   fingerprint) and a sample or integration test (`compare --il-fallback` on the same-runtime pair
   does not report `EQ001` for `set_X` or `Subscribe`). Both fail before the fix.
2. `IlFragment.Of` returns no fingerprint for a fragment that references a compiler-generated method
   or type (`LdFtn[lambda]`, `NewObj[closure class]`, a call to a local function or another generated
   method, and a state-machine type). Decide with `equiv-decide` whether to refuse outright or to
   hash the referenced bodies as well. If bodies are hashed, the hash covers everything reachable
   through generated members (an `async` lambda's state machine, a local function called from a
   lambda), and M3-015's rule applies to every call in them. Log the choice as a `Decision:` line.
3. `ldftn` and `ldvirtftn` of a member that `runtime-changes.json` names are not lowered to a plain
   constant. They are treated as the IOperation lowering treats a runtime-sensitive delegate
   creation. A test passes `char.IsLetter` as a method group.
4. `IL-COVERAGE.md`'s rows for `LdFtn`, `LdFtn[lambda]`, `LdVirtFtn`, `NewObj[closure class]` and the
   generated-call keys say what criteria 2 and 3 decided.
5. M0-012's IL mode (P1-017) generates pairs that differ only inside a lambda and only inside a local
   function, and the gate fails on the code before the fix. If its generator cannot produce them,
   `## Notes` says why and names the test that covers the case instead.
6. A `--il-fallback` `full` run of `gitextensions-8522` is compared with P1-018's. For each of the 20
   pairs listed in that report's hand check, `docs/runs/<date>-il-fragment-soundness.md` gives the
   new verdict. Any that is still Equivalent from IL is explained there, with the member it shares.

## Files
`src/Equiv.Frontend.CSharp/Lowering/Il/IlFragment.cs`, `IlLowerer.Heap.cs`, `IlKeys.cs` (only if a key
changes), their tests, a sample or integration test for criterion 1, the differential gate's generator,
`docs/tickets/IL-COVERAGE.md`, `docs/runs/<date>-il-fragment-soundness.md`.

## Tests
`IlFragmentTests.AFragmentThatNamesALambdaHasNoFingerprint`, `IlFragmentTests.AFragmentThatNamesALocalFunctionHasNoFingerprint`,
`IlLowererTests.ARuntimeChangedMethodGroupIsNotAConstant`, `IlFallbackSampleTests.ALambdaWhoseBodyDiffersIsNotEquivalent`,
and the gate operator's test for criterion 5.

## Size guard
Lowering lambda bodies so that such pairs can be proved is P2-067. Here they only stop being proved
wrongly. If the diff grows past the files above, stop.

## Out of scope
Turning `--il-fallback` on by default. Lowering lambdas (P2-067). The crash in P2-078.

## Notes
- Found 2026-10-01 by hand-checking P1-018's 21 new Equivalents at the user's request, then confirmed
  with the repro above at equiv 1d4569a.
- Until this lands, a verdict from a run with `--il-fallback` is not to be relied on where the method
  holds a lambda, a local function or a method group. The option is off by default.
- P1-017's IL gate did not catch this. Its generated pairs hold no lambda.
- No matched pair exists for a lambda or a local function: no result in the Git Extensions SARIF names
  one. Their bodies are only ever verified as part of the method that holds them.
- Decision: criterion 2, refuse or hash the referenced bodies -> refuse outright: a fragment whose text names a
  compiler-generated method or type gets no fingerprint. Alternatives: hash every body reachable through generated
  members and apply M3-015's rule to each call in them. Rule: 4 (the smaller change; reading the bodies is P2-067's
  lowering of them, and a refusal cannot prove a pair wrongly).
- Decision: how a fragment is found to name generated code -> the fingerprint's own writer flags it: every type it
  writes whose reflection name holds a `<` (itself, a type it is nested in, a type argument), and every method whose
  own name starts with one. Alternatives: scan the instruction tree for each kind of operand that carries a member or
  a type. Rule: 1 (the text is what is hashed, so what the text names is what must not be an ordinal). A field's own
  name is not looked at: a property's backing field `<P>k__BackingField` is named from the property.
- The refusal covers more than the ticket lists, each for the same reason: a cached delegate's class (`<>c`, and
  `<>O` for a static method group, C# 11), an anonymous type, and `<PrivateImplementationDetails>`. All are names
  the compiler numbers.
- Before the fix a `NewObj[closure class]`, a `Call[local function]` and a `Call[compiler-generated method]` already
  had no fingerprint, by accident: `IlSymbols` resolves no generated method in the source compilation, and the
  runtime check treated that as a call that does not resolve. Only `ldftn`, which that check did not look at, was
  shared. The rule is now explicit and comes first, so it does not depend on what resolves.
- Decision: criterion 3's refusal -> `Lowerable` is false for `ldftn` and `ldvirtftn` of a runtime-changed method, so
  it is an opaque with its key and no fingerprint, its receiver lowered first. Alternatives: a constant that carries
  the side, a `RuntimeSensitive` pure function. Rule: 1 (the IOperation lowering's `Delegate` leaves a
  runtime-sensitive creation an unshared opaque).
- Deviation: criterion 1's repro, as written, is no longer read from IL. Since P2-067 the IOperation lowering makes
  a lambda the pure function `delegate:<fingerprint>` of its bound body, so `set_X` and `Subscribe` hold no opaque,
  the fallback is not tried, and both are already Unknown(abstraction) with `lowering: operation`.
  `IlFallbackSampleTests.ALambdaWhoseBodyDiffersIsNotEquivalent` therefore adds a lifted `int?` operator to each
  member, spelled out on the legacy side as `samples/il-fallback` does. That is an opaque only the modern IOperation
  body has, so the pair is read from IL. On the code before the fix both members are `EQ001`, `proofMethod: bounded`,
  `lowering: il`; now both are `EQ003`. `IlFragmentTests` uses the ticket's repro unchanged.
- Decision: criterion 1's end-to-end test -> an integration test that writes the two projects to a temporary
  directory from `samples/il-fallback/legacy`'s project file. Alternatives: a new `samples/` directory. Rule: 4 (a
  sample needs an `expected.sarif.json`, a README and a place in the parity job, and both sides here are .NET
  Framework projects, which no sample's modern side is).
- Decision: criterion 5 -> `PairGen.ClosurePair`, drawn by its own fact
  `DifferentialSoundnessTests.PairsThatDifferOnlyInsideAClosureAreSoundUnderTheIlLowering` at a quarter of the
  budget (50 pairs per pull request, 1,250 nightly). Alternatives: a third arm of `Generated`'s `Frequency`.
  Rule: 4 (`BrokenIlSeed` and P2-080 depend on the first pair `Generated` draws from a seed). A closure pair is not
  made by `SyntaxMutator`: one method is rendered twice with the closure's literal `K` and `K + 1`, under an
  operator (`+`, `-`, `^`) that makes the two closures differ on every argument, and the result is written to `F`.
  The closure is a lambda called at once, a local function called by name, or a local function's method group.
  `PairGenTests.ClosurePairsDifferOnlyInsideALambdaOrALocalFunction` is the generator's test.
- Criterion 5, the gate before the fix: with `src/` at `bb69554` the new fact fails rule 1 under the il lowering
  (CsCheck seed `0000Sk4jyHq2`, 50 pairs). With the fix it passes at 50 pairs and at the nightly 1,250 (74 s).
- Finding, filed as P2-125 (not an IL bug, and outside this ticket's files): the same pairs fail rule 1 under the
  IOperation lowering. `IrLowerer` lowers a call of a local function as an `IrCall` of `<Type>::<Name>(...)`, the
  same on both sides, and no pair verifies a local function. `compare` with no flag reports `EQ001` for a member
  whose two sides differ only in the local function it calls. The new fact therefore runs under the IL lowering
  only, which is what criterion 5 asks; P2-125's criterion 3 adds the IOperation lowering. README's sentence on
  what an Equivalent can be relied on for now names that exception.
- Deviation: the Files list has no `IlLowerer.cs` (criterion 3's check is in `Typed`, beside `Heap.cs`'s `Function`),
  `PairSyntax.cs` (the generator's renderer needs a local function), `PairGenLoweringTests.cs`, `PairGenTests.cs`,
  README, ROADMAP or the P2-125 ticket. `IlKeys.cs` is unchanged: no key changed.
