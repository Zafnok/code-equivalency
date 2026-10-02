# repeated-edit

One edit made in several places, and one change on its own (ticket P2-064). The migration swaps
`Convert.ToInt32(string)` for `int.Parse(string)` in three methods: the two differ on a null string,
where `Convert.ToInt32` returns 0 and `int.Parse` throws `ArgumentNullException`, and the solver
treats them as different members. A fourth method, `Discount`, changes `>` to `>=` and calls
nothing.

All four are Divergent, and a reviewer has two things to look at, not four. Each result carries the
group it belongs to in `properties.reviewGroup` and the group's `rank`, and the run lists the groups
in `run.properties.reviewList`, highest rank first (VERIFICATION-MODEL.md section 6):

```
review list: 2 groups for 4 flagged results
  EQ002 count=3 rank=60.006 calls:System.Convert::ToInt32(string)|System.Int32::Parse(string)
  EQ002 count=1 rank=60.002 proofMethod:none
```

The three swapped methods share a group, because the same two call identities differ between their
traces. Both groups are in the same tier, and the larger one ranks higher.

## Expected verdicts

| Procedure | Verdict | `reviewGroup` | `rank` |
|---|---|---|---|
| `Orders.Quantity(string)` | Divergent | `calls:System.Convert::ToInt32(string)\|System.Int32::Parse(string)` | 60.006 |
| `Orders.Total(string, int)` | Divergent | the same | 60.006 |
| `Orders.IsBulk(string)` | Divergent | the same | 60.006 |
| `Orders.Discount(int)` | Divergent (on `total = 1000`) | `proofMethod:none` | 60.002 |

Exit code: 1 (new Divergent results).
