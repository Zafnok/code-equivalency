# P2-225 Precision: a migration that sets `UseShellExecute` to .NET Framework's default is still EQ006
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
P2-216 (done) stopped the `Process::get_StartInfo(` row firing on a process the method creates with `new`. Its rerun
shows the result it was filed for, `gitextensions-8522`'s
`ResourceManager.LinkFactory::ExecuteLink(string,System.Action<global::ResourceManager.CommandEventArgs>,System.Action<string>)`,
still EQ006, now on the `System.Diagnostics.Process::Start(` row (`defaultShellExecute`):
- legacy (.NET Framework 4.6.1) creates `new Process { EnableRaisingEvents = false, StartInfo = { FileName = uri } }`
  and calls `process.Start()`, so it starts the URL with `UseShellExecute`'s .NET Framework default, `true`;
- modern (.NET 5) adds `UseShellExecute = true` to the same initializer, the migration's documented fix for the new
  default, `false`.

Both start the URL through the shell on their own runtime, so the pair is equivalent. The row fires on the legacy call
because that call does read the default, and `defaultShellExecute` exempts only `Process.Start(ProcessStartInfo)` given
a `ProcessStartInfo` that sets the property, not the instance `Start()` of a new `Process` whose initializer sets it.

When done, a call whose only runtime-changed input is `UseShellExecute`'s default is proved equivalent to one that sets
the property to the default of the runtime the other side runs on, or `equiv-adr` records why that is not taken.

## Spec references
`src/Equiv.Core/RuntimeChanges/runtime-changes.json` (`defaultShellExecute`), `docs/tickets/done/P2-190-*.md` (the
exemption and its soundness argument), `docs/tickets/done/P2-216-*.md`, ADR 0040 (runtimes are detected per side).

## Acceptance criteria (all must hold; nothing beyond them)
1. The instance `Process.Start()` of a `new Process` whose initializer sets `StartInfo`'s `UseShellExecute` and not
   `WindowStyle` is exempt from the row, as `Process.Start(ProcessStartInfo)` is (P2-190's argument).
2. `equiv-decide` records how a side's default reaches the comparison (a value per side's runtime, or a rewrite of the
   legacy call), with its soundness argument; the chosen one is implemented.
3. A sample with the `ExecuteLink` shape, Equivalent, and one whose modern side sets `UseShellExecute = false`,
   Divergent.
4. `gitextensions-8522` rerun in compare mode quick; Notes give `ExecuteLink`'s new result and every other result that
   moved.

## Tests
The row-precondition tests; the sample.

## Size guard
Any other runtime default modelled per side: stop and file it.

## Out of scope
`WindowStyle`, which the shell ignores on .NET Framework and .NET honours without it.

## Notes
- Filed 2026-10-15 by P2-216 (its rerun: the result's remaining cause, which no ticket owned).
