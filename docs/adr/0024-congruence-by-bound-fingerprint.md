# ADR 0024: Identical bound code is Equivalent by congruence, and an unlowerable fragment present on both sides is shared

Status: accepted (2026-09-21); superseded in part by 0040 (runtime sensitivity applies only across the runtimes a pair crosses). Supersedes ADR 0014 in part: reaching an `IrOpaque` stops
making the outcome unknown when the same fragment is on both sides.

## Context
Under ADR 0014, an input that reaches an `IrOpaque` has an unknown outcome, and a whole-body opaque
makes the entire procedure Unknown. The coverage table (`docs/tickets/IOPERATION-COVERAGE.md`) still
makes lambdas, `await`, type patterns, floating point, `decimal`, interpolated strings, `lock`,
iterators and `ref` arguments opaque. So a method nobody touched is Unknown only because of what it
contains. In a 4.8-to-10 migration, and on almost every ordinary PR, most method bodies are
unchanged. The pre-M3 review predicted the first real run would be "almost all Unknown", and M3-010
and M4-001 only cover part of that. Opacity is caused by the construct, not by the change, so the
Unknown rate follows the size of the codebase instead of the size of the diff.

## Decision
The frontend computes a **bound fingerprint** for every body and for every opaque fragment. It is a
SHA-256 over a canonical serialisation of the Roslyn `IOperation` tree, which holds:
- operation kinds;
- every referenced symbol as its normalised identity, using the same normaliser, rename maps and
  applied `api-equivalences.json` rewrites as matching (ADR 0020);
- locals and lambda parameters numbered by first occurrence, and source parameters by position (ADR 0021);
- constants by type and value;
- the kind and normalised target type of every conversion and operator;
- the `checked` context.

Trivia, comments, formatting and local names cannot affect it. The fingerprint also records whether
the tree is **runtime-sensitive**, meaning it contains any of:
- a member listed in `runtime-changes.json`;
- a floating-point to integer conversion (made saturating in .NET 9);
- floating-point arithmetic when the legacy project runs on the 32-bit x87 JIT.

1. **Congruence.** A matched pair whose body fingerprints are equal, and neither of which is
   runtime-sensitive, is Equivalent without calling the solver. `proofMethod` is `congruence`, and
   the result lists `assumedCallees` exactly as ADR 0019 requires.
2. **Shared fragments.** An `IrOpaque` carries its fragment's fingerprint and the IR variables it
   reads. When a fragment's fingerprint occurs on both sides of a pair and is not runtime-sensitive,
   the encoder treats both occurrences as one opaque call. Its identity is `opaque:<fingerprint>`, its
   arguments are the variables it reads, and it takes the heap at that point and its trace position
   (ADR 0018). A fragment found on one side only, or runtime-sensitive, keeps ADR 0014's meaning.
   A fragment that writes a local other than its result, or that holds a lambda capturing a local
   assigned after the fragment, gets no fingerprint.

## Why
- The claim is the same one every `IrCall` already makes. Identical bound trees run the same
  operations in the same order. They can only behave differently through a callee that behaves
  differently between runtimes, and that is exactly the shared-callee assumption of ADR 0018 and
  ADR 0019, with the runtime-changes table as the listed exception. The runtime-sensitive flag
  carries that exception over to congruence.
- Fingerprinting the *bound* tree, not the source text, catches the drift that makes identical text
  mean different things. That covers a different overload chosen under .NET 10, an interpolated
  string bound to `DefaultInterpolatedStringHandler`, and a different conversion. Each of these
  changes the fingerprint and falls through to the solver.
- Congruence changes Unknown from depending on codebase size to depending on diff size, and it
  removes the solver from most pairs on a PR. That also keeps Z3 time inside a CI budget.
- A shared fragment is modelled as a call event, not as a pure function, because a fragment can
  contain calls. Keying it by position and heap keeps it sound when both sides execute it in a
  different order.
- A lambda that captures a variable written later sees a value that its reads at creation do not
  determine. Excluding it is the one case where a function of the reads would be unsound.

