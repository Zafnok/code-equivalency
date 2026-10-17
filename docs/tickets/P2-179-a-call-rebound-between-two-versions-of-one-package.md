# P2-179 A call rebound between two versions of one third-party package is decided from the two versions' code, not left Unknown
Status: in-progress
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
ADR 0042 makes a call opaque, with reason `rebound-call`, when the same source text binds to another
callee on the modern side, because "the library's code is not in the run". P2-137 counted the callee
pairs behind the reason and gave the base class library ones that are one call a catalogue entry. The
third-party pairs got none, and no open ticket owns them.

P2-130's run of `gitextensions-8522` (equiv `e98fd1b9`, compare mode quick,
`docs/runs/2026-10-13-full-gitextensions-8522/SUMMARY.md`) has `rebound-call` alone in 121 of 938
changed pairs (12.9%), and in 108 Unknown results (11.5% of changed pairs): the pair's largest opaque
reason, above ADR 0028's 5%. P2-137's table names who they are: FluentAssertions 5.2.0 to 5.10.3
(`ActionAssertions::Throw<T>` to `DelegateAssertions<Action>::Throw<T>`, 46 changed pairs alone),
System.IO.Abstractions 2.0.0.144 to 11.0.6 (`FileBase` to `IFile`, `DirectoryBase` to `IDirectory`)
and NSubstitute 3.1.0 to 4.2.1 (`Returns<FileBase>` to `Returns<IFile>`). On `duplicati-3124` the
reason is alone in 14 of 707 (2.0%).

Both versions of each package are restored on disk for the run (the corpus restores both sides), so
"not in the run" is a choice, not a fact. When done, a rebound pair whose two callees are members of
two versions of one package is decided from those members' code where it can be, and stays Unknown
where it cannot.

## Spec references
ADR 0042, ADR 0020 (API equivalences), ADR 0039 (IL lowering), ADR 0019 (named callee assumptions);
`docs/tickets/done/P2-137-*.md` (its tables); `.claude/skills/equiv-adr`; `.claude/skills/equiv-corpus-run`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Count, on `gitextensions-8522` at `main`'s head, the rebound pairs whose two callees are in one
   package (by assembly name) at two versions, by package, with the changed pairs that hold only them.
