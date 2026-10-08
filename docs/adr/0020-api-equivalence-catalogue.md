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

## Clarifications
- 2026-10-07 (P2-137). **Several entries for one legacy member.** A `params` member that the modern
  side binds to a different overload for each element count needs one entry per count:
  `String::TrimEnd(char[])` is `TrimEnd(char)` with one element (P2-070) and `TrimEnd()` with none.
  The decision already leaves a call alone when the entry's adapter cannot address all of its source
  arguments, so entries that share a legacy member are tried in file order and the call takes the
  first whose adapter addresses it. Each such entry addresses another number of source arguments (a
  test pins it), so at most one fits a call and the order decides nothing. The fingerprint names a
  callee without its arguments, so it keeps naming such a member by its first pass-through entry, as
  it did when there was one; the legacy tree still holds the `params` array there, so that name
  never makes two bodies congruent.
- 2026-10-08 (P2-142). **Two members that agree on a stated range of one integer argument.** .NET 9
  added integer overloads of the `TimeSpan` factories, so `TimeSpan.FromHours(2)` binds
  `FromHours(double)` before and `FromHours(int)` after. The two return the same value while the
  result is in the range of `TimeSpan`; outside it the first throws `OverflowException` and the
  second `ArgumentOutOfRangeException`. The soundness condition is about the argument tuples on
  which an entry is applied, and the decision already leaves a call alone when the adapter cannot
  address it. So an argument position may carry an integer range,
  `"integer": {"bits", "min", "max"}`, and the adapter then addresses a call only when the frontend
  knows at that call that the argument, without its implicit conversion, is an integer inside the
  range whose type converts implicitly to a signed integer of that width: a compile-time constant
  inside it, or an argument that is not constant, of a type all of whose values are inside it (every
  `int` of seconds is in the range of `TimeSpan`; an `int` of hours is not). It is passed widened to
  that width, which is the one implicit conversion added. The range is shown at the call and never
  assumed, so it is not a "further precondition": on every tuple the entry is applied to, the two
  members return the same value and neither throws. A call whose argument is not known to be inside
  the range is left as it is and stays a rebound call (ADR 0042). Each such entry's `reason` gives
  the range and why the members agree on it.
  An entry may also name the first runtime that has its modern member (`addedIn`), and then applies
  only to a pair whose runtimes cross it (ADR 0040's interval). On any other pair the modern side
  cannot bind the modern member where the legacy side binds the legacy one, and rewriting the legacy
  call would only make the site a rebound one, which is the cost ADR 0042's consequences name; an
  entry that says when its modern member appeared does not pay it. Entries that share a legacy
  member and differ in that runtime may address the same number of source arguments
  (`FromMilliseconds(double)` is `FromMilliseconds(long, long)` on .NET 9 and
  `FromMilliseconds(long)` from .NET 10 on); the one for the later runtime comes first, so a pair
  whose modern side has both members takes the one its source binds.
  Considered and not taken: modelling both overloads as one pure function of the integer, with a
  guard that throws each side's own exception type outside the range. It would also decide an `int`
  of hours or days, but it puts a model of two library members' bodies into both lowerings, where
  an entry only names members, and `jellyfin-13023` has no such call: of its 62 rebound calls of
  these factories, 43 pass a constant in range, 15 an `int` of seconds or milliseconds, 2 a `long`
  of milliseconds, where the `double` overload can lose bits and the two are not one function even
  in range, and 2 a `double` (ticket P2-142's Notes).
- 2026-10-08 (P2-143). **A `rest` item passes the `params` elements on as a span.** The decision
  lets an adapter list argument positions and constants only, so it could not say "the remaining
  elements, as the modern member's `params ReadOnlySpan<T>`", which is what a `params T[]` member
  needs when .NET 9 adds a span overload next to it (`String::Format`, `String::Join`,
  `StringBuilder::AppendFormat`, `Path::Combine`). An adapter may now end in one `{"rest": n}` item:
  every source argument from position n to the last, which must be exactly the elements of the
  legacy call's `params` array, passed as the span the modern compiler builds from the same
  elements. That span is a new `T[]` of the elements read through the `cast` map of `T[]` to the
  span (VERIFICATION-MODEL section 3), on both sides, so the rewritten legacy call and the modern
  call are the same IR. The elements are evaluated where the legacy call evaluates them. This is
  still the decision's adapter, "the modern argument list written in terms of the legacy call's
  source arguments": nothing is computed, and the soundness condition is unchanged. A call that
  passes an array is not addressed, as before, which is also what keeps the array overload's
  `ArgumentNullException` for a null array out of the equated cases. Such an entry does not pass
  its arguments through, so the fingerprint leaves its calls under their legacy name. The four
  entries name .NET 9 as the runtime that added their modern member (`addedIn`, the clarification
  above), so a pair that does not cross it, where both sides bind the array overload, is left alone.
