# M5-002 `equiv mcp` `probe` tool: an agent runs one matched pair on both runtimes with its own inputs
Status: todo
Effort: M
Model: Sonnet, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M5-001, M4-009; ADR 0035 accepted

## Goal
An agent that gets an Unknown back from `compare` has no way to test its own hunch about the
pair. `probe` lets it name a matched pair and supply argument values. It gets back both
runtimes' canonical outcomes, using M4-009's replay path. It is the agent-facing form of ADR 0035:
the agent proposes inputs, and the runtimes answer. It never changes a verdict. It is registered
only when the server runs on Windows.

## Spec references
ADR 0033 (MCP surface, SARIF is the only result schema for `compare`); ADR 0035 (execution never
proves; Windows only; runs user code); ADR 0036 (agent output is a hypothesis).

## Acceptance criteria (all must hold; nothing beyond them)
1. `equiv mcp` registers a `probe` tool with inputs `{ legacy, modern, identity, arguments: [json],
   culture? }`. It is registered explicitly, not by assembly scanning. On a non-Windows OS the
   tool is not registered, and `tools/list` does not show it.
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
`Probe_UnknownIdentity_IsToolError`, `Probe_NotRegisteredOffWindows`,
`Probe_RemovedNullCheck_NullDiffers` (integration, Windows).

## Size guard
If `probe` starts returning SARIF or verdicts, stop: that is `compare`.

## Out of scope
Remote transport. Batch probes. Heap or object-graph arguments.

## Notes
