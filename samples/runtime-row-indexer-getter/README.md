# runtime-row-indexer-getter

A version upgrade between two .NET versions of a Windows Forms project: the legacy side targets .NET 5 and the modern
side .NET 6 (ticket P2-163). `Nodes.cs` is byte-identical on both sides.

- `First` reads `t.Nodes[0].Text`. .NET 6 throws `ArgumentNullException` where .NET 5 threw `NullReferenceException`
  when a null node is assigned through `TreeNodeCollection`'s indexer, and when a node that belongs to another
  `TreeView` is assigned. Both changes are in the setter, which the row `System.Windows.Forms.TreeNodeCollection::set_Item(`
  (`changedIn: net6.0`) names. Reading the indexer takes an `int`, which cannot be null, and runs only the getter, so the
  row does not apply and the pair is congruent.

## Expected verdicts

| Procedure | Verdict | `proofMethod` |
|---|---|---|
| `Nodes.First(TreeView)` | Equivalent | `congruence` |

Exit code: 0 (no Divergent or Unknown result).

## The same method assigning null through the indexer

`null-assignment/` is the pair with `t.Nodes[0] = null;` before the read. The setter runs, so the row fires and the
method is a runtime exposure with a witness (EQ007, ADR 0060) under the null-argument change. The integration test
`RuntimeRowIndexerGetter_ANullAssignmentThroughTheIndexerIsEQ006` runs that pair; it has no snapshot.
