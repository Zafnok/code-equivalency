# P2-089 ARCHITECTURE.md's `equiv compare` synopsis no longer lists `--bound` and `--timeout-ms`, which the command never had
Status: todo
Effort: S
Model: Sonnet, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-050

## Goal
`docs/ARCHITECTURE.md`'s `Equiv.Cli` section gives the `equiv compare` synopsis with
`[--bound 3] [--timeout-ms 5000]`. `CompareCommand.Create` defines neither option, so
`equiv compare --bound 3` is a parse error. `CompareOptions.Bound` and `CompareOptions.TimeoutMs` are
set only by the MCP `compare` tool (`EquivTools.Compare`, arguments `bound` and `timeoutMs`) and are
otherwise read from `equiv.config.json` (`bound`, `timeoutMs`). The doc comment on `EquivConfig` repeats
the mistake: "ARCHITECTURE.md's `--bound`/`--timeout-ms` CLI defaults". P2-050 then moved the default
`timeoutMs` from 5000 to 60000, so the 5000 in the synopsis is wrong twice. `README.md`'s usage block
already omits both options and is correct.

Correct the two places. No option is added: no ticket in `docs/ROADMAP.md` asks for `--bound` or
`--timeout-ms`, and the config file and the MCP tool already set both values. Two lines of
documentation and one doc comment; no behaviour changes.

## Spec references
`docs/ARCHITECTURE.md` (`Equiv.Cli`), `README.md` ("Usage"; the MCP `compare` tool's arguments),
VERIFICATION-MODEL.md section 3 (`equiv.config.json`), `src/Equiv.Cli/CompareCommand.cs` (`Create`, `Run`),
`src/Equiv.Cli/EquivTools.cs` (`Compare`), ticket P2-050 (`--resource-limit`, `timeoutMs` 60000).

## Acceptance criteria (all must hold; nothing beyond them)
1. The `equiv compare` synopsis in `docs/ARCHITECTURE.md` lists neither `--bound` nor `--timeout-ms`.
   Every option it does list is one `CompareCommand.Create` defines, and every option `Create` defines
   is in it, `--config`, `--verbosity` and `--log` included if they are missing. The synopsis and
   `README.md`'s usage block name the same set of options.
2. `docs/ARCHITECTURE.md`'s `Equiv.Cli` section says in one bullet where the two values come from:
   `bound` (default 3) and `timeoutMs` (default 60000) are keys of `equiv.config.json`, and the MCP
   `compare` tool's `bound` and `timeoutMs` arguments override them. The defaults written there are
   the ones in `EquivConfig.Default`.
3. The `<summary>` on `EquivConfig` (`src/Equiv.Core/Configuration/EquivConfig.cs`) no longer mentions
   `--bound` or `--timeout-ms`. It says the record is the parsed and defaulted `equiv.config.json`
   (VERIFICATION-MODEL.md section 3). The rest of the comment is unchanged.
4. `--bound` and `--timeout-ms` appear nowhere in the repository outside `docs/tickets/` and
   `docs/runs/` (`git grep -nE -e '--bound|--timeout-ms' -- . ':!docs/tickets' ':!docs/runs'` prints
   nothing).
5. No file under `src/` changes other than the one comment, and no test changes.

## Files
`docs/ARCHITECTURE.md`, `src/Equiv.Core/Configuration/EquivConfig.cs` (the `<summary>` only),
`docs/ROADMAP.md` (the P2-089 line, marked done).

## Tests
None. No behaviour changes; the existing `CompareCommandTests` and `McpCommandTests` already cover the
config and MCP precedence.

## Size guard
More than three files changed, or any change to `CompareCommand.cs`, `CompareOptions.cs` or
`README.md`, means the ticket has been misread.

## Out of scope
Adding `--bound` or `--timeout-ms` to `equiv compare`. If a later ticket needs them, they follow
`--resource-limit`: an `Option<int?>` that sets the `CompareOptions` property, exit 3 when not positive,
command line over config. That is its own ticket. Rewording any other part of ARCHITECTURE.md's
`Equiv.Cli` section. P2-050's own ticket text, which says `--timeout-ms` where it means `timeoutMs`:
a ticket in `done/` is a record and is not edited.

## Notes
- Depends on P2-050 (PR #328) because that PR edits the same synopsis line and the same record, and
  sets the 60000 default that criterion 2 writes down. Branch after it merges.
