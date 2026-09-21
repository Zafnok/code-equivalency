# ADR 0020: A shipped catalogue of known-equivalent API pairs, applied visibly

Status: accepted (2026-09-21). Amends VERIFICATION-MODEL section 3's rule that "BCL API
changes are NOT auto-equated".

## Context
The code a real 4.8 → 10 migration actually edits is at the framework boundary, and there every
call identity changes. Today each such call is Divergent unless the user writes a
`callIdentityRenames` entry by hand:
- **Web API results.** `ApiController.Ok<T>(T)` and `ControllerBase.Ok(object)`, and
  `IHttpActionResult` and `IActionResult`, are different identities and different sorts. So every
  real controller action that returns `Ok(...)` or `NotFound()` is Divergent. VERIFICATION-MODEL
  section 3 promises a "result identity" normalisation, but M2-005 ruled it out of scope and no
  ticket owns it. The `webapi-basic` sample avoids the problem because its action returns `int`.
- **Overload drift.** Identical source text binds to different members:
  - `s.Split(',')` binds `String::Split(Char[])` on 4.8 and `String::Split(Char, StringSplitOptions)`
    on .NET 10.
  - `s.Contains('x')` binds `Enumerable::Contains<Char>` on 4.8 and `String::Contains(Char)` on
    .NET 10.

  Each is a false Divergent on code nobody touched.

## Decision
`Equiv.Core` ships `api-equivalences.json` alongside `runtime-changes.json`. Each entry names:
- a legacy member and a modern member (in `CallIdentity` form), or a legacy type and a modern type;
- for members, an argument adapter: the modern argument list written in terms of the legacy call's
  source arguments (with a `params` array expanded into its elements), as argument positions
  (optionally with one implicit conversion removed or added) and typed constants only;
- a `reason` and a Microsoft Learn `url` that establishes the equivalence.

The frontend applies the table while lowering the **legacy** side, because only the frontend still
sees source arguments. A `params` array, for example, is an array creation in IR, but a list of
elements in Roslyn. A call to a legacy member is lowered as an `IrCall` to the modern identity with
adapted arguments, and a type entry maps the legacy sort name to the modern one in `TypeMapper`.
This works like `callIdentityRenames`, with the adapter added. The frontend returns the ids of the
entries it applied with each lowered body (`ProcedurePair.EquivalencesApplied`), and the SARIF lists
them in that result's `properties.equivalencesApplied`, so an Equivalent says which assumptions it
rests on.
Users can disable entries in `equiv.config.json` (`suppressApiEquivalences`, prefix-matched like
`suppressRuntimeChanges`). The soundness condition is: for every adapted argument tuple on which
both members are actually invoked, they return the same value, and either neither throws or both throw the same exception type
(the exception type is an observable, VERIFICATION-MODEL section 1). A rewritten legacy call
keeps the guards of the legacy call as written, not of its modern target: a static or extension
legacy call gets no receiver null check. A
guard the frontend emits before a call (for example the null-receiver check on an instance call)
is outside the entry and is still compared as usual. An equivalence that needs any further
precondition is not an entry.

## Why
- "False alarms are cheaper than false proofs" still holds for any single call. But if every
  migrated line is a false alarm, the tool says nothing about exactly the code users most want
  checked.
- A curated, cited table keeps the claim reviewable. Recording each applied entry keeps it visible
  on the result it affects, the same stance as `proofMethod` and `opaqueNodes`.
- The table's shape and loader mirror `runtime-changes.json` (M2-006). That makes it one more data
  file, not a new component. The data lives in `Equiv.Core`, so a Java frontend can ship its own
  table in the same shape; only its application is language-specific.

## Rejected
- **Leave it to users' `callIdentityRenames`.** It pushes the same curation onto every user, it has
  no argument adapter so overload drift cannot be expressed, and it leaves no record in the SARIF.
- **Infer equivalence from matching names or signatures.** This is exactly the automatic equating
  section 3 forbids, and it would produce false proofs.
- **Differentially test each entry on both runtimes in CI.** Worth doing eventually (the Windows
  runner has .NET Framework), but it is its own tool. The citation rule plus review is the MVP bar.

## Consequences
- VERIFICATION-MODEL section 3 is amended so that BCL changes are not auto-equated except through a
  cited catalogue entry, which is recorded on the result. Section 6 lists `equivalencesApplied`.
- The Web API entries equate action results (status code observed, body opaque, as section 3
  already says), not wire responses. Pipeline configuration such as the default JSON serializer
  (Newtonsoft, PascalCase, on Web API 2; System.Text.Json, camelCase, on ASP.NET Core) lives
  outside every procedure and is not checked. Each Web API entry's `reason` says so.
- New ticket M3-009 builds the catalogue, the adapter and the config key, extends `webapi-basic`
  with actions that return `Ok(...)` and `NotFound()`, and adds a sample `api-drift`. M3-003 depends
  on it and asserts both.
- Frontend guards still apply, and that is correct. Take `Enumerable.Contains<Char>(s, c)` →
  `String.Contains(c)`: on a null `s`, the legacy call throws `ArgumentNullException` and the
  modern side throws `NullReferenceException` from the frontend's receiver check. The pair
  therefore stays Divergent on null input, which is the real behaviour change. The entry equates
  only the non-null case, which is the only case where both members are invoked.
