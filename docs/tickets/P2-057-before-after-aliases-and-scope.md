# P2-057 `--before`/`--after` aliases, and the docs say any runtime pair
Status: todo
Effort: S
Model: Sonnet, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-055, P2-056

## Goal
Once P2-055 and P2-056 land, `equiv` handles migrations, version upgrades and same-runtime commits.
Its surface still says ".NET Framework 4.8 → .NET 10" everywhere. Add the aliases ADR 0040
decision 4 names, and correct the scope wording in the places ADR 0040's Consequences list.

## Spec references
ADR 0040 decisions 4 and 5, Consequences.

## Acceptance criteria (all must hold; nothing beyond them)
1. `compare` accepts `--before` as an alias of `--legacy` and `--after` as an alias of `--modern`.
   Giving both spellings of one option is a usage error, and `--help` lists the aliases.
2. The MCP tools' `legacy` and `modern` parameter descriptions (`src/Equiv.Cli/EquivTools.cs`) say
   "the solution before the change" and "the solution after it". The parameter names do not change.
3. README's scope line says: any two C# solutions on .NET Framework 4.x or .NET 3.0 and later, whether
   a migration, a version upgrade or a same-runtime change. The line about Java stays as it is.
4. The header text of `api-equivalences.json` says that entries equate members on any runtime that
   has both, and that they apply to the before side.
5. `grep -rn "4.8" docs/VERIFICATION-MODEL.md docs/ARCHITECTURE.md README.md` finds only historical or
   example text, and each remaining hit is justified in the PR description.

## Files
`src/Equiv.Cli/CompareCommand.cs`, `src/Equiv.Cli/EquivTools.cs`, `README.md`,
`src/Equiv.Core/ApiEquivalences/api-equivalences.json` (header only), `docs/VERIFICATION-MODEL.md`,
`docs/ARCHITECTURE.md`, tests.

## Tests
`CompareCommandTests.BeforeAfterAreAliases`, `CompareCommandTests.BothSpellingsIsAUsageError`,
`EquivToolsTests.ParameterDescriptionsSayBeforeAndAfter`.

## Size guard
Renaming SARIF fields, `IFrontend` parameters or MCP parameter names is ADR 0040's rejected
alternative: stop.

## Out of scope
The corpus (P2-058).

## Notes
