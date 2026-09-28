# M5-002 `equiv mcp` `probe` tool: an agent runs one matched pair on both runtimes with its own inputs
Status: done (PR #266)
Effort: M
Model: Sonnet, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M5-001, M4-009; ADR 0035 accepted

## Goal
An agent that gets an Unknown back from `compare` has no way to test its own hunch about the
pair. `probe` lets it name a matched pair and supply argument values. It gets back both
runtimes' canonical outcomes, using M4-009's replay path. It is the agent-facing form of ADR 0035:
the agent proposes inputs, and the runtimes answer. It never changes a verdict. It is registered
only when the server runs on Windows and was started with `equiv mcp --execute` (ADR 0035: opt-in
on every surface, so an agent cannot turn execution on by itself).

## Spec references
ADR 0033 (MCP surface, SARIF is the only result schema for `compare`); ADR 0035 (execution never
proves; Windows only; runs user code); ADR 0036 (agent output is a hypothesis).

## Acceptance criteria (all must hold; nothing beyond them)
1. `equiv mcp` registers a `probe` tool with inputs `{ legacy, modern, identity, arguments: [json],
   culture? }`. It is registered explicitly, not by assembly scanning. Without `--execute`, or
   on a non-Windows OS, the tool is not registered and `tools/list` does not show it. `equiv mcp
   --execute` prints ADR 0035's stderr note at startup, and on a non-Windows OS exits 3 with
   ADR 0035's message.
2. `probe` loads both solutions (the same cache `compare` uses in the session, if M5-001 has one),
   finds the matched pair by normalised identity, builds drivers as M4-009 does, and returns
   `{ legacy: {kind, canonical}, modern: {kind, canonical}, equal: bool }`. An unconstructible
   parameter returns an MCP tool error naming it.
3. The tool's description says, in its first sentence, that it runs code from both solutions on
   the host.
4. `probe` never writes SARIF and never changes a `compare` result. Its output is not a verdict.
   The response has no rule id.
5. A test with a fake runner covers the success, not-constructible and unknown-identity paths. One
   Windows integration test probes `samples/removed-null-check` with a `null` argument and gets
   `equal: false`.

## Files
`src/Equiv.Cli/Mcp/*` (the new tool), tests.

## Tests
`Probe_ReturnsBothOutcomes`, `Probe_UnconstructibleParameter_IsToolError`,
`Probe_UnknownIdentity_IsToolError`, `Probe_NotRegisteredWithoutExecute`,
`Probe_NotRegisteredOffWindows`,
`Probe_RemovedNullCheck_NullDiffers` (integration, Windows).

## Size guard
If `probe` starts returning SARIF or verdicts, stop: that is `compare`.

## Out of scope
Remote transport. Batch probes. Heap or object-graph arguments.

## Notes
- Decision: `probe`'s `arguments` are plain JSON values (`JsonElement[]`), not the driver's own wire-format tokens, and are
  bound positionally against the pair's Roslyn method symbols (`IMethodSymbol.Parameters`), receiver excluded (built as
  `new T()`, as replay's is) -> a new `ProbeArguments` (Frontend.CSharp) converts each JSON value to its parameter's wire
  form, and a new `IReplayDriverFactory.Probe(pair, arguments, directory)` builds the same two drivers `Create`/`Plan` do.
  Alternatives: wire-format strings directly (mirrors the driver protocol one-to-one, but unusable for an LLM agent, which
  is the whole point of the tool); a synthetic `Counterexample` reusing `Create` unchanged (fails for any pair with
  synthesised IR parameters, which an agent's plain arguments cannot supply anyway). Rule: 1 (mirror the consumer — the
  consumer here is the agent, not the driver's wire protocol).
- Decision: `probe` never goes through IR/`Counterexample` at all; it calls the pair's Roslyn method symbols directly via
  the existing `legacy`/`modern` `ReplayTarget` dictionaries `ReplayDriverFactory` already holds. An "unconstructible
  parameter" is caught the same way `ReplayArguments.CallObstacle` catches an uncallable method (not public, generic,
  by-ref, no public parameterless constructor) plus a new per-argument JSON-to-wire conversion that fails by naming the
  parameter. Rule: 4 (smaller change: reuses `CallObstacle` and the existing `Driver` compilation step unchanged).
- Decision: raw outcomes for `probe` come from a new `Replayer.Run(ReplayPlan, culture)` (`Equiv.Execute`, public) that
  returns both sides' `ExecutionOutcome`s unconditionally, alongside the existing `Replay` (which only returns outcomes on
  `NotReproduced` and hides them on `Reproduced` — the opposite of what an agent's hunch-check wants to see). Rule: 1
  (mirror the consumer: `probe`'s contract is "always both outcomes", not "reproduced yes/no").
  `Equiv.Cli` already references `Equiv.Execute` (M4-009), so no new architecture edge.
- Decision: `equiv mcp --execute` is a new bool option on `mcp` gating registration server-wide (checked once at startup,
  same stderr note/exit-3 message as `compare --execute`, ADR 0035), not a per-call tool argument — matches criterion 1
  ("registered only when the server ... was started with `equiv mcp --execute`"). `EquivTools` takes an optional
  `ExecutionEnvironment?`; null (the default) leaves `probe` out of `tools/list` entirely. Rule: 2 (closed shape: a
  nullable collaborator the constructor either has or doesn't, rather than a bool flag plus a separately-injected host).
- Decision: `probe`'s `McpServerToolCreateOptions.ReadOnly` is `false` (the SDK's own default is `true`), since ADR 0035
  is explicit that `--execute` runs the user's code with real side effects (file I/O, network), unlike `compare` and
  `lower_only`, which only read. Rule: 3 (the annotation is exactly what a client-facing safety check would assert).
- Decision: an `OutcomeKind`'s wire name is spelled out in a small `switch` in `ProbeTool`, matching `Equiv.Execute`'s own
  internal `OutcomeLine.Name` (not exposed publicly) rather than relying on `enum.ToString()` or widening
  `Equiv.Execute`'s public surface for one caller. Rule: 4 (smaller, self-contained change; the two switches independently
  enforce the same "no enum-formatting reliance" rule this repo already has for wire/report text).
