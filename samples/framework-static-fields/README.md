# framework-static-fields

A framework `static readonly` field of a value or string type has one value for the whole run (ticket P2-250). Each
pair here reads one the same way on both sides and differs only in where the read sits around a call, or in spelling
`string.Empty` as `""`. Before P2-250 such a read was a `field.*` map every call wrote and whose value was free, so a
read moved past `Audit.Record` was another value, and `string.Empty` was not `""`: each pair was Divergent. Now
`string.Empty`, `IntPtr.Zero` and the other fields `FrameworkStatics` knows are their constant, and any other one, such
as `TimeSpan.MaxValue`, is an input no call writes, shared by both sides by name.

## Expected verdicts

| Procedure | Verdict |
|---|---|
| `Audit.Record(string)` | Equivalent: the same body on both sides |
| `Labels.Describe(string)` | Equivalent: `string.Empty` is read before the call on one side and after it on the other |
| `Labels.Trimmed(string)` | Equivalent: `string.Empty` against `""` |
| `Labels.Release(string)` | Equivalent: `IntPtr.Zero` is read before the call on one side and after it on the other |
| `Labels.Forever(string)` | Equivalent: `TimeSpan.MaxValue`, which no catalogue knows, read before and after the call |
| `Labels.Blank(string)` | Divergent: `string.Empty` against `" "`, an edit the known value does not hide |

Exit code: 1 (a new Divergent result).
