# ADR 0055: A `[GeneratedRegex]` member is compared by its declaration, and the matcher the generator emits for it is no procedure

Status: accepted (2026-10-10). Narrows ARCHITECTURE.md's frontend step 2 ("every method with a body"), and
extends ADR 0024 decision 1 to a second declaration that has no bound code of its own.

## Context
`equiv` enumerates every method of a compilation, and a compilation holds what its source generators add.
The regex generator writes, for each `[GeneratedRegex]` member, an implementing part that returns a
singleton, and in `System.Text.RegularExpressions.Generated` a `Regex` subclass with the matcher and a
`Utilities` class whose helpers are named by a hash of their text. What it writes depends on the
generator's version, which is the target framework's. On `jellyfin-13023` (net8.0 to net9.0, no pattern
edited; ticket P2-118, found by P2-066) that namespace holds 137 results, of which 17 are not Equivalent:
4 EQ002, 8 Unknown, 4 `unmatched-overload` and 1 Removed. Nobody wrote that code and nobody can review it.
The 30 `[GeneratedRegex]` methods themselves are Unknown, `no-body`: ADR 0024's clarification of
2026-10-07 fingerprints a partial method by its implementing part on a same-runtime pair only, and left
generated code on a pair that crosses a runtime to this decision.

## Decision
1. **The declaration is the code.** A partial method, or the `get` accessor of a partial property, that
   carries `[GeneratedRegex]` and whose implementing part the regex generator wrote has a bound fingerprint
   (ADR 0024) on every runtime pair. The text hashed is its signature line and one line for the
   `[GeneratedRegex]` attribute as the compiler binds it: its constructor, and its arguments as constants
   (the pattern, the options, the match timeout, the culture name). The implementing part is not read.
   The generator wrote it when the member carries the generator's own
   `[GeneratedCode("System.Text.RegularExpressions.Generator", ...)]`, which the compiler gives the member
   from that part; a member without it keeps the rules it had.
2. **What a matched pair gets.** Equal fingerprints are Equivalent by ADR 0024 decision 1, with
   `proofMethod: congruence`. Anything else is Unknown: each side still lowers to one whole-body opaque
   with the reason `no-body`, which is never shared, so the result names `no-body` on the declaration
   somebody wrote. Such a pair is never Divergent: `equiv` has no input to show for two patterns.
3. **Runtime sensitivity.** The declaration constructs a `Regex`, so its text is runtime-sensitive when a
   `runtime-changes.json` row for `System.Text.RegularExpressions.Regex::.ctor` applies inside the pair's
   interval (ADR 0040 decision 2). A row with a precondition applies as it does to a call whose
   arguments are not read.
4. **The emitted matcher is no procedure.** A type that carries the regex generator's `[GeneratedCode]`,
   and every type nested in it, is left out of symbol enumeration: its members are no pairs, no Added and
   no Removed. Each side's count of the procedures left out is in `run.properties.generatedProcedures`
   (`legacy`, `modern`), written when either is not zero (ADR 0029: nothing is dropped silently).
5. **Other generators stay as they are.** Their output is enumerated and compared as written code. The
   interop generator's stays Unknown on a pair that crosses a runtime, by ADR 0054 decision 4: there the
   runtime marshals the call, so equal declarations do not make equal behaviour. A generator joins
   decision 1 by a dated clarification here, which must say what its declaration is and which runtime rows
   stand for its runtime.

## Why
- ADR 0024's argument is that what a method does is fixed by the code bound for it, by its callees, and by
  the runtime. For a `[GeneratedRegex]` member the first is the attribute: the generator's contract is
  that the member returns a `Regex` for that pattern, those options, that timeout and that culture, the
  one `new Regex(pattern, options, timeout)` would construct. The emitted matcher is how that `Regex`
  matches, as the interpreter inside the base class library is how a constructed one does. `equiv`
  already treats `new Regex(p, o)` on both sides as one shared call (ADR 0018, ADR 0019) with
  `runtime-changes.json` as the listed exception, and reads no line of the interpreter to do so.
  Decision 1 makes the same claim for the same object, and decision 3 carries the same exception.
- Comparing the emitted code proves nothing about the regex. The 4 EQ002 are two versions of a
  vectorised search for the first possible match position, each correct for its pattern. A proof that
  two such matchers accept one language is a regular-language equivalence problem stated over span
  arithmetic, which no rung is built to decide; and a Divergent there would mean a generator bug, which
  is the runtime's to answer for, as a base class library bug is.
- The generator's stamp is required because a `[GeneratedRegex]` member whose implementing part somebody
  wrote by hand has code of its own. The stamp is on the member, so it is read from the symbol in that
  side's compilation, not from a file name or a path.
- Only the `[GeneratedRegex]` attribute is in the text. The stamp holds the generator's version, which
  differs between any two runtimes, and no other attribute is an input of the generator.
- The emitted types are `file`-local and nothing written can name them, so leaving them out removes no
  callee from any written body. Their hash-named helpers stop appearing as Removed and unmatched.
- Unknown for unequal declarations, as ADR 0054 decides for unequal imports: two patterns can accept
  the same language, and nothing here can say.

## Rejected
- **Keep comparing the emitted code, in a review group ranked last.** The 17 results stay wrong or
  undecided, they are only read later; and the 30 `no-body` Unknowns on the declarations stay.
- **Skip generated trees and say so in `run.properties`, with no rule for the declaration.** An edited
  pattern would then be reported nowhere: the declaration is `no-body` on both sides either way.
- **Fingerprint the implementing part on every runtime pair (extend P2-107's rule).** It returns a
  singleton of a `file`-local type; its text says nothing about the pattern, and the type's members
  would still be compared.
- **Every type with any `[GeneratedCode]` is left out.** Designer files, `resx` accessors and
  hand-edited generated code carry it, and their declarations are not attributes `equiv` can compare.
- **Recognise generated trees by file path.** A `Compilation` does not say which trees a generator
  added, and a path is what ticket P2-072 was wrong about.
- **Divergent when the declarations differ.** No counterexample exists.

## Consequences
- On a pair with `[GeneratedRegex]` members, each unedited one is an EQ001 by congruence where it was an
  EQ003, and each edited one stays an EQ003, `no-body`. The matcher's results are gone from the report
  and counted in `run.properties.generatedProcedures`. Expected on `jellyfin-13023`: 137 fewer results,
  30 fewer `no-body` Unknowns.
- An Equivalent `[GeneratedRegex]` pair assumes what a shared `new Regex` call assumes: that the two
  runtimes' engines agree on the pattern wherever no table row says otherwise. It also assumes both sides
  were generated under one build culture, which the generator reads when `RegexOptions.IgnoreCase` is
  set and the attribute names no culture.
- A side whose generator did not run (the member has no stamp) keeps `no-body` and no fingerprint.
- ARCHITECTURE.md's frontend step 2, VERIFICATION-MODEL.md sections 1 and 6, and `ProcedureEnumerator`
  say this. `FrontendAnalysis` carries the two counts.
- Tickets: P2-118 implements this for the regex generator and lists the other generators the corpus
  pairs use.