2. Decide, through `equiv-adr`, how such a pair may be decided: for example by lowering both callees
   from IL and verifying them as a pair the same run checks (ADR 0019's named assumption, discharged),
   or by a per-package equivalence entry, or not at all. The decision says why it cannot prove a pair
   whose two library versions behave differently.
3. Implement the decision for the largest package of criterion 1, with a unit test and a sample pair
   under `samples/` that has two versions of one local library.
4. Rerun `gitextensions-8522` in compare mode quick and record, in Notes, `rebound-call`'s changed
   pairs alone and its Unknowns next to 121 and 108, and that no result that was Divergent became
   Equivalent without the library's two bodies being proved equal.

## Tests
Unit and integration tests for the chosen rule; the sample pair of criterion 3.

## Size guard
A change to how a base class library rebound pair is decided: stop, that is P2-137's catalogue.

## Out of scope
Catalogue entries for base class library pairs; P2-143's span overloads.

## Notes
- Filed 2026-10-14 by P2-130 (criterion 6): the largest opaque reason of `gitextensions-8522` with no
  open owner.

### Criterion 1: the rebound pairs of one package at two versions (2026-10-17)
- Source: `gitextensions-8522`'s quick run with `--execute` on `main`'s head (P2-237's run, equiv `de93b9c7`, the same
  `src/` as `82ba9c14`): 13,592 matched pairs, 837 changed, 144 results with `reboundCalls`. A callee's package is the
  assembly that declares it, read from its declaring namespace and checked against the restored package's DLL. Each of
  these packages is one assembly of the same name. A pair counts when both callees are in that one assembly. A base
  class library member constructed over a package type (`IEnumerator<ExCSS.StyleRule>::get_Current()`) is a base class
  library pair and is not counted here.
- "Held only by them": an Unknown (`opaque`) result whose reached opaques are all `rebound-call` and whose rebound
  pairs are all in that package. A run's SARIF has no reason set per pair, so this is read from the result's message.
  The census's 121 (reason set exactly `rebound-call`) also holds the base class library and solution pairs P2-137
  lists, and 2 results with both NSubstitute and `System.IO.Abstractions` pairs.

| Package (assembly), versions | Callee pairs | Changed pairs naming them | Held only by them | Why ADR 0061 does not make them one call |
|---|---|---|---|---|
| FluentAssertions 5.2.0 -> 5.10.3 | 21 | 70 | 69 | `ActionAssertions::Throw<T>` is a member of a non-generic type and `DelegateAssertions<Action>::Throw<T>` of a generic one, so the type arguments at the call differ (rule 2). The bodies differ too: 5.10.3 fails first on a null subject, asks the virtual `CanHandleAsync`, and words the failure message differently |
| System.IO.Abstractions 2.0.0.144 -> 11.0.6 | 14 | 21 | 11 | every callee is abstract (`FileBase`) or an interface member (`IFile`): no body, and a virtual call (rule 2) |
| NSubstitute 3.1.0 -> 4.2.1 | 6 | 15 | 7 | `Returns<FileBase>` and `Returns<IFile>` have other type arguments (rule 2), and 4.2.1's body calls `ConfigureReturn` where 3.1.0 calls a private `Returns` |
| AdysTech.CredentialManager 1.7.0 -> 2.1.1 | 3 | 3 | 3 | another signature: the credential type enum moved out of `CredentialManager` (rule 2), and it is a value type (rule 4) |
| ExCSS 2.0.6 -> 4.1.0 | 2 | 1 | 0 | `Parser::Parse` to `StylesheetParser::Parse`: another type and body |

### Criterion 2: the decision
- ADR 0061 (accepted in this PR): a rebound pair whose two callees are one assembly's members at two versions is one
  call when the two members run the same IL. That covers the members' own bodies and every member of the assembly they
  reach, compared the same way. A member of any other assembly must be a framework member that no runtime-changes row
  names, or one of the same build. Proved pairs are listed in `properties.reboundCallsProved`. The ADR says why a pair
  whose versions behave differently cannot be proved: a behaviour difference needs a different instruction in some
  compared body, or a callee the rule refuses.
- The bar test: the decision changes verdicts on existing input, adds a SARIF property and reverses one of ADR 0042's
  Rejected reasons ("could never be discharged"). So it is a new ADR, not a clarification. ADR 0042 is marked
  superseded in part by 0061. It is accepted in the same PR as its implementation, since the ticket asks for both.
- Decision: compare the IL for identity rather than verify it with the solver -> identity. Alternatives: lower both
  callees from IL and verify them as a pair of the run; a per-package catalogue. Rule: 4. No third-party pair on the
  corpus is refactored but equal (criterion 1's table). Roslyn imports no private member of a referenced assembly for
  ADR 0039's resolution, and identity reads both builds without a compilation. ADR 0061 names the solver as the
  extension.
- Decision: a token is compared by the name it resolves to in ILSpy's type system, not by its number -> by name.
  Alternatives: raw tokens; Roslyn symbols. Rule: 1. Two builds number their tables differently, and Roslyn has no
  symbols for a package's private members.
- Decision: a virtual, abstract or override callee is refused even when sealed -> refused. Alternatives: allow a
  sealed override. Rule: 4. It is one test of three flags, and no pair on the corpus is a sealed override.
- Decision: a runtime-changes row is matched by member name over every overload and every runtime, and a row that ends
  in `::` or a partial name covers every member it starts -> by name. Alternatives: the exact `CallIdentity` within
  the pair's interval. Rule: 4. Library IL has no Roslyn symbol to build an identity from, and the name match refuses
  more, never less.
- Decision: the comparison lives in `Equiv.Frontend.CSharp/Lowering/Il/TwoVersions.cs` and reads modules through
  `IlAstReader.Library` -> beside the IL reader. Alternatives: a new project. Rule: 4. ADR 0039 keeps
  `ICSharpCode.Decompiler` in this one project.
- Decision: a proved pair is carried as `ReboundCall.Proved` and split in SARIF into `reboundCalls` and
  `reboundCallsProved` -> one list with a flag. Alternatives: a second list on `ProcedurePair` and
  `VerificationResult`. Rule: 4. Every place that passes `reboundCalls` along then passes both.

### Criterion 3: implementation, tests and sample
- `TwoVersions` implements rules 1 to 6. `CallSites.Prove`, `For` and `Same`: a callee in two rebound pairs is one call
  with neither, and the legacy lowering maps a proved legacy member to the modern one. In `IrLowerer` and `IlLowerer`
  a call to a proved legacy member is the same call to the modern member. `CSharpFrontend` asks `TwoVersions` once per
  rebound pair. `SarifReportWriter` writes `reboundCallsProved`.
- Tests: `TwoVersionsTests` has one case per rule and per refusal, each from two builds of a library emitted to files,
  plus project references, the calling project itself, reference assemblies, and framework and same-build
  dependencies. Also `CallSitesTests`, `CSharpFrontendTests.AReboundPairOfOneLibraryAtTwoVersionsWithTheSameIlIsOneCall`
  (both lowerings) and `SarifReportWriterTests.Sarif_ListsProvedReboundCallsApart`.
- Sample `samples/package-version-rebound`: one library at two versions (.NET Framework 4.8 and .NET 10, one assembly
  name) whose extension class was renamed. `Probe.Loud` is Equivalent with the pair in `reboundCallsProved`.
  `Probe.Neat`, whose callee's body changed, is Unknown (`opaque`, `rebound-call`). End-to-end assertion:
  `SamplesEndToEndTests.PackageVersionRebound_TwoVersionsOfOneLibraryAreOneCallOnlyWhereTheirIlIsTheSame`.
- Deviation: criterion 3 asks to implement the decision "for the largest package of criterion 1". The rule is not per
  package, so it is implemented once for every assembly. On FluentAssertions it decides that no pair is one call
  (criterion 1's table), which is the result the ADR expects for two versions that behave differently.
- The sample's legacy library has no `AssemblyInfo.cs`, so its version is 0.0.0.0: `IlLowererTests` compile all of a
  sample side's files as one compilation, where a second set of assembly attributes is an error.

### Criterion 4: the rerun of `gitextensions-8522`
- Run: this branch at `9288f41a`, compare mode quick, `--execute --jobs 4`, the same restored checkout and default
  config as the baseline (P2-237's run at `de93b9c7`, the same command). Wall-clock 379 s (baseline 488 s), lowering
  115 s (baseline 141 s), exit 1 (Divergent results exist, as before). The run directory is outside the repository.
- `rebound-call` alone: 121 changed pairs of 837, before and after (the Goal's 121 of 938 was the 2026-10-13 run).
  `rebound-call` in any reason set: 136 and 136. Unknown results with `reboundCalls`: 128 and 128, of them
  Unknown (`opaque`) 112 and 112 (the Goal's 108 was the 2026-10-13 run). Results with `reboundCallsProved`: 0.
- No result changed its rule id: EQ001 12,909, EQ002 24, EQ003 557, EQ004 15, EQ005 192, EQ006 121 on both runs, each
  procedure with the same rule. So no result that was Divergent became Equivalent, with or without a proved pair.
- Why nothing moved: criterion 1's table. Every third-party pair on this pair of versions is refused by rule 2 (other
  type arguments, a virtual or abstract callee, another signature) before any IL is compared, and the
  FluentAssertions, NSubstitute and ExCSS bodies differ as well. That is ADR 0061's expected result for two library
  versions whose code changed: those calls are honestly Unknown.
- No follow-up ticket is filed for the 121. Of them, 90 are held by third-party pairs whose library code changed or
  has no body (criterion 1). Proving them would mean proving different code equal, which the rule refuses by design.
  The rest are P2-137's base class library and solution pairs, which their own tickets own.
