# ADR 0059: A callee the config maps to another member is a callee pair the result assumes and names as unproven

Status: accepted (2026-10-14). Extends ADR 0019's `assumedCallees` and `unprovenAssumptions` from matched callee pairs
to the pairs `callIdentityRenames` declares.

## Context
P2-130's adjudication of `duplicati-3124` (`docs/runs/2026-10-14-migrations-verdict.md`) found 11 false EQ002 where
every call to one member is replaced, call for call, by another member that does the same (ticket P2-192). Seven
replace the remoting `CallContext::LogicalSetData(string,object)` and `::LogicalGetData(string)`, which net5.0 lacks, by
a solution-local `CallContext` over `AsyncLocal<object>`. The two members are unrelated functions, so the shim's
`threw` is free. ADR 0020's catalogue holds only base class library pairs with a Microsoft Learn citation. ADR 0047
reads only a forwarder's body. `equiv.config.json`'s `callIdentityRenames` already makes a legacy identity the same
function as a modern one in every encoder (`TraceEncoder.Canonical`), and on `samples/callee-replacement` it turns the
two false EQ002 into EQ001. But no result says it rests on that entry, so a wrong entry would be a silent false proof.

## Decision
A `callIdentityRenames` entry `L → M` declares that every call to `L` on the legacy side is the same call to `M`: the
same arguments in the same order, with the same result, `threw` flag, heap effect and trace event. `L` and `M` may be
any members: base class library, third-party or the solution's own. The declaration does not change: legacy side only, never
inferred, and a function is named by callee and argument types, so an entry whose two members take or return
different types unifies nothing. What changes is the report. The pair `(L, M)` is a callee pair the verdict assumes
(ADR 0019). No result of the run proves it, since `L` and `M` are never a matched pair. So every result of a pair
whose legacy body calls `L`:
- lists `L` in `properties.assumedCallees` and in `properties.unprovenAssumptions`. An Equivalent's message therefore says
  "Assumes callees equivalent; not proved for: L". The contracts pass (ADR 0036 decision 2) never takes `L`, because it
  has no callee pair to prove a contract on;
- lists `{ legacy: L, modern: M }` in `properties.callIdentitiesMapped`, sorted by legacy and then by modern identity.
  It is not part of the fingerprint.

## Why
- The map already has exactly this meaning in every encoder, so the only thing missing was ADR 0019's stance: a
  verdict names what it assumed. A user-declared pair is the clearest case of an assumption. Nothing in the run can
  prove it, so it is always unproven, and an Equivalent that rests on it reads as conditional on it.
- That is how the declaration is kept from hiding a real difference. It is never inferred. Each use is on the result
  it affects, with the counterpart named. A call whose arguments, order or use of the result differ still differs,
  because the pair shares one function and not one value (`Culture::Leave()` in the sample stays EQ002). And the
  modern member, when it is the solution's own, keeps its own Added result, so its body is in the report to review.
- `unprovenAssumptions` is what every other rule already reads as "what this claim rests on that is not proved"
  (`agreesWhen`, the Equivalent message). A separate list would need each of them to learn a second one.

## Rejected
- **A new config key with a `reason`.** It would duplicate `callIdentityRenames`, whose meaning is this one in every
  encoder. A second spelling of one rule is two places to keep in step.
- **A catalogue entry (ADR 0020).** The modern member here is the solution's own, or the legacy one is a third
  party's. The catalogue is shipped, cited and solution-independent (the same reason ADR 0047 rejected it).
- **Proving the shim against a specification of the remoting member.** The shim's body is a `ConcurrentDictionary`
  and an `AsyncLocal<object>`. Proving it would need models of both, and a specification of how a logical call
  context flows across threads and awaits, and no observable in VERIFICATION-MODEL section 1 holds that flow.
- **Running both members on real runtimes (ADR 0035).** A slot store shows its behaviour only over a sequence of calls
  across asynchronous flows. Running one call on each side compares nothing.
- **Inferring the pair from names or signatures.** ADR 0020 rejected that as the automatic equating that produces
  false proofs.
- **Spelling the pair by `M` in `unprovenAssumptions`.** `M` can itself be a matched pair, proved Equivalent, and the
  assumption `L ≡ M` would then vanish from the list. `L` is the key the user wrote, and unique to the entry.

## Consequences
- An Equivalent that rests on a mapped call now lists the mapped legacy identity as unproven. Before this ADR, the
  same Equivalent listed nothing. No verdict, rule id or exit code changes.
- A user can declare, for example, the remoting `CallContext` members as the solution's shim, or a third-party hashing
  factory as `HashAlgorithm::Create(string)`, in `equiv.config.json`. Corpus runs carry no config, so the scoreboard
  does not move. Ticket P2-192's Notes give `duplicati-3124`'s results with the two `CallContext` entries and the hashing helper's
  entry declared: 8 of its 11 false EQ002 become EQ001, each naming its declared callee.
- A replacement that is not one call for one call (`Foo()` for `FooAsync().GetAwaiter().GetResult()`) is another kind of
  equivalence and is not declarable this way. Ticket P2-205 owns it.
- SARIF gains `properties.callIdentitiesMapped`, so the change that implements this carries `Release: minor`. VERIFICATION-MODEL sections 1 and 6 change in the
  PR that accepts this ADR. Ticket P2-192 implements it, with `samples/callee-replacement`.
- Proposed as ADR 0058 and renumbered on merge, since `main` took that number first (ADR 0058 is the cross-runtime
  `[DllImport]` decision).

## Clarifications
- 2026-10-15 (P2-213). **A corpus pair's committed config.** "Corpus runs carry no config, so the scoreboard does not
  move" (Consequences) held before P2-197 gave a pair its own `tools/corpus/configs/<slug>.json`, which `corpus.ps1
  -Compare` passes as `--config`. That file may hold `callIdentityRenames` entries, under this Decision unchanged: each
  entry is an assumption no run proves, and every result that rests on it says so in `unprovenAssumptions` and
  `callIdentitiesMapped`. The scoreboard counts such an Equivalent as it counts any Equivalent with a non-empty
  `unprovenAssumptions` (ADR 0019). An entry is committed only where the pair's pull request itself replaces every
  call to `L` by a call to `M` with the same arguments, and `tools/corpus/README.md` "Pair configs" cites the files
  that show it. An entry is never added to clear a result no adjudication traced. `duplicati-3124` holds the first
  three.
