# P1-002 Loop ladder rung 5: LLM-proposed coupling invariants, Z3-checked
Status: done (PR #228)
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P1-001

## Goal
When rung 4 times out, ask a model for a candidate coupling invariant and let Z3
check it. A wrong guess can never produce Equivalent. Off by default.

## Spec references
VERIFICATION-MODEL.md section 5.1 rung 5; ADR 0008; ADR 0036 (a proposed invariant is a
hypothesis until Z3 admits it; results record `properties.proposedBy`); P1-001
(`FragmentEncoder`, the rung-4 obligation).

## Acceptance criteria (all must hold; nothing beyond them)
1. `IInvariantProposer` in `Equiv.Verify.Z3` with one method:
   `Task<string?> ProposeAsync(InvariantRequest request, CancellationToken ct)` where
   `InvariantRequest` carries the IR text of both loop fragments, the header variable
   pairs with types, and the previous rejected candidates with their Z3 counterexamples.
   Returns SMT-LIB text over the declared variables, or null to give up.
2. `LlmInvariantRung`: at most `MaxRounds` (default 3) calls; each candidate is
   parsed with `ctx.ParseSMTLIB2String` against the declared sorts; a parse failure is
   fed back as a rejection; the rung-4 obligation is re-run with `Inv` fixed to the
   candidate; UNSAT on all three obligations (init, step, exit) gives Equivalent with
   `proofMethod: llm-invariant` and `properties.invariant`; otherwise Unknown(no-invariant).
3. Proposer implementations: `FakeInvariantProposer` (test project, scripted answers)
   and `AnthropicInvariantProposer` (production) using the Claude API over plain
   `HttpClient` with the Messages endpoint; model id and API key from
   `--invariant-model <id>` and the `ANTHROPIC_API_KEY` environment variable. No SDK
   package unless ADR 0002 gets a row in this PR. Prompt is one template file embedded
   as a resource; it includes the IR text and asks for SMT-LIB only.
4. Off by default: without `--invariant-model`, the rung is skipped and the ladder
   reports rung 4's Unknown. When enabled, the CLI prints one stderr line
   `note: sending loop IR text to <model id>` before the first call.
5. Every candidate, verdict and counterexample round is recorded in
   `properties.ladderTrace`.

## Files
`src/Equiv.Verify.Z3/Ladder/IInvariantProposer.cs`, `InvariantRequest.cs`,
`LlmInvariantRung.cs`, `AnthropicInvariantProposer.cs`, `Prompts/invariant.txt`;
`src/Equiv.Cli/CompareCommand.cs` (one option).

## Tests
`Rung_AcceptsOnlyWhenAllObligationsUnsat`, `Rung_FeedsCounterexampleBack`,
`Rung_StopsAtMaxRounds`, `Rung_ParseFailureIsRejection`, property
`RandomWrongInvariantIsNeverAccepted` (CsCheck over random linear predicates),
`AnthropicProposer_BuildsRequestAndParsesResponse` (HTTP handler fake, no network),
`Cli_PrintsNoteWhenEnabled`, snapshot with the fake proposer solving the `loop-fusion`
sample after rung 4 is forced to time out (timeout 1 ms).

## Size guard
One prompt template, one HTTP call site, no agent loop beyond `MaxRounds`.

## Out of scope
Prompt tuning. Caching. Any other model provider. Sending anything but IR text.

## Notes
- Deviation: rung 4 has one relation per pair of an old and a new cut point (P1-001 Notes), not one `Inv` per loop pair, so a candidate is one SMT-LIB `define-fun` per relation `inv.<a>.<b>`, over the argument names `ChcEncoder.Invariant` already prints (`in.*`, `lit.*`, `old.*`, `new.*`). "The header variable pairs with types" in criterion 1 are those relations' arguments with their SMT-LIB sorts (`InvariantRequest.Relations`); "the IR text of both loop fragments" is the IR text of both procedures, whose loops rung 4 cuts at every header.
- Decision: obligations -> rung 4's divergence rules with every relation replaced by its definition and `bad` by false, grouped as init (no relation in the premise), step (a relation in premise and conclusion) and exit (a relation in the premise, `bad` in the conclusion), one SMT query each (`ChcEncoder.Refutes`). Alternatives: re-running Spacer with the relations fixed (Spacer cannot take a definition; it would only re-derive). Rule: 1.
- Decision: rung 5 checks in `ChcArithmetic.WrappingIntegers`, not the plain integers or bitvectors: P1-001 showed that a definition solving the wrap-around clauses is a proof over the bitvectors, and the model writes linear integer arithmetic, which it writes better than bitvector terms. The consequence is that `loops/fusion`'s invariant must bound its counters (`inside <= i <= max(n, 0)`), which Spacer could not find and the hand-written test invariant states. Rule: 4.
- Decision: parsing -> the candidate text with `(assert (inv.<a>.<b> <args>))` appended, parsed once per relation with only that relation's argument constants and their uninterpreted sorts declared, so a `define-fun` expands to its body over them; a body naming any other uninterpreted symbol (a `declare-const`, `declare-fun`, or the relation left undefined) is rejected, since a free constant could coincide with a rule's own constant and couple the definition to that rule. Whatever the text asserts, the last assertion is then a formula over the arguments, which is always a sound definition to check. Rule: 4.
- Decision: a failed obligation's counterexample is the first broken rule, as `<premise relation>(<arg> = <value>, ...) -> <conclusion relation or bad>(...)`, with `entry` for a rule without a premise; a Z3 `unknown` on an obligation is itself a rejection ("Z3 gave up on the <name> obligation: ..."). Rule: 1.
- Decision: rung 5 runs only after rung 4's `Unknown(ChcTimeout)` (criterion 2's "when rung 4 times out"), not after `ChcSpurious` or a not-applicable rung 4; each round is one `LadderStep` with rung `llm-invariant` (Proved or Inconclusive) naming the candidate on one line and Z3's verdict (criterion 5), and the last round carries the `Unknown(NoInvariant)` or the Equivalent. Rule: 1.
- Decision: `ProofMethod.LlmInvariant` spells `llm-invariant`, `UnknownReason.NoInvariant` spells `no-invariant`, and `Equivalent.ProposedBy` is SARIF `properties.proposedBy` (ADR 0036 decision 1), set only by rung 5. Rule: 2.
- Decision: `MaxRounds` is a constant 3 with no CLI option (not in the Files list). Rule: 5.
- Decision: the proposer is chosen in `Z3Backend` from `VerificationOptions.InvariantModel` (a Core option, like `ChcIntMode`), through a factory the internal constructor lets tests replace; the CLI only passes the model id and prints the note, once, before verifying any pair. The API key is read from `ANTHROPIC_API_KEY` when the backend builds the proposer; a missing key or an HTTP failure throws from the first call, so the pair gets ADR 0023's error notification rather than a quiet Unknown. The model's `none` or an empty answer gives up. Rule: 4.
- Decision: the request is a `JsonObject` serialised to a `StringContent` (no reflection-based `JsonContent.Create`), with `max_tokens` 16000 and no `thinking` parameter (the model id is the user's, so its thinking rules are not known here); the shared `HttpClient` has a 10-minute timeout. The prompt template is read with `GetManifestResourceStream`, as `ApiEquivalenceTable` reads its catalogue (the ticket asks for an embedded resource). Rule: 5.
- Decision: the snapshot runs the ladder on `loops/fusion` (the IR of `samples/loop-fusion`; the sample itself needs the C# frontend, which `Equiv.Verify.Z3.Tests` may not reference) at 1 ms, with rung 5's obligations given their own 10 s timeout through `LoopLadder.InvariantTimeoutMs`: at 1 ms they time out as well (`Rung_TimeoutIsRejection`). It holds the verdict and the ladder from rung 4 on; rungs 1 to 3 at 1 ms may or may not time out. Rule: 3.
- Decision: the soundness property runs random linear predicates as definitions for `loops/int-proof-wraps`, a pair equal over the plain integers but Divergent over the bitvectors, so no definition may be admitted; 50 samples, one thread. Rule: 3.
- Toolchain: `Context.ParseSMTLIB2String` stops at an `(exit)` in the text and returns the assertions before it, so a candidate can leave no assertion at all; that is a rejection ("asserts no definition").
