# callee-replacement

A member the new runtime lacks is replaced, call for call, by a member of the solution's own that the user declares
does the same (ADR 0058, ticket P2-192). It is the shape of `duplicati-3124`'s largest false-positive group. The legacy
side keeps a culture name in the remoting `CallContext`, through `CallContext.LogicalSetData(string, object)` and
`CallContext.LogicalGetData(string)`, which .NET 5 and later do not have. The modern side calls `Slots.SetData` and
`Slots.GetData`, its own shim over `AsyncLocal<object>`.

`equiv.config.json` declares the two pairs in `callIdentityRenames`: each legacy call to a remoting member is the same
function as a modern call to its shim member. The tests run the sample with that config.

- `Culture.Current()` and `Culture.Enter(string)` make the same calls with the same arguments on both sides, so with the
  declaration each pair is Equivalent. Nothing in the run proves the declaration: the remoting member has no body, and
  the shim is Added. So each result lists the remoting member in `assumedCallees` and `unprovenAssumptions`, its message
  says so, and `properties.callIdentitiesMapped` names the shim member it was declared to be. Without the config both
  pairs are Divergent, since the two callees are two unrelated functions.
- `Culture.Leave()` stores `null` on the legacy side and `string.Empty` on the modern side. The declaration makes the
  two calls one function, not one value, so the different argument keeps the pair Divergent.
- `Slots.SetData(string, object)` and `Slots.GetData(string)` exist only on the modern side, so each is Added. Their bodies
  are in the report to review, which is what the declaration asks the reader to trust.

## Expected verdicts

| Procedure | Verdict | `unprovenAssumptions` |
|---|---|---|
| `Culture.Current()` | Equivalent | `CallContext::LogicalGetData(string)` |
| `Culture.Enter(string)` | Equivalent | `CallContext::LogicalSetData(string,object)` |
| `Culture.Leave()` | Divergent | `CallContext::LogicalSetData(string,object)` |
| `Slots.SetData(string, object)` | Added | |
| `Slots.GetData(string)` | Added | |

Exit code: 1 (a new Divergent result).
