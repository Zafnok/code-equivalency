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