## Rejected
- **Text or syntax-tree diff.** Identical text can bind differently across frameworks, which is the
  whole migration risk.
- **Comparing compiled IL.** The legacy and modern compilers and reference assemblies differ, so IL
  differs even when behaviour does not, and IL loses the source spans that SARIF needs.
- **A fragment as a pure uninterpreted function with no trace event.** Unsound when the fragment
  contains calls whose order is observable.
- **Lowering every construct before shipping.** It is never finished, and the Unknown rate would
  still grow with codebase size in the meantime.

## Consequences
- The Unknown rate of an unchanged method becomes zero unless it is runtime-sensitive. The census
  (ADR 0027) reports the congruent share.
- A counterexample that involves a shared fragment cannot be replayed concretely. ADR 0026 decides
  what such a result becomes, and it must land before shared fragments.
- The fingerprint becomes part of what matching and api-equivalences must keep stable, and a
  normaliser change can invalidate many congruences at once. The sample snapshots pin this.
- VERIFICATION-MODEL sections 1, 2 and 5 gain the congruence rule and the fragment encoding in the PR that accepted this ADR, and ADR 0014 gains "superseded in part by 0024".
- Tickets: M3-015 (fingerprints and congruence), M4-004 (shared fragments).

## Clarifications
- 2026-10-01 (P2-067). **A delegate creation that runs no code is not a fragment.** Decision 2 makes
  a shared fragment a call event, and "a fragment as a pure uninterpreted function with no trace
  event" was rejected because it is "unsound when the fragment contains calls whose order is
  observable". Converting a lambda, a static method or a method of `this` to a delegate evaluates
  nothing: the lambda's calls run when a callee invokes the delegate, and that callee is its own call
  event with the heap at that point. The reason for the rejection does not apply, so such a conversion
  is lowered as ADR 0025's shared pure function, named `delegate:<fingerprint>` by this ADR's
  fingerprint of the conversion and applied to the fragment's reads. Everything else in decision 2
  holds as written: the cases with no fingerprint (a capture written later or by a function, a
  runtime-sensitive body, an outer local function, a struct's `this`) stay opaque and unshared, and
  a method group whose receiver is evaluated stays a shared fragment, because evaluating the
  receiver can run code and throws on null. A lambda's function also carries its site's position
  among the body's lambdas with that fingerprint, because two lambdas are two methods and their
  delegates are never equal. Measured on Git Extensions (the ticket's Notes): in 134 of the 213
  changed pairs whose only opaque reason was `DelegateCreation`, every such fragment was already
  shared, and 84 of those were Unknown(Abstraction) on the fragment's tainted `threw` edge.
- 2026-10-07 (P2-136). **A runtime-sensitive delegate creation that runs no code is not a fragment
  either.** Decision 2 keeps ADR 0014's meaning for a runtime-sensitive fragment because sharing it
  would claim that both runtimes run it alike. The clarification above left a runtime-sensitive
  lambda "opaque and unshared" on those grounds, but its own argument holds for it as well: the
  conversion evaluates nothing, so nothing of the lambda's body runs at that point on either
  runtime. What differs between the sides is the delegate's behaviour when a callee later invokes
  it, and ADR 0025 already has the vehicle for a value both sides compute alike in form but not in
  meaning: a side-specific function. So such a conversion is lowered as `delegate:<fingerprint>`,
  the fingerprint computed as for any other conversion, marked runtime-sensitive (see ADR 0025's
  clarification of the same date). It is still never shared: decision 2 is unchanged for every
  fragment that is a call event, a runtime-sensitive method group whose receiver is evaluated
  included. Measured on Git Extensions (the ticket's Notes): of the 49 changed pairs whose only
  opaque reason was `DelegateCreation`, a runtime-sensitive body was the only cause in 29.
- 2026-10-07 (P2-107). **On a same-runtime pair, a partial method is fingerprinted by its
  implementing part.** `docs/runs/2026-10-02-cleanup-verdict.md` counts 128 pairs of
  `powershell-19687` (net8.0 on both sides) as changed although the pull request edited neither
  side: 82 `unbound` and 46 `no-body`. Decision 1 already covers a body `equiv` cannot lower. A
  whole-body opaque is a fact about the IR, and the fingerprint is taken from the bound tree, so the
  same run's 306 `iterator` bodies are congruent. Only two whole-body reasons have no fingerprint to
  compare: `unbound`, which ADR 0029 decision 2 excludes, and `no-body`, where the declaration
  `equiv` reads holds no code. All 46 `no-body` pairs are `[LibraryImport]` partial methods. The
  frontend reads the defining declaration; the code is in the implementing declaration the interop
  generator adds to the same compilation: a body that marshals its arguments and calls a local
  `extern` function carrying `[DllImport]`.
  - **The rule.** When the pair's runtime interval is empty (ADR 0040 decision 2), a partial method
    whose defining declaration has no body has the fingerprint of its implementing part, which is
    found through the method's symbol in that side's own compilation. The text hashed is the usual
    serialisation of the implementing part's bound body, followed by the bound attributes of the
    method (the compiler gives it those of both parts), of its parameters and return value, and
    of every local function the body declares: each attribute's constructor, and its constructor
    and named arguments as constants. Decision 1 then applies as written, and the verdict is
    Equivalent with `proofMethod: congruence`. The lowered bodies do not change: each is still one
    `no-body` opaque, so `pairsWholeBodyOpaque` counts the pair as it counts an iterator, and
    `changedPairs` (ADR 0034) does not.
  - **What makes the two bodies the same function.** What a method does is fixed by the code the
    compiler binds for it, by what its callees do, and by the runtime that executes it. The
    fingerprint establishes the first for the implementing part exactly as it does for any body:
    the same operations in the same order, and every symbol resolved in that side's own
    compilation. The second is the shared-callee assumption every verdict already carries (ADR 0018,
    ADR 0019). The third is why the rule needs one runtime: a same-runtime pair crosses no rule
    (ADR 0040), and across runtimes it is the runtime that marshals an interop call, which no
    `runtime-changes.json` row describes. An `extern` function has no bound code, so its attributes
    are its code: they name the library and the entry point and say how each argument is marshalled.
    That is why they are in the text, and why a local `extern` function's are too. The native
    function behind them is a callee outside both solutions, as a BCL member is.
  - **Why P2-072's case is not a counterexample.** There an audit compared two files of one path
    across the two checkouts and found them equal, but the modern solution compiled a different,
    edited file into the type. Nothing in this rule is established from a file, a path or a text
    comparison between the checkouts. Each side's fingerprint is computed from the symbol the
    matched identity resolves to in that side's compilation, from that symbol's own declarations
    as that project's preprocessor symbols leave them, and from the symbols they bind there. On
    P2-072's pair the two bound trees differ (an empty `try` against one that calls, `throw e`
    against `throw`), so the fingerprints differ, as they do today. A file that is in the checkout
    and not in the solution is never read.
  - **What keeps the counting it has.** A pair that crosses a runtime, where source-generated code
    is P2-118's to decide. A partial method with no implementing part, one whose implementing part
    is itself `extern` and so has no body, and one whose implementing part does not bind by ADR
    0029 decision 2's test: none has a fingerprint. Every other
    `no-body` method, such as a record's primary constructor: the compiler writes its body from
    the type's other declarations, no single declaration holds it, and a fingerprint of part of
    them would be weaker evidence than a bound tree. And every `unbound` pair: a name that does
    not bind has no symbol to compare, and what it would have bound to depends on the rest of the
    solution, which is the shape of P2-072's mistake. P2-106 removes the `unbound` results that
    are only warnings promoted to errors, by making those bodies bind; this rule neither needs
    that nor changes what `unbound` means.
  - **A limit.** A congruent result lists the callees its lowered bodies call (decision 1, ADR
    0019). A body that is one opaque calls none in the IR, so a partial method's result lists no
    assumed callees, as an iterator's lists none. Lowering the implementing part would list them
    and is a lowering change, not part of this rule.
- 2026-10-08 (P2-145). **A local function's attributes are in every text that holds the function.**
  The clarification above says an `extern` function has no bound code, so its attributes are its
  code, "and why a local `extern` function's are too". It wrote them for a partial method's
  implementing part on a same-runtime pair only. An attribute is not an operation, so in every
  other text a local `extern` function was its signature alone: two bodies that differed only in
  its `[DllImport]` had one fingerprint and were Equivalent by decision 1 on any runtime pair, and
  two lambdas that differed only there were one `delegate:<fingerprint>` function to the solver.
  The decision's claim is that equal texts run the same operations; a text that leaves out which
  native function is called does not establish it. So the line of every local function, in a
  body, in an implementing part and in a fragment, is followed by the bound attributes of the
  function, of its return value and of its parameters, each as its constructor and its arguments
  as constants. An `extern` function has one more line ahead of them, holding its import as the
  compiler resolves it: the library, the entry point, the character set, the calling convention
  and the other settings of `[DllImport]`. Two of those are in no attribute argument. The entry
  point is the function's own name when the attribute names none (measured: Roslyn emits the
  import of a local function `F` with `[DllImport("a.dll")]` as `a.dll!F`), so renaming such a
  function changes what the body calls, and the text numbers local functions instead of naming
  them. The character set is the module's `[DefaultCharSet]` when the attribute names none. A
  body that declares no local function with an attribute has the text it had.
  - **What the text still does not hold.** What the marshaller reads from outside the function:
    the layout attributes of the types in its signature, `[BestFitMapping]` on a containing type,
    and the assembly's `[DisableRuntimeMarshalling]` and `[DefaultDllImportSearchPaths]`. No
    fingerprint holds a type's or an assembly's attributes today; P2-146 has them. And an `extern`
    local function with no `[DllImport]` is written by its containing type and its source name,
    although the name a runtime would look it up by is the one the compiler generates from the
    containing member.
- 2026-10-09 (P2-146). **What the marshaller reads from outside an imported function is in that
  function's text.** The two clarifications above say that an `extern` function's attributes are its
  code because they "say how each argument is marshalled". The runtime's marshaller reads more than
  the function's own attributes, and the clarification of 2026-10-08 listed what the text left out.
  Each was reproduced as two functions with one fingerprint (the ticket's tests): a pair that
  differed only there was Equivalent by decision 1, on a same-runtime pair as well. So the lines of
  a function that has a `[DllImport]`, a method's and a local function's alike, are followed by:
  - the assembly's `[DisableRuntimeMarshalling]`;
  - the assembly's `[DefaultDllImportSearchPaths]`, unless the function has its own;
  - the `[BestFitMapping]` of the assembly and of the type that declares the function, unless the
    import names both `BestFitMapping` and `ThrowOnUnmappableChar`. The runtime takes the type's
    where there is one and the assembly's otherwise; the text holds both, which costs congruence
    only on a pair that edits one the runtime does not read;
  - every type the signature reaches that is declared in the solution, once each: its kind, its
    base type or an enum's underlying type, its attributes, its instance fields in declaration
    order, each with its type, a fixed buffer's length and its attributes, and for a delegate its
    signature with the attributes of its return value and parameters. The signature reaches a type
    through an array, a pointer, a reference, a function pointer, a type argument, a field, a base
    type and a delegate's signature.
  - **Why a type's lines belong to the function and not to a rule about types.** The ticket asked.
    A type's layout and a field's `[MarshalAs]` are how an argument of that type is marshalled, which
    is what this ADR already calls the function's code; the function is the one text whose meaning
    they are known to change, and it is where the earlier clarifications put every other
    marshalling setting. A rule about types would say that every body that uses a type depends on
    the type's declaration. That is a wider claim about what a fingerprint covers, it is not needed
    to close this gap, and it has its own ticket (P2-149), because a type's layout can also change
    what an ordinary body does.
  - **Which texts change.** Only that of a function with a `[DllImport]`, and only where one of
    the settings exists: a function whose signature names no type of the solution, in an assembly
    with none of the three attributes, has the text it had. A body that declares no `extern`
    function has no such line, whatever its types and its assembly say, and neither has an
    `InternalCall`, which the marshaller never sees.
  - **What the text still does not hold.** A type from a reference is written by its name alone:
    what it holds is outside both solutions, as a callee there is (ADR 0018, ADR 0019). An attribute's
    arguments are written as the compiler binds them, so a `typeof` in one is a name that the rename
    map does not rewrite; that can cost congruence and cannot give it.
- 2026-10-08 (P2-144). **The throw a `switch` expression ends in is part of its bound code, and
  across .NET Core 3.0 it is a runtime rule.** A `switch` expression that matches no arm throws.
  The source does not write the throw and the `IOperation` tree does not hold it: the compiler
  adds it, calling the constructor of `System.Runtime.CompilerServices.SwitchExpressionException`
  where the reference assemblies have the type (.NET Core 3.0 and later) and of
  `System.InvalidOperationException` where they do not (.NET Framework). This is the drift the
  decision's Why names, identical text that means different things, and the same kind as the
  interpolated string bound to `DefaultInterpolatedStringHandler`, which the fingerprint already
  spells out. The exception type is an observable (VERIFICATION-MODEL section 1).
  - **The rule.** The serialisation of a `switch` expression whose arms do not cover every value
    names the constructor the compiler calls for an unmatched value, looked up in that side's own
    compilation as the compiler looks it up. `runtime-changes.json` has a row for
    `SwitchExpressionException`'s constructors with `changedIn: netcoreapp3.0` (ADR 0040 decision
    2), so a body that names one is runtime-sensitive on a pair that crosses that runtime, and
    its call in the lowered body is a runtime-changed callee. An input that matches no arm then
    reaches two throws of two types, and the pair is Divergent with rule EQ006, citing the row.
    ADR 0042 already keeps a runtime-changed callee out of every rebound pair, so the two
    constructors are no longer one. Suppressing the row (`suppressRuntimeChanges`) is how a user
    says the difference does not matter to them, as for any row.
  - **An expression that covers every value has no throw.** The compiler proves it (a discard or
    `var` arm with no `when` clause, `true` and `false`) and emits no code for the case, and
    Roslyn says so (`ISwitchExpressionOperation.IsExhaustive`). Its serialisation names no
    constructor, and its lowered body has no no-match block: Roslyn's control-flow graph still
    draws one, behind the failing edge of the last arm's test, and the lowering takes that test as
    always passing. Covering every named member of an enum is not covering every value, since a
    cast yields the others, so such an expression keeps its throw.
  - **Why not one exception type on both sides, as an assumption on the result.** P2-144 asked.
    `SwitchExpressionException` derives from `InvalidOperationException`, so only a handler or a
    test for the exact type sees the difference, and treating the two as one would keep the pair
    Equivalent. It would also be a claim about two different behaviours that no run could ever
    discharge, which ADR 0042 rejected for a rebound call ("the proof would rest on library code
    that nobody checked"), and it would narrow what section 1 observes. A runtime rule says what is
    true, is reported once per review group, and has the per-member suppression every row has.
  - **What both sides lacking the type means.** Two `netstandard2.0` projects hosted on different
    runtimes both call `InvalidOperationException`'s constructor. Their serialisations are equal
    and no row applies, so the pair stays congruent although its interval crosses the row.
  - **Measured on `gitextensions-8522`** (the ticket's Notes): 58 matched pairs hold a `switch`
    expression and every one of their expressions covers every value: in 55 pairs each through a
    discard or `var` arm, and 3 pairs hold one whose patterns leave no value out. So the 39
    congruent results P2-137 found naming the two constructors as a rebound call were right to be
    Equivalent: no input reaches a throw the compiler never emits. They stay congruent and no
    longer name the pair.
