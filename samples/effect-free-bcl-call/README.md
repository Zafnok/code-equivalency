# effect-free-bcl-call

A BCL member that runs no observable code is not a call (ADR 0043, ticket P2-071). Before that ADR
each of these was an extra trace event that could throw, and a false Divergent.

- `Lengths.First(string, string)`: the modern side reads `b.Length` once more and discards it.
  `String.Length` is in the effect-free catalogue, so the read is a pure function of `b` behind the
  null check the guard has already made. Neither side has a call. Equivalent.
- `Basket()`: the legacy side stores a new `List<int>` in an `IList<int>` field, the modern side a
  new `Collection<int>`. Both parameterless constructors are in the catalogue, so neither is a
  call, and a new `Collection<T>` converted to an interface at once is a new `List<T>` there. Both
  sides leave the same heap. Equivalent. This rests on the assumption VERIFICATION-MODEL section 1
  names: no code asks the stored object for its concrete type.
- `Lengths.Reset(string, List<int>)`: the modern side calls `list.Clear()`, which has an effect and
  is not in the catalogue. It is an ordinary call that only the modern side makes, on a list that
  only the modern side dereferences. Divergent.
- `Basket.Items` is the same source on both sides. Equivalent by congruence.

## Expected verdicts

| Procedure | Verdict |
|---|---|
| `Lengths.First(string, string)` | Equivalent |
| `Basket()` | Equivalent |
| `Basket.Items` | Equivalent (congruence) |
| `Lengths.Reset(string, List<int>)` | Divergent: the modern side calls `List<int>.Clear()` |

Exit code: 1 (a new Divergent result).
