# P1-002 Loop ladder rung 5: LLM-proposed coupling invariants, Z3-checked
Status: todo
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P1-001

## Goal
When rung 4 times out, ask a model for a candidate coupling invariant and let Z3
check it. A wrong guess can never produce Equivalent. Off by default.

## Spec references
VERIFICATION-MODEL.md section 5.1 rung 5; ADR 0008; P1-001 (`FragmentEncoder`, the
rung-4 obligation).

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
