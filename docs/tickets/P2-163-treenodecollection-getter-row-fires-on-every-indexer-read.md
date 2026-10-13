# P2-163 Precision bug: the `TreeNodeCollection::get_Item(` row fires on every read of the indexer
Status: todo
Effort: S
Model: Sonnet, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-075

## Goal
`runtime-changes.json` holds the row `System.Windows.Forms.TreeNodeCollection::get_Item(` (changedIn
`net6.0`, "A null argument throws ArgumentNullException ..."). The page it cites lists
`TreeNodeCollection.Item[Int32]`. Reading that indexer takes an `int`, which cannot be null, so no
read reaches the change. Only assigning a null node through the setter does. The row names the
getter, so every `nodes[i]` read on a net5.0-to-net6.0 pair is EQ006. Minimal repro, as a sample
pair (identical file; legacy `net5.0-windows`, modern `net6.0-windows`):

```csharp
static string First(System.Windows.Forms.TreeView t) => t.Nodes[0].Text;
```

Today this is EQ006. It should be congruent. P2-066 found the first one and P2-075 recorded it as
not done (its Notes). On P2-124's rerun of `gitextensions-9860` this row is the only source of
EQ006 left on either upgrade pair: 4 results, all false positives
(`docs/runs/2026-10-12-upgrade-verdict.md`).

Make the row say what the page says: the change is reached by `set_Item(` with a null value. The
file already has a `set_Item(` row for a different net6.0 change (a node that belongs to another
`TreeView`). Decide through `equiv-decide` whether the null-argument change becomes a second row on
`set_Item(` or a precondition on the existing one, and log the `Decision:` line.

## Spec references
VERIFICATION-MODEL section 3 (runtime-changed APIs), `runtime-changes.json`, P2-075 (rows match only
affected members) and its Notes, P2-073 (a row precondition on the call's arguments).

## Acceptance criteria (all must hold; nothing beyond them)
1. The sample pair above has no EQ006 and is congruent.
2. The same pair with `t.Nodes[0] = null;` in the body is EQ006 under the null-argument change.
3. No row in `runtime-changes.json` names `TreeNodeCollection::get_Item(`.

## Tests
Integration test on the sample, both variants. The table test that checks every row's member
resolves still passes.

## Out of scope
Other "null argument" rows whose member is a getter: list any you find in `## Notes`.

## Notes
- Found by P2-124: 4 EQ006 on `gitextensions-9860`, in
  `GitUI.CommandsDialogs.RevisionFileTreeController::Find(System.Windows.Forms.TreeNodeCollection,string)`,
  `GitExtensions.UITests.CommandsDialogs.FormBrowse_LeftPanel_ReorderNodesTest::ValidateOrder(System.Collections.Generic.List<global::System.Windows.Forms.TreeNode>,System.Windows.Forms.TreeNodeCollection,int[])`,
  `BugReporter.ExceptionDetails::Initialize(BugReporter.Serialization.SerializableException)` and
  `BugReporter.ExceptionDetails::FillInnerExceptionTree(BugReporter.Serialization.SerializableException,System.Windows.Forms.TreeNode)`.
  Each reads the indexer and none assigns through it.
