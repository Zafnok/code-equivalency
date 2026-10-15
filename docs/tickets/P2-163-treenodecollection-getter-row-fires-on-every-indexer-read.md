# P2-163 Precision bug: the `TreeNodeCollection::get_Item(` row fires on every read of the indexer
Status: in-progress
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
- Decision: where the null-argument change goes -> folded into the existing `TreeNodeCollection::set_Item(` row, whose
  reason now names both net6.0 changes; `docs/runtime-changes-review.md` traces both pages to it. Alternatives: a second
  `set_Item(` row (`EveryRowHasASource` keeps `(member, changedIn)` unique, and `TryMatch` reports the first matching row
  only, so the second would never be named), a precondition on the existing row (no precondition can say "null", and
  the other-TreeView change is reached by non-null nodes, so narrowing the row to null would drop it). Rule: 1.
- Deviation: the ticket's Files list only the table, but criterion 1 also needed a frontend fix. With the row moved to
  `set_Item(` the sample was still not congruent: `BoundSerialiser` rendered a property reference as both accessors and
  flagged a row on either, so a read of the indexer was kept out of congruence by the setter's row
  (`notCongruent: runtime-rule TreeNodeCollection::set_Item(...)`, then Equivalent by the bounded rung). It now flags only
  the accessor the reference runs; the fingerprint text is unchanged. The lowered body already called only the getter.
  VERIFICATION-MODEL section 3 states the rule.
- Out of scope (other null-argument rows on a getter): none. The other rows whose member is a getter and whose reason
  mentions null (`Socket::get_LocalEndPoint(`, `ProcessStartInfo::get_Environment`/`get_EnvironmentVariables(`,
  `BindingSource::get_SortDescriptions(`, `PublicKey::get_EncodedParameters(`) describe a changed result, which a read
  does reach.
- The sample is the first Windows Forms pair. Its projects set `EnableWindowsTargeting` so Ubuntu (the parity leg)
  restores the Windows Desktop targeting pack, and `IlSamples` gains three stub types (`TreeView`, `TreeNodeCollection`,
  `TreeNode`), as it has for the Web API types, since the Frontend tests compile sample sources against their own
  framework.
