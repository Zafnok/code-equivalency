# P2-241 Execution gap: `probe` analyses a pair with the default config, so a runtime the pair's config states is missing
Status: in-progress
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-237

## Goal
The `probe` tool of `equiv mcp --execute` (ticket M5-002) calls `frontend.Analyze(legacy, modern, EquivConfig.Default, ...)`.
A pair whose runtimes, callee mappings or other declarations come from its `equiv.config.json` is therefore analysed
differently under `probe` than under `compare --config`. On `openra-17989`, whose four mod projects get net5.0 only from
`tools/corpus/configs/openra-17989.json` (P2-197), every `probe` into those projects fails with "the modern project ... has
no detected runtime": P2-237 could not run 8 of OpenRA's sampled results, 3 of them blocked by nothing else.

When done, `probe` takes an optional config path, as `compare --config` does, and analyses the pair under it.

## Spec references
ADR 0033, ADR 0035, ADR 0040 decision 1 (runtimes from config), ticket M5-002, `src/Equiv.Cli/Mcp/ProbeTool.cs`,
`docs/runs/2026-10-17-precision-execute-verdict.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. `probe` has an optional `config` argument, a path to an `equiv.config.json`, loaded as `compare` loads it; a config
   diagnostic is returned as the tool's error, and no argument means the default config, as today.
2. The tool's description and the README's MCP section say what it does.
3. The commit that changes the tool's schema carries the `Release:` footer `equiv-release` says.

## Tests
A unit test that a config naming a project's runtime makes a `probe` of a member there constructible where the default
config is not; a test that a bad config path is the tool's error.

## Size guard
None expected; if the config needs to reach anything beyond `Analyze`, stop and record it.

## Out of scope
Any other change to `probe`'s inputs or outputs. P2-240, P2-242.

## Notes
- Filed 2026-10-17 by P2-237. The blocked rows are OpenRA #0, 2, 12, 14, 17, 26, 27 and 28 of P2-212's sample.
- Decision: every config diagnostic is a `probe` tool error (`error: equiv.config.json: <id> <path>: <message>`, one
  line each), where `compare` prints them as warnings and fails only on an invalid mode: a tool result has no warning
  channel (and adding one is an output change, out of scope), and a config applied only in part would analyse the pair
  differently from the run being followed up. A non-JSON config is `error: <the loader's message>`.
- Decision: `probe` loads the config itself (`ProbeTool.LoadConfig`, `EquivConfigLoader.Load` on the file's text, as
  `CompareCommand.LoadConfig` does) rather than sharing compare's private loader, whose contract is write-warnings-to-a-
  writer and throw-on-mode. The config reaches only `Analyze`; the size guard did not trip.
- The unit test uses `FakeFrontend`'s new `replayUnder` hook (the replay factory chosen from the config `Analyze` got),
  because the fake's runtimes are canned; the real path from config runtimes to `ReplayDriverFactory` is ADR 0040
  decision 1's and already covered by `compare --config`.
