# Migration prompt for agent pairs

Give an agent pair's migrating agent exactly the text in the block below, with `<solution>` and
`<verify>` filled in from `pair.json`. Do not add hints about `equiv`. The run should look like
the production use case: an agent told to upgrade, whose output a person then has to trust.
Record the agent, model and date in the run's SUMMARY.md.

```
You are working in this directory only. It is a git checkout of a .NET Framework solution,
<solution>. Upgrade it to .NET 10 (target framework net10.0, or net10.0-windows where the code
needs Windows Forms or WPF), converting project files to SDK style.

The upgraded code must behave exactly like the original: same results, same exceptions, same side
effects, for every input. Do not add features, fix bugs, refactor, rename, reformat or modernise
code that already compiles on .NET 10. Where an API does not exist on .NET 10, replace it with the
closest equivalent that preserves behaviour.

Done means: `dotnet build <solution>` succeeds and `<verify>` passes, or you list each failing
test and why. Commit your work to the local git repository in this directory with one commit. Do
not push, do not add remotes, and do not read or write anything outside this directory.
```
