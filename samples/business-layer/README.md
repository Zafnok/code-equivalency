# business-layer

Typical service-layer code (ADR 0027 decision 5): an `OrderService` over two plain model types,
`Order` and `OrderLine`. It is the ratchet for the precision tickets. Its lowering census is
snapshotted by `LoweringCensusTests.BusinessLayerCensusSnapshot` (ticket M3-014), and every
precision ticket must move at least one method below to its target verdict.

Most methods are unchanged between legacy and modern. Three are cosmetic refactors, and one is a
real divergence: `Math.Round(total, 2)` rounds half to even, while
`Math.Round(total, 2, MidpointRounding.AwayFromZero)` rounds half away from zero.

## Expected verdicts

"Today" is the verdict on `main` when M4-002 merged. "Unlocked by" is the ticket that moved, or is
expected to move, the method to its target verdict. Since M3-015 an unchanged method is Equivalent
by congruence (`proofMethod: congruence`) before the solver can prove it, so the last column names
the ticket that lowers its construct. Since M3-016 every Unknown points at the construct that caused it: its
primary location is that line on the modern side, and every cause is a `relatedLocation`.

`Describe` is unchanged in source but not in binding: its interpolated string binds `string.Format`
on .NET Framework 4.8 and `DefaultInterpolatedStringHandler` on .NET 10. Its bound fingerprints
differ, so it is not congruent (ADR 0024), and it goes to the solver, where the interpolated string
is opaque.

Since M4-002, `decimal` arithmetic is a set of pure functions both sides share (ADR 0025). `LineTotal` moves
`UnitPrice * Quantity` into a local without changing it: both sides apply `conv.i32.dec`, `dec.mul` and `dec.sub` to
the same values, so the solver proves it Equivalent without modelling `decimal`. Its `Binary` and `Conversion` opaques
leave the census.

Congruent results that call another matched procedure list it in `properties.assumedCallees`
(ADR 0019). Every such callee here is itself Equivalent, so no result has `unprovenAssumptions`.

| Procedure | Construct | Change | Today | Target verdict | Unlocked by | Construct lowered by |
|---|---|---|---|---|---|---|
| `OrderService.CustomerName(Order)` | property read | unchanged | Equivalent (congruence) | Equivalent | M3-015 | M3-010 |
| `OrderService.Subtotal(Order)` | `foreach` over `List<T>`, `decimal` arithmetic | unchanged | Equivalent (congruence) | Equivalent | M3-015 | M4-001; its `decimal` `+=` is still opaque (`CompoundAssignment`), M4-002 lowers only the operators |
| `OrderService.SkusOver(Order, int)` | LINQ chain with lambdas | unchanged | Equivalent (congruence) | Equivalent | M3-015 | M4-004 |
| `OrderService.ParseQuantity(string)` | `int.TryParse(s, out var n)` | unchanged | Equivalent (congruence) | Equivalent | M3-015 | M4-003 |
| `OrderService.ConfirmAsync(Task<Order>)` | `async`/`await` | unchanged | Equivalent (congruence) | Equivalent | M3-015 | M4-006 |
| `OrderService.QuantityOf(object)` | `is T t` pattern | unchanged | Equivalent (congruence) | Equivalent | M3-015 | M4-005 |
| `OrderService.Describe(Order)` | interpolated string | binding only | Unknown | Equivalent | none yet: not congruent, see above | none yet |
| `OrderService.Export(Order)` | `using` | unchanged | Equivalent (congruence) | Equivalent | M3-015 | M4-001 |
| `OrderService.Record()` | `lock` | unchanged | Equivalent (congruence) | Equivalent | M3-015 | M4-003 |
| `OrderService.IsLarge(int)` | integer comparison | unchanged | Equivalent (congruence) | Equivalent | M3-001 | M3-001 |
| `OrderService.TotalQuantity(Order)` | `foreach` over `List<T>` | renamed local | Equivalent (congruence) | Equivalent | M3-015 (local names are not in the fingerprint) | M4-001 (with M3-010) |
| `OrderService.LineTotal(OrderLine)` | `decimal` arithmetic | extracted variable | Equivalent (bounded) | Equivalent | M4-002 | M4-002 (with M3-010) |
| `OrderService.Reserve(Order, int)` | guard `throw new ArgumentNullException` | inverted guard | Equivalent (bounded) | Equivalent | M3-010 | M3-010 (the `order == null` conversion) |
| `OrderService.RoundTotal(decimal)` | `Math.Round` overloads | real divergence | Divergent | Divergent | M3-001 | stays Divergent through M4-002: `Math.Round` is a call, not a pure function, so the divergence is untainted (M3-016) |
| 14 auto-property accessors of `Order` and `OrderLine` | auto-property | unchanged | Equivalent (congruence) | Equivalent | M3-015 | M3-010 |

Exit code: 1 (`RoundTotal`'s Divergent result; `Describe`'s Unknown does not by itself change the exit code).
