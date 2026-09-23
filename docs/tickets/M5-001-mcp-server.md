# M5-001 `equiv mcp`: the compare pipeline as an MCP server over stdio
Status: todo
Effort: M
Model: Sonnet, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M3-004

## Goal
A coding agent can launch `equiv mcp` and call `compare` on two solutions, getting back the same
SARIF log `equiv compare` writes, plus a one-line verdict summary (ADR 0033). Same binary, same
container, same pipeline; stdio transport only.

## Spec references
ADR 0033; ADR 0006 (SARIF only); ARCHITECTURE.md `Equiv.Cli` (options, exit codes, `--lower-only`).

## Acceptance criteria (all must hold; nothing beyond them)
1. `ModelContextProtocol` (latest stable, 2.2.0 on 2026-09-23) is in `Directory.Packages.props`,
   referenced by `Equiv.Cli` only, with a row in ADR 0002. The licence gate passes.
2. `equiv mcp` starts an MCP server on stdin/stdout, advertises server name `equiv` and the MinVer
   version, and exits 0 when stdin closes.
3. Two tools, registered explicitly (no `WithToolsFromAssembly` or other assembly scanning):
   - `compare`: inputs `legacy`, `modern` (required), `config`, `baseline`, `bound`, `timeoutMs`
     (optional), with the same meaning and validation as the CLI options. Result: a text summary
     (`Equivalent n, Divergent n, Unknown n, skipped projects n, exit code k`), then the SARIF log
     as JSON text.
   - `lower_only`: inputs `legacy`, `modern`, `config`. Result: the same as `compare --lower-only`.
   Both carry `readOnlyHint: true` and never write files.
4. Every input error `equiv compare` maps to exit 3 or 4 comes back as an MCP tool error
   (`isError: true`) with the same message, not a protocol error and not a process exit.
5. Nothing but protocol messages is written to stdout in `mcp` mode: the route line and the
   analysed-line-count line in `CompareCommand` go through a `TextWriter` the caller passes
   (stdout for `compare`, stderr for `mcp`). `equiv compare` output is byte-for-byte unchanged.
6. The tool result for a sample equals, after removing run timestamps and absolute paths, the
   SARIF file `equiv compare` writes for that sample.
7. ARCHITECTURE.md `Equiv.Cli` lists `equiv mcp` and its two tools. README has an "Use from a
   coding agent" section with a generic stdio server config (`command: equiv`, `args: ["mcp"]`)
   and the container form (`docker run -i --rm -v <repo>:/src equiv mcp`).
8. 100% line and branch coverage on `Equiv.Cli` holds; the mutation gate holds.

## Files
`Directory.Packages.props`, `src/Equiv.Cli/Equiv.Cli.csproj`, `src/Equiv.Cli/Program.cs`,
`src/Equiv.Cli/CompareCommand.cs`, `src/Equiv.Cli/McpCommand.cs` (new), `src/Equiv.Cli/EquivTools.cs`
(new), `tests/Equiv.Cli.Tests/McpCommandTests.cs` (new), `tests/Equiv.Tests.Integration/McpIntegrationTests.cs`
(new), `docs/adr/0002-dependencies.md`, `docs/ARCHITECTURE.md`, `README.md`.

## Tests
`Equiv.Cli.Tests` (in-memory client/server over a pipe, fake frontend and backend):
`ListTools_ReturnsCompareAndLowerOnly`, `Compare_ReturnsSummaryThenSarif`,
`Compare_MissingSolution_IsToolError`, `Compare_UnsupportedInput_IsToolError`,
`LowerOnly_NeverCallsBackend`, `McpMode_WritesNothingButProtocolToStdout`,
`CompareCommand_OutputUnchanged`.
`Equiv.Tests.Integration`: `Mcp_Compare_MatchesCliSarif` on every sample (starts the real
`equiv mcp` process).

## Size guard
More than 4 new files under `src/`, or any change under `src/Equiv.Core`, `src/Equiv.Frontend.*`
or `src/Equiv.Verify.*`, means the pipeline is being reshaped: stop.

## Out of scope
HTTP / Streamable HTTP transport, authentication, the hosted tier (ADR 0032), progress
notifications, MCP resources and prompts, writing SARIF to disk from the server, any change to
verdicts.

## Notes
