# P2-075 A runtime-change row matches only the overloads and members its change affects
Status: todo
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
