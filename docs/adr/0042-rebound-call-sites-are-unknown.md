# ADR 0042: A call site with the same text on both sides that binds to a different callee is possibly the same call, and its outcome is unknown

Status: accepted (2026-10-01). Narrows VERIFICATION-MODEL section 3's rule that two different callee
identities are two different functions.

## Context
P2-047's audit found 9 false EQ002 among 31 audited (ticket P2-069). In each, the method's source is
unchanged, and a dependency upgrade changed what one call site binds to. A property's type became an
interface where it was a class, so `fs.File.Exists(p)` calls `IFile::Exists` where it called
`FileBase::Exists`. A generic call got a new type argument (`Returns<FileBase>` became
`Returns<IFile>`). A generated class moved namespace. Section 3 gives two identities two unrelated
uninterpreted functions and two different trace events, so every input that reaches the call is a
counterexample. The developer changed nothing there. The library's bodies are not in the run, so
nothing shows the two members behave alike either.

## Decision
A **call site** is a call the lowering emits for a member at a syntax node: an invocation, an object
creation, a property, indexer or event accessor, an `await`. Its key is the node's source tokens (so
layout and comments do not count) and the member's name. When a key occurs on both sides of a matched
pair and binds to callee identity L on the legacy side and M on the modern side, and L differs from
M after the rename map, the API-equivalence catalogue (ADR 0020) and the config's call-identity map
have been applied, then (L, M) is a **rebound pair**: possibly the same function. A key that binds to
several identities on a side pairs each legacy-only identity with each modern-only one. A callee in
the runtime-changes table is never in a rebound pair, so it keeps its EQ006. Every call to L in the
legacy body, and every call to M in the modern body, is lowered as an `IrOpaque` with reason
`rebound-call` and the call's span, which no fragment shares. It is emitted where the call would be,
after the receiver and arguments are evaluated and the receiver is null-checked. ADR 0014 then
decides the pair. It is Divergent only on an input that reaches no rebound call. Otherwise, when
some input reaches one, it is Unknown with reason `opaque`. A pair with no loop then has scope
`line`: it is proved equivalent on every input that reaches none (ADR 0029). Every result of such a
pair carries `properties.reboundCalls`, one `{ legacy, modern }` per rebound pair, sorted. Both
lowerings apply the rule. The IL lowering has no source text, so it takes the pairs found from the
source.

## Why
- Two declarations are not evidence of two behaviours. The source is the same and the compiler chose
  the callee. They are not evidence of one behaviour either, because the library's code is not in the
  run. Unknown is the one verdict that claims neither.
- ADR 0014 already means "an input that reaches this has an unknown outcome". Using it needs no
  backend change and no new `unknownReason`. It also gives a residual claim that is a proof, and
  related locations on the call sites (ADRs 0027 and 0029).
- The key holds the text and the member name, so an edit the developer made stays a difference. A
  call to `Delete` where the legacy side calls `Exists` has another name and other text, so it is
  two ordinary calls and the pair is still Divergent.
- A call is marked by its identity, not by its site, so that the IL lowering produces the same IR as
  the IOperation lowering (ADR 0039). The cost is that a call to L at a site the modern side does not
  have is unknown too. That only loses a Divergent, never a proof.
- Evaluating the operands first keeps everything before the call modelled: the receiver's null
  check, and earlier calls in the same expression.

## Rejected
- **One shared function for L and M, the pair Equivalent with (L, M) listed as an assumption.** The
  proof would rest on library code that nobody checked. ADR 0019's named assumptions are pairs the
  same run verifies. This one could never be discharged. The two calls also differ in a sort
  whenever a receiver was retyped, so sharing them needs the sorts unified as well.
- **One shared function, tainted as an abstraction (ADR 0026).** A divergence past the call would be
  Unknown, but a pair with no divergence would be Equivalent, which is the unchecked proof above.
- **Pairing by shape alone (same member name and parameter count, any text).** That pairs
  `a.Save(x)` with `b.Save(x)`, which is an edit.
- **Also treating a callee as rebound when its identity is unchanged and only its return type
  changed.** `fs.File` would then be unknown, and with it every call through it, including a call to
  another member that the developer did change.
- **Leaving out framework callees.** `s.TrimEnd('/')` binding to another overload fails the same way,
  and the honest verdict is the same. The catalogue still decides first: an entry rewrites the
  legacy call to the modern identity, so that site is no longer rebound and can be proved
  (ticket P2-070).
- **A new `unknownReason`.** Consumers already handle `opaque`. The opaque node's reason and
  `properties.reboundCalls` say which kind it is, and the census counts it by reason.

## Consequences
- A pair whose only difference is a rebound call moves from EQ002 to EQ003. So does a real behaviour
  change behind an overload change, which was Divergent only because the identities differ. It is
  now Unknown with the pair named, and no longer fails the default gate.
- A divergence on a path that reaches a rebound call is not reported as Divergent (ADR 0014's cost).
- The catalogue rewrites the legacy side only (ADR 0020). When the modern side still calls an
  entry's legacy member, the same text binds to the entry's modern member on the legacy side and to
  the legacy member on the modern side. That site is now rebound and the pair Unknown. It was
  Divergent.
- Not covered: a member whose identity is unchanged but whose type changed, with no rebound call
  after it (`fs.File == null` compares values of two unrelated sorts and stays Divergent).
- The census counts these pairs under `opaqueByReason["rebound-call"]`, and they leave
  `changedPairsWithoutOpaque`. `externalCallees` no longer counts a rebound framework call. Under
  `--il-fallback` such a pair is lowered again from IL, and keeps its IOperation lowering unless the
  IL bodies remove some other opaque.
- SARIF gains `properties.reboundCalls`. Verdicts change on existing input, so the change that
  implements this carries `Release: minor`.
- VERIFICATION-MODEL sections 1, 3 and 6 change in the PR that accepts this ADR. Ticket P2-069
  implements it, with `samples/dependency-rebinding`. P2-070's pair is EQ003, with its rebound pairs
  named, until its catalogue entries land.
