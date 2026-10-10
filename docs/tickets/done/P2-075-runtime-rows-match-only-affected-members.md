# P2-075 A runtime-change row matches only the overloads and members its change affects
Status: done (PR #457)
Effort: S
Model: Sonnet, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-047

## Goal
P2-047's audit found 2 false EQ006 results where a row matched a member its documented change does
not touch. `s.IndexOf("x.exe", StringComparison.OrdinalIgnoreCase)` was flagged under the ICU
culture-comparison row, although ordinal comparisons do not use ICU. A method that only enumerates
`ListView.Groups` was flagged under the row about adding a group that another `ListView` already owns.
Minimal repro, as a sample pair (identical file, legacy net48):

```csharp
static bool Has(string s) => s.IndexOf("x.exe", System.StringComparison.OrdinalIgnoreCase) >= 0;
```

Today this is EQ006. It should be Equivalent. Tighten the two rows' member matching: the ICU row
excludes the overloads that take `StringComparison.Ordinal` or `OrdinalIgnoreCase` as a constant,
and the `ListViewGroup` row names only `Add` and `Insert`.

## Spec references
VERIFICATION-MODEL section 3 (runtime-changed APIs), `runtime-changes.json`, P2-047's audit.

## Acceptance criteria (all must hold; nothing beyond them)
1. `samples/runtime-row-unaffected-overload` (the pair above) is Equivalent. The same call with
   `StringComparison.CurrentCultureIgnoreCase` stays EQ006.
2. A test asserts that the `ListViewGroup` row matches `Add` and `Insert` only.

## Tests
- Integration test on `samples/runtime-row-unaffected-overload`.
- Unit test on the row's member match.

## Out of scope
An audit of every other row's member list.

## Notes
- Found by P2-047: 2 on Git Extensions.
- P2-066 found 2 more on `gitextensions-9860` (net5.0 to net6.0): `GitUI.FileStatusList::SelectNextVisibleItem()`
  enumerates `ListView.Groups` under the same `ListViewGroupCollection::` row, and
  `GitUI.CommandsDialogs.RevisionFileTreeController::Find(System.Windows.Forms.TreeNodeCollection,string)` reads the
  getter `TreeNodeCollection::get_Item(int)` under the `net6.0` row about null arguments. The page lists
  `TreeNodeCollection.Item[Int32]`; an `int` index cannot be null, so only assigning a null node through the setter
  reaches the change, and the row names `get_Item(` instead of `set_Item(`.
- Decision: the ordinal exclusion is a call-site fact, not part of the callee's identity, so a row carries `ordinalUnaffected: true`
  and `RuntimeChangeTable.TryMatch` takes an `ordinalComparison` flag; the frontend sets it when the invocation passes a constant
  `StringComparison.Ordinal` or `OrdinalIgnoreCase` (`OrdinalComparison`). The flag marks the eight `System.String` culture rows
  (`IndexOf`, `LastIndexOf`, `StartsWith`, `EndsWith`, `Compare`, `CompareTo`, and the two measured `Equals(..., StringComparison)`
  rows). The identity string, and so the uninterpreted function, is unchanged.
- Decision: both lowerings of a body that is read for a runtime flag apply it: the IOperation lowering (`IrLowerer.Bound`) and the
  bound fingerprint (`BoundSerialiser`). A call to a forwarder is resolved to its target, whose comparison argument is the
  forwarder's parameter, so it stays flagged. The IL fallback lowering has no operand constants and stays flagged too (conservative).
- The `ListViewGroupCollection::` row is two rows, `Add(` and `Insert(`; `AddRange(` is not in the documented change.
- Not done (ticket criteria stop at the two rows; Out of scope): the `TreeNodeCollection::get_Item(` finding from P2-066 above, and
  `LoweringCensus`'s `runtimeChangeCalls`, which still counts an ordinal call as a table match (it also ignores suppression).
- `samples/runtime-row-unaffected-overload` also holds `HasCulture`, the criterion-1 call with `CurrentCultureIgnoreCase`, so the
  sample's exit code is 1 and both verdicts are in its snapshot.
- Locally the SDK-style samples that target net8.0/net9.0 (`version-bump`, `params-span-overloads`, `webapi-*`,
  `runtime-row-framework-only-change`) fail to load on this box (no targeting pack), as they do without this change; the new sample,
  Core, Frontend and Architecture tests pass.
