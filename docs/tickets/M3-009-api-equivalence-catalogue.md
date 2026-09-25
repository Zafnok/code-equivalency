# M3-009 API-equivalence catalogue: overload drift and Web API results
Status: in-progress
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M2-006, M3-010

## Goal
Code nobody touched stops being Divergent just because it binds a different overload on .NET 10.
A Web API 2 action that returns `Ok(x)` or `NotFound()` can be Equivalent to its ASP.NET Core
port. `Equiv.Core` ships `api-equivalences.json`, a table in the style of `runtime-changes.json`.
`Equiv.Frontend.CSharp` applies it while lowering the legacy side, and each result lists the
entries applied to it (ADR 0020).

## Spec references
VERIFICATION-MODEL.md sections 3 and 6; ADR 0020; M2-006 (the table, loader and config-suppression
pattern to mirror).

## Design
Each entry is one of two kinds.
- **Member entry:** `id`, `legacy` (exact `CallIdentity` value), `modern` (exact `CallIdentity`
  value), `arguments` (the modern argument list), `reason`, `url`. Each item in `arguments` is
  one of:
  - `{"arg": n}`: the legacy call's n-th source argument, counting a `params` array's elements
    individually and counting the receiver of an extension method as argument 0;
  - `{"arg": n, "unwrap": true}`: the same argument with its outermost implicit conversion
    removed (Roslyn's `IConversionOperation.Operand`);
  - `{"arg": n, "convertTo": "<metadata name>"}`: the same argument through the implicit
    conversion M3-010 lowers (boxing, upcast);
  - `{"const": <value>, "type": "<IR type>"}`.

  For an instance member on the modern side, the first item is the receiver.
- **Type entry:** `id`, `legacyType`, `modernType` (metadata names), `reason`, `url`.

`TypeMapper` maps a legacy sort name to its modern one on the legacy side only. The call lowering
checks the legacy side's `CallIdentityFactory` result against member entries before emitting
`IrCall`. The soundness condition is ADR 0020's: whenever both members are invoked on adapted
arguments, they return the same value and either neither throws or both throw the same
exception type. The `reason` must say why that holds,
including null handling.

## Acceptance criteria (all must hold; nothing beyond them)
1. `src/Equiv.Core/ApiEquivalences/api-equivalences.json` is an embedded resource, loaded by
   `ApiEquivalenceTable`, with the same comment header style and loader error handling as
   `RuntimeChangeTable`. Every entry has a non-empty `reason` and a `url` under
   `https://learn.microsoft.com/`; a unit test enforces both, and that `id`s are unique.
2. Initial content, and nothing speculative:
   - member entries for `String::Split(Char[])` with exactly one `params` element →
     `String::Split(Char, StringSplitOptions)` with `None`;
   - `Enumerable::Contains<Char>(IEnumerable<Char>, Char)` with argument 0 unwrapped from
     `String` → `String::Contains(Char)` (ADR 0020 explains why the null case stays Divergent,
     correctly);
   - member entries for the Web API 2 `ApiController` helpers `Ok()`, `Ok<T>(T)` (argument
     converted to `System.Object`), `NotFound()`, `BadRequest()`
     → the matching `ControllerBase` helpers;
   - type entries `System.Web.Http.IHttpActionResult` → `Microsoft.AspNetCore.Mvc.IActionResult`
     and each helper's concrete legacy result type → its modern counterpart.

   Each entry's `reason` states why it meets the soundness condition. If an entry cannot meet
   it, leave that entry out and record a `Decision:` line in Notes. The Web API entries'
   `reason` also says that they equate action results, not serialized responses (ADR 0020).
   `StatusCode(HttpStatusCode)` is not an entry: `ControllerBase.StatusCode` takes an `int`,
   and an enum-to-`int` conversion is explicit, outside the adapter language.
3. `equiv.config.json` accepts `suppressApiEquivalences` (an array of id prefixes). The config
   loader, its diagnostics and its tests follow `suppressRuntimeChanges`.
4. The frontend applies member and type entries on the legacy side only. `ProcedurePair` gains
   `ImmutableArray<string> EquivalencesApplied` (the sorted, distinct ids that fired in either
   body; empty by default). The SARIF writer emits `properties.equivalencesApplied` when it is
   non-empty. A rewritten call keeps the guards of the legacy call as written: a static or
   extension legacy call gets no receiver null check even when its modern target is an instance
   member (ADR 0020). Test `LegacyLinqContains_EmitsNoReceiverNullCheck`.
5. A `params`-expanded legacy call whose element count differs from the entry's is left as it
   is, and so is any call whose arguments the adapter cannot address. Test
   `SplitWithTwoSeparators_IsNotRewritten`.
6. Sample `webapi-basic` gains `Find(int id)`: `if (id < 0) return NotFound(); return Ok(id);` on
   both sides, returning `IHttpActionResult` and `IActionResult` respectively. Its README table
   lists `Find` as Equivalent, with `equivalencesApplied` naming the ids used.
7. New sample `api-drift`: `Parts(string s)` returns `s.Split(',')` and `HasX(string s)` returns
   `s.Contains('x')` on both sides (the legacy side has `using System.Linq;`). Its README table
   lists `Parts` as Equivalent and `HasX` as Divergent (on a null `s` only), each with its entry
   applied. Both samples lower with zero `IrOpaque` (`Samples_LowerWithoutOpaque`), so choose
   shapes the lowerer covers once M3-010 has landed.
8. VERIFICATION-MODEL sections 3 and 6 already describe this (ADR 0020). Check that they match
   what was built.

## Files
`src/Equiv.Core/ApiEquivalences/{ApiEquivalence.cs,ApiEquivalenceTable.cs,api-equivalences.json}`,
`src/Equiv.Core/Configuration/*` (the new key), `src/Equiv.Core/Matching/ProcedurePair.cs`,
`src/Equiv.Core/Reporting/SarifReportWriter.cs`, `src/Equiv.Frontend.CSharp/Lowering/{TypeMapper.cs,
CallIdentityFactory.cs,IrLowerer.cs}`, `samples/webapi-basic/**`, `samples/api-drift/**`, and
their tests.

## Tests
`Table_EveryEntryHasReasonAndLearnUrl`, `Table_IdsAreUnique`, `Config_SuppressApiEquivalences_*`
(mirroring the runtime-changes config tests), `LegacySplit_IsRewrittenToTheModernOverload`,
`SplitWithTwoSeparators_IsNotRewritten`, `LegacyLinqContains_UnwrapsTheStringArgument`,
`LegacyLinqContains_EmitsNoReceiverNullCheck`, `OkOfInt_ConvertsItsArgumentToObject`, `ModernSide_IsNeverRewritten`,
`LegacyResultSort_MapsToTheModernSort`, `Sarif_ListsEquivalencesApplied`, and the two sample
lowering snapshots.

## Size guard
Three new `src/` files. If you find yourself writing an adapter language beyond positions and
constants with `unwrap` and `convertTo`, stop: that entry is not in scope.

## Out of scope
Explicit conversions in adapters. Differential testing of entries on both runtimes.
EF6 → EF Core, WCF, `System.Web` request/response APIs.

## Notes
- Decision: a fourth `src/` file, `ApiEquivalences/ApiArgument.cs`, holds one adapter item. The
  repo keeps one top-level type per file (Meziantou MA0048), and a public nested type trips CA1034.
  `ApiEquivalence` is a single record with `IsType`; a type entry keeps its names in `Legacy`/`Modern`.
- Decision: the receiver of an instance legacy call is argument 0, as the ticket already says for
  an extension call. Without that, `String::Split` (instance on both sides) cannot address `s`.
- Decision: an entry applies only when its adapter uses every source argument and every position
  it names exists. That is how "element count differs" is detected, so the table needs no count
  field. An array passed straight to a `params` parameter (`s.Split(arr)`) is not addressable,
  because the adapter addresses elements only.
- Decision: `Ok<T>(T)` is matched on the exact `CallIdentity`, which includes the type argument, so
  the shipped entry is `webapi.ok-of-int` (`Ok`1(int)<int>`). `Ok(dto)` or `Ok(string)` stays
  Divergent (a false alarm, not a false proof). Covering every `T` needs a pattern in `legacy`,
  which is beyond "positions and constants" (size guard).
- Decision: an adapter constant is its JSON literal's text with an IR type of `bool`, `bv<n>` or a
  sort name. A sort constant is the element `TypeMapper.Constant` gives a C# constant with the
  same invariant text, so `{"const": 0, "type": "System.StringSplitOptions"}` is exactly the
  modern side's default `StringSplitOptions.None`.
- Decision: a type entry counts as applied when a legacy sort name is mapped by it, so
  `equivalencesApplied` lists type entries too (the `webapi-basic` README names them).
- Decision: `VerificationResult` gains `EquivalencesApplied` and `CompareCommand` copies it from
  the pair, because the SARIF writer only sees `VerificationResult`s (two files the ticket's Files
  list does not name).
- Decision: a rewritten call's identity is checked against `runtime-changes.json` as the modern
  member it becomes, the same flag the modern side's own call gets.
- Decision: `Find` has routes (`find/{id:int}`, `find/{id}`), so it matches as
  `GET /api/orders/find/{id}` like `Get` does.
- Decision: VERIFICATION-MODEL section 3's result-identity bullet named `HttpResponseMessage`
  and sat under "applied to both sides". Neither held, so the bullet now says the catalogue is
  legacy-only and `HttpResponseMessage` has no entry. Section 6 already matched.
- Verified end to end with the CLI: `api-drift` gives `Parts` EQ001 and `HasX` EQ002 (the
  counterexample is a null `s`: legacy returns, modern throws `NullReferenceException`), and
  `webapi-basic` gives `Find` EQ001. Each result lists its entries.
- A fresh worktree needs the samples restored before the integration tests can load them
  (`MSBuild -t:Restore` for the legacy side, `dotnet restore` for the modern side, as
  `build.ps1` does). On a first run, parallel tests loading the same fresh sample once hit an
  `obj/*.AssemblyReference.cache` file lock. It passed on rerun.
