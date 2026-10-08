# ADR 0054: An `extern` method is a procedure, and it is Equivalent only when both sides import the same thing on one runtime

Status: accepted (2026-10-08). Reverses ARCHITECTURE.md's frontend step 2 as M2-002 built it ("all with a
body": `extern` members were left out), and extends ADR 0024 decision 1 to a member that has no bound body.

## Context
`ProcedureEnumerator` leaves out every method whose `IsExtern` is true, and Roslyn reports a partial method
whose implementing part is `extern` as `extern` (ticket P2-145, gap 2). So a `[DllImport]` method whose
library, entry point or marshalling differs between the sides is no pair: it gets no Equivalent, no Divergent
and no Unknown. Its callers are congruent, because the callee's identity did not change, and ADR 0019's
answer, that a callee's change is caught by the callee's own pair, has no pair to point at. ADR 0024's
clarification of 2026-10-07 (P2-107) already holds that an `extern` function has no bound code, so its
attributes are its code, and that across runtimes it is the runtime that marshals an interop call, which no
`runtime-changes.json` row describes. Nothing applies either statement to an `extern` method.

## Decision
1. **Enumerated and matched.** An `extern` ordinary method, constructor, static constructor, property
   accessor, operator or conversion is a procedure, matched by identity as any member is. One present on one
   side only is Added (EQ004) or Removed (EQ005). A partial method whose implementing part is `extern` is
   one of them. Abstract members stay out: they name no implementation at all.
2. **Its fingerprint, on one runtime.** On a pair whose runtime interval is empty (ADR 0040 decision 2), an
   `extern` method that names its implementation has a bound fingerprint (ADR 0024). The text hashed is its
   signature line, as every text starts; one line holding its import as the compiler resolves it, which is
   the library, the entry point (the method's own name when `[DllImport]` names none), the character set
   (the module's `[DefaultCharSet]` when the attribute names none), the calling convention, and
   `ExactSpelling`, `SetLastError`, `BestFitMapping` and `ThrowOnUnmappableChar`; and one line per bound
   attribute of the method (a partial method has those of both parts), of its return value and of each
   parameter, as its constructor and its arguments as constants. A method names its implementation when
   it has `[DllImport]` or `MethodImplOptions.InternalCall`; one with `InternalCall` and no `[DllImport]`
   has, in place of the import, its declared type and name without the rename map, which is what its
   runtime looks it up by.
3. **What a matched pair gets.** Equal fingerprints are Equivalent by ADR 0024 decision 1, with
   `proofMethod: congruence`. Anything else is Unknown. Each side lowers, as it does today, to one
   whole-body opaque with the reason `no-body`, which carries no fragment fingerprint and is never shared
   (ADR 0024 decision 2), so the solver cannot prove the pair and the result is the Unknown every whole-body
   opaque gives, naming `no-body`. No new reason, rule id or SARIF property. An `extern` pair is never
   Divergent: `equiv` does not model the native function, so it has no input to show.
4. **A pair that crosses a runtime.** No fingerprint, so Unknown, `no-body`. And a body that declares an
   `extern` local function is runtime-sensitive on such a pair, so it is not congruent and no fragment
   that holds the function is shared.
5. **Which members have no fingerprint on one runtime either.** An `extern` method with neither
   `[DllImport]` nor `InternalCall`: nothing in either solution says what runs. They are Unknown, `no-body`.
6. **`--execute`.** An `extern` method is not called by a generated driver (ADR 0035): its signature is
   reported as one generated source cannot call, with the obstacle `extern`.

## Why
- ADR 0024's argument for decision 1 is that what a method does is fixed by the code the compiler binds for
  it, by what its callees do, and by the runtime that executes it. For an `extern` method the first is its
  import and its marshalling attributes: they select the native function and say how each argument crosses.
  Equal texts on one runtime leave only the second, and the native function is a callee outside both
  solutions, as a base class library member is (ADR 0018, ADR 0019). That is the claim every `IrCall` to a
  library member already makes.
- The import line holds the resolved import, not only the attribute's arguments, because two of its parts
  are in no argument. The entry point defaults to the method's name, and a rename map can match two methods
  of different names. The character set defaults to the module's.
- One runtime is required because the third term differs otherwise. The runtimes probe for a library name
  by different rules, .NET 6 and later clear the last error before a call marked `SetLastError`, and
  built-in COM marshalling is not the same code. No `runtime-changes.json` row describes the marshaller,
  and a row per import is not what that table is. An `extern` local function in a body is the same call,
  so decision 4 treats it alike; without it the rule would be escaped by moving the import inside a method.
- Unknown and not Divergent for unequal texts: `"a.dll"` and `"a"` can name one library, and a moved entry
  point may be the same function. A Divergent carries an input on which the two sides differ, and there
  is none to give. Unknown says what is true: the import changed and nothing here can compare the two.
- The callers need no change. Once the method is a pair, ADR 0019 lists it in each caller's
  `assumedCallees`, and in `unprovenAssumptions` when its own result is Unknown. That is the report the
  ticket's second repro lacks.
- Decision 6: a driver would pass made-up handles, pointers and lengths straight to native code. That tests
  the native library, not the two solutions, and can damage the machine it runs on. A caller of an `extern`
  method is driven as before.
- Measured for the import line, with a program that compiled the declarations and read the metadata: a
  method `G` with `[DllImport("a.dll")]` is emitted as the import `a.dll!G`, a partial method whose
  implementing part carries the attribute likewise, and Roslyn gives the defining part the attributes of both.

## Rejected
- **Keep `extern` methods out and compare the import at each call site.** A caller's fingerprint would
  then hold its callees' attributes, which is inlining a callee into its callers (rejected by ADR 0019),
  and an import nobody calls from source would still be unseen.
- **Divergent (EQ002) when the imports differ.** No counterexample exists, and two spellings of one library
  would be reported as a behaviour change.
- **Always Unknown.** Every unedited `[DllImport]` would be Unknown on every run, the dependence on codebase
  size that ADR 0024 exists to remove. P2-107 already makes an unedited `[LibraryImport]` method congruent.
- **Congruent across runtimes when the imports are equal.** It would claim that two runtimes marshal alike.
- **A fingerprint for an `extern` method with no attribute.** Equal signatures would be the whole evidence.
- **A new Unknown reason or rule id.** `no-body` is the fact, and it is the reason these members would get
  from the lowering today.

## Consequences
- Every `extern` method is a result. On a same-runtime pair an unedited one is one more EQ001 by
  congruence and counts in `pairsWholeBodyOpaque`; an edited one is an EQ003 that did not exist. On a pair
  that crosses a runtime every matched `extern` method is an EQ003, so the Unknown count of a migration
  rises by the number of its imports. P2-145 counts `powershell-19687` before and after.
- What the text does not hold, and so what an Equivalent `extern` pair still assumes: the marshalling
  settings a method takes from outside itself (the assembly's `[DisableRuntimeMarshalling]` and
  `[DefaultDllImportSearchPaths]`, a containing type's `[BestFitMapping]`, the layout of the types in its
  signature). Ticket P2-146.
- ARCHITECTURE.md's frontend step 2 and `ProcedureEnumerator` say that an `extern` member is a procedure.
  VERIFICATION-MODEL.md needs no change: the verdicts and the congruence rule are as written.
- ADR 0045's one-sided helper stays "not `extern`": an `extern` method has no body to put in its caller.
- Tickets: P2-145 implements this. P2-118 still owns generated interop code on a pair that crosses a
  runtime.
