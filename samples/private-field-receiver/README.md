# private-field-receiver

Legacy's private `Account.Label()` throws `InvalidOperationException` when the account has no owner; modern drops the
check, so the same state reaches `owner.ToUpper()` and throws `NullReferenceException`. The method is private, it reads
two fields, and `Account` has no public parameterless constructor: before ADR 0062 a replay could not call it at all
(`not public (private)`), nor build its receiver (ticket P2-245). Under `--execute` the replay driver binds `Label` by its
exact signature on both sides and builds the receiver without a constructor, with the model's `owner = null` and its
`balance`, so the Divergent is `reproduced` and `replayInputs` says the receiver is `uninitialized Account.balance,
Account.owner`. Execution confirms the model, it does not prove it: whether a constructor of `Account` reaches a null
owner is a hand trace's question (ADR 0062 decision 5). Here it does, since `new Account(null, 0)` stores the null.

## Expected verdicts

| Procedure | Verdict |
|---|---|
| `Account.Account(string, int)` | Equivalent: the two constructors store the same fields |
| `Account.Label()` | Divergent: the model's `owner` is null (legacy throws `InvalidOperationException`, modern throws `NullReferenceException`) |

Exit code: 1 (a new Divergent result).
