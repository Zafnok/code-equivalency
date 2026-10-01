# P2-069 An unchanged call site that a dependency upgrade rebinds is still the same call
Status: in-progress
Effort: L
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-047

## Goal
P2-047's audit found 9 false Divergents where the method's source is unchanged but a dependency
upgrade changed what a call site binds to. A property's type became an interface where it was a
class, so `fs.File.Exists(p)` calls `IFile::Exists` instead of `FileBase::Exists`. A generic call
got a new type argument (`Returns<FileBase>` became `Returns<IFile>`). A generated class moved
namespace. Each side then calls a different uninterpreted function, and the solver gives them
different results. Minimal repro, as a sample pair with a library project that changes between the sides:

```csharp
// library, legacy side
public abstract class FileBase { public abstract bool Exists(string p); }
public interface IFs { FileBase File { get; } }
// library, modern side
public interface IFile { bool Exists(string p); }
public interface IFs { IFile File { get; } }
// the method, identical on both sides
static bool Has(IFs fs, string p) => fs.File.Exists(p);
```

Today this is EQ002. It is not a false Divergent that can be proved Equivalent, since the library's
implementations are not visible. It should be Unknown, naming the rebinding, not Divergent. Decide,
through `equiv-adr`, when two different callee identities at textually identical call sites are
treated as possibly the same function, and what verdict that gives.

## Spec references
ADR 0018, ADR 0019, ADR 0026 (Divergent only when untainted), VERIFICATION-MODEL section 3
(the equivalence table and call identity), P2-047's audit.

## Acceptance criteria (all must hold; nothing beyond them)
1. The `equiv-adr` outcome is merged before any code change.
2. The pair above, as `samples/dependency-rebinding`, is not Divergent, and its SARIF names the two
   identities it treated as possibly the same.
3. A variant in which the modern method really calls a different member (for example `Delete`
   instead of `Exists`) stays Divergent.

## Tests
- Integration test on `samples/dependency-rebinding` (both variants).
- Unit tests the decision names.

## Out of scope
Matching members whose names changed. BCL overload rebinding (P2-070).

## Notes
- Found by P2-047: 9 false positives on Git Extensions (7 from one System.IO.Abstractions upgrade,
  1 generic instantiation, 1 generated `Resources` class that moved namespace).
- Decision (criterion 1, through `equiv-adr`): a new ADR, 0042 (PR #325), not a clarification. It narrows
  VERIFICATION-MODEL section 3's rule that two callee identities are two functions, moves a class of
  pairs from Divergent to Unknown and adds a SARIF property. A call site is a call the lowering emits
  at a syntax node, keyed by the node's tokens and the member's name. A key on both sides of a pair
  that binds to identities L and M is a rebound pair, and every call to L on the legacy side and to M
  on the modern side is an unshared `IrOpaque` with reason `rebound-call`. ADR 0014 then gives Unknown
  (`opaque`, scope `line`), and `properties.reboundCalls` names the pair. Rejected there: one shared
  function (an Equivalent on library code nobody checked), a tainted shared function, pairing by
  shape without the text, a rule for a retyped member whose identity is unchanged, leaving out
  framework callees, a new `unknownReason`.
- Decision: how a rebound call reaches the backend -> an `IrOpaque` the frontend emits, no backend
  change. Alternatives: a `Rebound` flag on `CallIdentity` that the encoder treats as an opaque point;
  a new `UnknownReason`. Rule: 4.
- Decision: how call sites are found -> the lowering records every `IrCall` it emits (`CallSites`),
  `CSharpFrontend` pairs the two recordings and lowers a rebound pair a second time. Alternatives: a
  separate walk over the operation tree before lowering. Rule: 1. The recording is exactly what is
  lowered, so the catalogue's rewrites, constructor initializers and the calls a `foreach` or
  `using` inserts are covered without restating the lowering's rules.
- Decision: what is marked -> every call to a rebound identity on its side, not only the site that
  was compared. Alternatives: marking by site in the IOperation lowering and by identity in the IL
  lowering. Rule: 3. Both lowerings then give the same IR, which `--il-fallback` relies on (ADR 0039):
  `IlFallback.Side.Rebound` hands the IL lowering each side's identities.
- Decision: where the pairs live -> `Equiv.Core.Matching.ReboundCall`, on `ProcedurePair.ReboundCalls`
  and `VerificationResult.ReboundCalls`, written as `properties.reboundCalls`. Alternatives: strings
  such as `"L -> M"`. Rule: 2.
- Decision: the sample's shape -> each side is two projects, `App` and `Files`, with the solution in
  `legacy/` and `modern/` as every sample has it. `Files` holds one implementation, `NoFiles`, because
  a project that yields no procedure is a load failure (P2-018). The legacy `Files` project has no
  `AssemblyInfo.cs`: `IlSamples` compiles each side's sources as one compilation, and two sets of
  assembly attributes do not compile. The variant is `Probe.Clear`. Rule: 3.
- Unit tests of the decision: `CallSitesTests` (the pairing rule), `ReboundCallLoweringTests` (both
  lowerings), `CSharpFrontendTests.AReboundCallSiteIsListedAndOpaqueInBothBodies`,
  `.ACallIdentityRenameKeepsAReboundSiteACall`, `.UnderTheIlFallbackAReboundCallIsOpaqueInTheIlBodiesToo`,
  `IlFallbackTests.EachSideIsReadWithItsReboundIdentities`, `SarifReportWriterTests.Sarif_ListsReboundCalls`,
  `CompareCommandTests.ThePairsReboundCallsReachTheResult`.
- Surprise: `Probe.Clear` is Divergent on the null check of `fs.File`, not on the `Exists` and `Delete`
  events. `IFs::get_File()` has one identity and two result sorts, so the two results are unrelated
  values and the solver makes one of them null. That is the retyped member with an unchanged identity,
  which ADR 0042 leaves out (its Consequences). A body that only tests such a value, with no rebound
  call after it, is still a false Divergent.
- Surprise: the catalogue rewrites the legacy side only (ADR 0020). When the modern side still calls
  the entry's legacy member, the same text binds to the entry's modern member on the legacy side and
  to the legacy member on the modern side, so the site is rebound and the pair Unknown. It was
  Divergent before. `CSharpFrontendTests.RecordsTheEquivalencesAppliedToTheLegacyBody` used one
  compilation for both sides and now pins this as `Kept`.
- No existing sample's `expected.sarif.json` changed.
