# M5-001 `equiv mcp`: the compare pipeline as an MCP server over stdio
Status: in-progress
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
Decision: branch is the worktree's own `claude/mcp-server-m5-001-1baacd` (the app made it from `main`), not `M5-001-mcp-server`.
Decision: `CompareCommand.Run`'s stdout and stderr lines go through a `Streams(Out, Error)` record on `CompareOptions` (null means the console, read when `Run` starts), not two more `Run` parameters (Sonar S107). The `mcp` tools pass stderr as `Out` and a `StringWriter` as `Error`; the captured text becomes the tool error's message and is then copied to stderr.
Decision: `bound` and `timeoutMs` are `CompareOptions.Bound`/`TimeoutMs` overriding the loaded config's `Bound`/`TimeoutMs`. Non-positive is exit 3 with `error: bound and timeoutMs must be positive integers`, so the tool reports it as a tool error and a future CLI option gets the same check. (ARCHITECTURE.md lists `--bound`/`--timeout-ms` but `compare` has no such options today; the config file's `bound`/`timeoutMs` are the existing meaning.)
Decision: a run that wrote a SARIF log is a normal tool result whatever its exit code (a skipped C# project is exit 4 with a log and says `exit code 4` in the summary); a run with no log (exit 3, or 4 from a frontend load failure) is a tool error. An exception that escapes `Run` is a tool error with its message, the analogue of `Program`'s exit 5.
Decision: summary counts come from the log's rule ids (EQ001 equivalent, EQ002/EQ006 divergent, EQ003 unknown); skipped projects are the tool-execution notifications without an exception (pair failures carry one, ADR 0023).
Decision: the server version is the assembly's `AssemblyInformationalVersion` (MinVer's, including the `+sha` build metadata).
Decision: `Streams` is its own file because MA0048 wants file name = type name; three new files under `src/` (McpCommand, EquivTools, Streams), under the size guard's four. `McpCommand.ProcessStreams` (the process's real stdin/stdout) carries `[ExcludeFromCodeCoverage]` naming this ticket; `McpIntegrationTests` runs it for real.
Toolchain: `McpServer.RunAsync` reads its input synchronously before returning a task, so a test that starts `ServeAsync` on the test thread deadlocks; `McpCommandTests` starts it with `Task.Run`. A pipe-based test client must also dispose its own write end of the pipe, or the server never sees EOF (the SDK's `StreamClientTransport` does not).
Environment: `Mcp_Compare_MatchesCliSarif` passes for 11 of the 12 samples on this dev box. `webapi-basic` fails here, as the existing `SamplesEndToEndTests` does for it before this change (no SARIF is written for it), so it is not caused by this ticket; the Windows CI leg has the VS Build Tools it needs.
