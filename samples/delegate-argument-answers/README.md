# delegate-argument-answers

Legacy's `Pricing.Total` asks the `price` delegate for a unit price and throws `ArgumentOutOfRangeException` when it is
negative; modern drops the check and returns `unit * count`. The model's runs call `price`, and its answer to that call
is a negative number. Before ticket P2-249 a replay could not build `price` (`the model's runs call price, and the
counterexample holds none of the model's answers to that call`). Now the counterexample carries the model's answer to
each call of a delegate input, and under `--execute` the replay builds `price` as a function that answers the model's
argument with the model's result, so legacy throws, modern returns, the Divergent is `reproduced` on .NET Framework 4.8
and .NET 10, and `replayInputs` says `price` is a `delegate` (ADR 0062 decision 3).

## Expected verdicts

| Procedure | Verdict |
|---|---|
| `Pricing.Total(Func<int, int>, int)` | Divergent: the model's `price` answers a negative unit price (legacy throws `ArgumentOutOfRangeException`, modern returns the product) |

Exit code: 1 (a new Divergent result).
