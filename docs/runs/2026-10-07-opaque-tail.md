# P1-028 the opaque tail: reasons no ticket owns, over the three large runs (2026-10-07)

Question: once every ticket that owns an opaque reason has landed, which reasons still keep changed
pairs opaque on the three large runs, how many pairs does each one unlock, and how far can IR
coverage take the lowerable share?

**Answer: the open owners take the lowerable share of the 2,246 changed pairs from 34.1% to 55.6%,
and the reasons no ticket owns hold the other 44.3%. Two of them are large, not a tail:
`DelegateCreation` unlocks 257 pairs (11.4%) and `rebound-call` 191 (8.5%), and each has only an
owner that is already done.** Five more unlock at least 1% each: `DefaultValue` 73,
`InterpolatedString` 59, `CaughtException` 51, `CompoundAssignment` 35 and `iterator` 27. Six
tickets are filed, P2-136 to P2-141; `iterator` is the seventh and falls to the cap. With every
reason lowered the share is 99.9%: three pairs hold a reason that is opaque by decision.

The ticket expected a tail in which each reason is small and pairs hold several at once. The second
half holds less than expected: of the 997 changed pairs that hold an unowned reason, 781 are
unlocked by one unowned reason plus the open owners (the sum of Table 2), so single tickets do
unlock most of them.

## Setup
- No new run. The census (`run.properties.loweringCensus`: `opaqueByReason`, `changedReasonSets`) of
  the three SARIF files the ticket's figures come from, all `full`, default config, pairs from
  `tools/corpus/pairs.csv`:
  - `gitextensions-8522`, legacy 3f4ed21998af, modern 5190ba5c1a5f, run `20261002-1814-full-after`
    (P2-076's run): 951 changed pairs of 13,541 matched.
  - `gitextensions-9860`, legacy bcd0c2617bdd, modern 37797ea4dd74, run `20261003-0055-full`, equiv
    `8e0ed3c`: 722 of 14,020.
  - `jellyfin-13023`, legacy 5e8c0fe40c0e, modern ceb850c77052, run `20261003-0055-full`, equiv
    `8e0ed3c`: 573 of 14,503.
- 951 + 722 + 573 = 2,246 changed pairs, and their `opaque` Unknowns are 237 + 253 + 243 = 733, the
  ticket's two figures. The two Git Extensions runs share most of their procedures, so a body of
  that repository can count twice; the sums are over runs, as the ticket's are.
- Tool: `tools/spikes/opaque-tail/opaque_tail.py`, which reads those keys and nothing else. Its
  owner map is the ticket state of 2026-10-07.
- Definitions (ADR 0034 item 2). A changed pair's **reason set** is the union of both sides' opaque
  reasons. A reason is **in** a pair when the set holds it and **alone** when the set is exactly it.
  A pair is **unlocked** by a set of reasons when its reason set is a subset of them. All counts are
  exact, since the census records whole sets.

## Who owns a reason
A reason counts as owned when a ticket lowers the reason as a whole and is either open today or
merged after the run was made, so the run does not show its effect yet:
- `Conversion`: P2-099 and P2-123 merged after the runs (P2-123's census: 110 pairs alone became 16
  on `gitextensions-9860`), and P2-095, open, owns the conversions to `Nullable<T>` that are 13 of
  those 16.
- `switch-pattern`: P2-122, with P2-093, P2-103 and P2-104 for three forms.
- `Binary`: P2-087.
- `await-using`: P1-029, merged after the runs.
- `CollectionExpression`: P2-120, merged after the runs, and P2-128.
- `no-body`: P2-118.

A reason counts as unowned when no ticket names it, when its owner was done before the runs and
the runs still hold it (`DelegateCreation` after P2-067, `InterpolatedString` after P2-086), or
when an open ticket owns one form of it and nobody has counted the others (`DefaultValue`: P2-095
owns `default(T?)`; `InterpolatedString`: P2-102 owns an integer hole before a hole that runs code).
`rebound-call` is unowned on the same terms: ADR 0042 keeps a call that binds to another callee
opaque until an API-equivalence entry names the two callees, P2-070 added three entries and is done,
and the reason is still in 248 pairs. `DynamicInvocation` and `TypeParameterObjectCreation` are
opaque by decision and are never counted as unlocked.

## Table 1: every opaque reason
Per run: bodies holding the reason on each side (`opaqueByReason`), the changed pairs it is in, and
the changed pairs it alone keeps opaque. Ordered by the changed pairs it is in, summed. The share is
of 2,246.

| Reason | gitextensions-8522: bodies (legacy / modern), in, alone | gitextensions-9860: bodies (legacy / modern), in, alone | jellyfin-13023: bodies (legacy / modern), in, alone | Sum: bodies | Sum: in | Sum: alone | Open owner |
|---|---|---|---|---|---|---|---|
| `switch-pattern` | 1199 / 1196, 177, 74 | 1189 / 1189, 138, 49 | 991 / 991, 186, 22 | 3379 / 3376 | 501 | 145 (6.5%) | P2-122 (P2-093, P2-103, P2-104) |
| `Conversion` | 763 / 762, 65, 12 | 2019 / 2019, 259, 110 | 1015 / 1014, 138, 29 | 3797 / 3795 | 462 | 151 (6.7%) | P2-099 and P2-123 (merged after the runs), P2-095 |
| `DelegateCreation` | 275 / 271, 107, 50 | 233 / 237, 75, 22 | 197 / 197, 156, 53 | 705 / 705 | 338 | 125 (5.6%) | none (P2-067 done before the runs) |
| `rebound-call` | 194 / 194, 156, 113 | 25 / 25, 25, 25 | 69 / 69, 67, 11 | 288 / 288 | 248 | 149 (6.6%) | none (ADR 0042 keeps a rebound call opaque until a catalogue entry names it; P2-070 added three and is done) |
| `Binary` | 652 / 652, 80, 29 | 655 / 655, 51, 10 | 497 / 497, 106, 23 | 1804 / 1804 | 237 | 62 (2.8%) | P2-087 |
| `DefaultValue` | 315 / 315, 45, 14 | 355 / 355, 66, 11 | 261 / 261, 37, 1 | 931 / 931 | 148 | 26 (1.2%) | none (P2-095 owns `default(T?)` only) |
| `CaughtException` | 125 / 124, 46, 11 | 124 / 124, 35, 8 | 228 / 228, 32, 5 | 477 / 476 | 113 | 24 (1.1%) | none |
| `InterpolatedString` | 74 / 74, 21, 4 | 87 / 81, 87, 23 | 31 / 31, 4, 0 | 192 / 186 | 112 | 27 (1.2%) | none (P2-086 done before the runs; P2-102 owns one form) |
| `CompoundAssignment` | 58 / 58, 20, 9 | 59 / 59, 8, 3 | 91 / 91, 32, 2 | 208 / 208 | 60 | 14 (0.6%) | none |
| `DeconstructionAssignment` | 80 / 80, 21, 4 | 76 / 76, 14, 4 | 39 / 39, 9, 0 | 195 / 195 | 44 | 8 (0.4%) | none |
| `Tuple` | 48 / 48, 15, 6 | 48 / 48, 9, 3 | 42 / 42, 18, 2 | 138 / 138 | 42 | 11 (0.5%) | none |
| `no-body` | 0 / 0, 0, 0 | 0 / 0, 0, 0 | 30 / 30, 30, 30 | 30 / 30 | 30 | 30 (1.3%) | P2-118 |
| `iterator` | 73 / 73, 11, 11 | 73 / 73, 9, 9 | 64 / 64, 7, 7 | 210 / 210 | 27 | 27 (1.2%) | none |
| `undefined` | 25 / 25, 8, 1 | 26 / 26, 9, 0 | 13 / 13, 6, 0 | 64 / 64 | 23 | 1 (0.0%) | none |
| `call-throw-in-try` | 13 / 12, 7, 2 | 13 / 13, 3, 1 | 64 / 64, 11, 2 | 90 / 89 | 21 | 5 (0.2%) | none |
| `ArrayElementReference` | 62 / 62, 6, 4 | 60 / 60, 4, 3 | 53 / 53, 10, 0 | 175 / 175 | 20 | 7 (0.3%) | none |
| `CollectionExpression` | 0 / 0, 0, 0 | 0 / 0, 0, 0 | 0 / 20, 20, 0 | 0 / 20 | 20 | 0 (0.0%) | P2-120 (merged after the runs), P2-128 |
| `ref-argument` | 26 / 26, 1, 0 | 45 / 60, 17, 1 | 4 / 4, 1, 0 | 75 / 90 | 19 | 1 (0.0%) | none |
| `await-using` | 0 / 0, 0, 0 | 0 / 0, 0, 0 | 83 / 83, 13, 13 | 83 / 83 | 13 | 13 (0.6%) | P1-029 (merged after the runs) |
| `ImplicitIndexerReference` | 0 / 0, 0, 0 | 6 / 6, 2, 0 | 39 / 39, 9, 4 | 45 / 45 | 11 | 4 (0.2%) | none |
| `ArrayCreation` | 42 / 42, 3, 0 | 43 / 43, 3, 1 | 68 / 68, 4, 0 | 153 / 153 | 10 | 1 (0.0%) | none |
| `InstanceReference` | 374 / 374, 4, 2 | 422 / 422, 6, 4 | 31 / 31, 0, 0 | 827 / 827 | 10 | 6 (0.3%) | none |
| `rethrow` | 11 / 11, 1, 0 | 10 / 10, 1, 0 | 30 / 30, 6, 0 | 51 / 51 | 8 | 0 (0.0%) | none |
| `TranslatedQuery` | 8 / 8, 4, 0 | 8 / 8, 2, 0 | 0 / 0, 0, 0 | 16 / 16 | 6 | 0 (0.0%) | none |
| `Discard` | 7 / 8, 2, 0 | 14 / 14, 1, 0 | 16 / 16, 1, 0 | 37 / 38 | 4 | 0 (0.0%) | none |
| `Throw` | 8 / 7, 3, 0 | 7 / 7, 1, 1 | 0 / 0, 0, 0 | 15 / 14 | 4 | 1 (0.0%) | none |
| `None` | 5 / 5, 0, 0 | 3 / 3, 1, 1 | 12 / 12, 2, 1 | 20 / 20 | 3 | 2 (0.1%) | none |
| `PropertyReference` | 10 / 10, 2, 0 | 10 / 10, 0, 0 | 25 / 25, 0, 0 | 45 / 45 | 2 | 0 (0.0%) | none |
| `TypeOf` | 17 / 17, 2, 2 | 17 / 17, 0, 0 | 15 / 15, 0, 0 | 49 / 49 | 2 | 2 (0.1%) | none |
| `TypeParameterObjectCreation` | 2 / 2, 0, 0 | 0 / 0, 0, 0 | 16 / 16, 2, 0 | 18 / 18 | 2 | 0 (0.0%) | opaque by decision (P2-028) |
| `DynamicInvocation` | 1 / 1, 1, 0 | 1 / 1, 0, 0 | 0 / 0, 0, 0 | 2 / 2 | 1 | 0 (0.0%) | opaque by decision (P2-029) |
| `Unary` | 1 / 1, 0, 0 | 2 / 2, 0, 0 | 3 / 3, 1, 0 | 6 / 6 | 1 | 0 (0.0%) | none |

`undefined` is not a construct: it is the value a variable has after an opaque node may have written
it (P2-009), so it is in a pair only next to the reason that caused it, except in one.

## Table 2: marginal unlock of each unowned reason
Assumes every owned reason above is gone. A reason's marginal unlock is the changed pairs that are
then opaque because of it alone: pairs whose reason set holds it and otherwise only owned reasons.
1% of 2,246 is 22.46 pairs, so the line for a ticket is 23.

| Unowned reason | gitextensions-8522 | gitextensions-9860 | jellyfin-13023 | Sum (marginal unlock) | Share of 2246 | Sum: in | Ticket |
|---|---|---|---|---|---|---|---|
| `DelegateCreation` | 79 | 58 | 120 | 257 | 11.4% | 338 | P2-136 |
| `rebound-call` | 125 | 25 | 41 | 191 | 8.5% | 248 | P2-137 |
| `DefaultValue` | 27 | 39 | 7 | 73 | 3.3% | 148 | P2-138 |
| `InterpolatedString` | 11 | 47 | 1 | 59 | 2.6% | 112 | P2-139 |
| `CaughtException` | 25 | 18 | 8 | 51 | 2.3% | 113 | P2-140 |
| `CompoundAssignment` | 16 | 6 | 13 | 35 | 1.6% | 60 | P2-141 |
| `iterator` | 11 | 9 | 7 | 27 | 1.2% | 27 | not filed (the cap of six) |
| `DeconstructionAssignment` | 9 | 7 | 3 | 19 | 0.8% | 44 | not filed (below 1%) |
| `Tuple` | 7 | 4 | 5 | 16 | 0.7% | 42 | not filed (below 1%) |
| `ArrayElementReference` | 4 | 3 | 2 | 9 | 0.4% | 20 | not filed (below 1%) |
| `undefined` | 2 | 3 | 3 | 8 | 0.4% | 23 | not filed (below 1%) |
| `ref-argument` | 0 | 7 | 0 | 7 | 0.3% | 19 | not filed (below 1%) |
| `InstanceReference` | 3 | 4 | 0 | 7 | 0.3% | 10 | not filed (below 1%) |
| `call-throw-in-try` | 3 | 1 | 2 | 6 | 0.3% | 21 | not filed (below 1%) |
| `ImplicitIndexerReference` | 0 | 0 | 5 | 5 | 0.2% | 11 | not filed (below 1%) |
| `Throw` | 1 | 1 | 0 | 2 | 0.1% | 4 | not filed (below 1%) |
| `None` | 0 | 1 | 1 | 2 | 0.1% | 3 | not filed (below 1%) |
| `PropertyReference` | 2 | 0 | 0 | 2 | 0.1% | 2 | not filed (below 1%) |
| `TypeOf` | 2 | 0 | 0 | 2 | 0.1% | 2 | not filed (below 1%) |
| `ArrayCreation` | 0 | 1 | 0 | 1 | 0.0% | 10 | not filed (below 1%) |
| `TranslatedQuery` | 0 | 1 | 0 | 1 | 0.0% | 6 | not filed (below 1%) |
| `Discard` | 0 | 0 | 1 | 1 | 0.0% | 4 | not filed (below 1%) |
| `rethrow` | 0 | 0 | 0 | 0 | 0.0% | 8 | not filed (below 1%) |
| `Unary` | 0 | 0 | 0 | 0 | 0.0% | 1 | not filed (below 1%) |

The marginal unlocks sum to 781. The other 216 pairs that hold an unowned reason hold two or more
of them, or, in three pairs, a reason that is opaque by decision. The ten pairs of reasons that add
most beyond their two marginals:

| Two reasons landed together | Changed pairs unlocked | Of which need both |
|---|---|---|
| `DelegateCreation` + `DefaultValue` | 346 (15.4%) | 16 |
| `DelegateCreation` + `CaughtException` | 322 (14.3%) | 14 |
| `DefaultValue` + `InterpolatedString` | 143 (6.4%) | 11 |
| `CaughtException` + `InterpolatedString` | 119 (5.3%) | 9 |
| `DelegateCreation` + `rebound-call` | 455 (20.3%) | 7 |
| `InterpolatedString` + `ref-argument` | 73 (3.3%) | 7 |
| `rebound-call` + `CaughtException` | 248 (11.0%) | 6 |
| `rebound-call` + `Tuple` | 213 (9.5%) | 6 |
| `rebound-call` + `DefaultValue` | 269 (12.0%) | 5 |
| `rebound-call` + `CompoundAssignment` | 231 (10.3%) | 5 |

In order, each step landing the reason that unlocks most given the ones before it. The first row
starts from the 1,249 pairs the owners leave lowerable:

| Step | Reason | Changed pairs this step unlocks | Cumulative lowerable | Share of 2246 |
|---|---|---|---|---|
| 1 | `DelegateCreation` | 257 | 1506 | 67.1% |
| 2 | `rebound-call` | 198 | 1704 | 75.9% |
| 3 | `DefaultValue` | 95 | 1799 | 80.1% |
| 4 | `InterpolatedString` | 79 | 1878 | 83.6% |
| 5 | `CaughtException` | 87 | 1965 | 87.5% |
| 6 | `CompoundAssignment` | 51 | 2016 | 89.8% |
| 7 | `Tuple` | 35 | 2051 | 91.3% |
| 8 | `DeconstructionAssignment` | 36 | 2087 | 92.9% |
| 9 | `iterator` | 27 | 2114 | 94.1% |
| 10 | `call-throw-in-try` | 18 | 2132 | 94.9% |
| 11 | `undefined` | 18 | 2150 | 95.7% |
| 12 | `ref-argument` | 18 | 2168 | 96.5% |
| 13 | `ArrayElementReference` | 17 | 2185 | 97.3% |
| 14 | `ImplicitIndexerReference` | 11 | 2196 | 97.8% |
| 15 | `InstanceReference` | 10 | 2206 | 98.2% |
| 16 | `ArrayCreation` | 8 | 2214 | 98.6% |
| 17 | `rethrow` | 8 | 2222 | 98.9% |
| 18 | `TranslatedQuery` | 6 | 2228 | 99.2% |
| 19 | `Discard` | 4 | 2232 | 99.4% |
| 20 | `Throw` | 4 | 2236 | 99.6% |
| 21 | `None` | 2 | 2238 | 99.6% |
| 22 | `PropertyReference` | 2 | 2240 | 99.7% |
| 23 | `TypeOf` | 2 | 2242 | 99.8% |
| 24 | `Unary` | 1 | 2243 | 99.9% |

The six filed reasons reach 89.8%. The eighteen that stay unfiled hold the last 10.1%, and none of
them adds more than 36 pairs at its step.

## Table 3: the lowerable share, today and at the ceiling
"Today" is `changedPairsWithoutOpaque` as the runs recorded it. "Every owner landed" unlocks the
owned reasons of the section above. "The whole tail landed" unlocks every reason but the two that
are opaque by decision, which is the ceiling IR coverage can reach on these runs.

| Run | Changed pairs | Lowerable today | With every owner landed | With the whole tail landed |
|---|---|---|---|---|
| gitextensions-8522 | 951 | 415 (43.6%) | 551 (57.9%) | 950 (99.9%) |
| gitextensions-9860 | 722 | 218 (30.2%) | 422 (58.4%) | 722 (100.0%) |
| jellyfin-13023 | 573 | 134 (23.4%) | 276 (48.2%) | 571 (99.7%) |
| Sum | 2246 | 767 (34.1%) | 1249 (55.6%) | 2243 (99.9%) |

- The ceiling is not a proof rate. A pair with no opaque node is one the solver is asked about, and
  what it then answers is the other half of the Unknowns: 400 of the 1,471 are `timeout` (P1-031,
  ADR 0050).
- The ceiling includes `rebound-call`, which is not lowered but named: 248 pairs hold it, and
  without an entry for each of their callee pairs the ceiling is 1,995 of 2,246 (88.8%).
- Half of the distance to the ceiling has no owner: the owners add 482 pairs (21.5 points) and the
  unowned reasons 994 (44.3 points).

## The reason sets behind the unowned count
The 25 largest reason sets that hold an unowned reason, of 236 such sets:

| Reason set | gitextensions-8522 | gitextensions-9860 | jellyfin-13023 | Sum |
|---|---|---|---|---|
| `rebound-call` | 113 | 25 | 11 | 149 |
| `DelegateCreation` | 50 | 22 | 53 | 125 |
| `DelegateCreation+switch-pattern` | 16 | 9 | 33 | 58 |
| `Conversion+DelegateCreation` | 9 | 25 | 10 | 44 |
| `InterpolatedString` | 4 | 23 | 0 | 27 |
| `iterator` | 11 | 9 | 7 | 27 |
| `DefaultValue` | 14 | 11 | 1 | 26 |
| `CaughtException` | 11 | 8 | 5 | 24 |
| `Conversion+DefaultValue` | 2 | 19 | 1 | 22 |
| `rebound-call+switch-pattern` | 10 | 0 | 12 | 22 |
| `InterpolatedString+switch-pattern` | 6 | 12 | 0 | 18 |
| `CompoundAssignment` | 9 | 3 | 2 | 14 |
| `Tuple` | 6 | 3 | 2 | 11 |
| `Conversion+DelegateCreation+switch-pattern` | 0 | 0 | 10 | 10 |
| `CaughtException+DelegateCreation` | 4 | 4 | 1 | 9 |
| `Binary+DelegateCreation+switch-pattern` | 1 | 0 | 7 | 8 |
| `CaughtException+Conversion` | 2 | 6 | 0 | 8 |
| `CaughtException+switch-pattern` | 4 | 3 | 1 | 8 |
| `DeconstructionAssignment` | 4 | 4 | 0 | 8 |
| `ArrayElementReference` | 4 | 3 | 0 | 7 |
| `Conversion+DefaultValue+switch-pattern` | 1 | 6 | 0 | 7 |
| `Conversion+InterpolatedString` | 0 | 7 | 0 | 7 |
| `CompoundAssignment+switch-pattern` | 4 | 0 | 2 | 6 |
| `DefaultValue+switch-pattern` | 4 | 0 | 2 | 6 |
| `InstanceReference` | 2 | 4 | 0 | 6 |

## Tickets filed (criterion 4)
Every unowned reason at or above 1%, largest first, to the cap of six:

| Ticket | Reason | Marginal unlock | Share of 2,246 | In | Alone |
|---|---|---|---|---|---|
| P2-136 | `DelegateCreation` | 257 | 11.4% | 338 | 125 |
| P2-137 | `rebound-call` | 191 | 8.5% | 248 | 149 |
| P2-138 | `DefaultValue` | 73 | 3.3% | 148 | 26 |
| P2-139 | `InterpolatedString` | 59 | 2.6% | 112 | 27 |
| P2-140 | `CaughtException` | 51 | 2.3% | 113 | 24 |
| P2-141 | `CompoundAssignment` | 35 | 1.6% | 60 | 14 |

Not filed, with their counts: `iterator`, 27 pairs (1.2%), every one of them alone because an
iterator is a whole-body opaque, is over the line and is the seventh, so the cap leaves it out; it
is the next ticket to file. Below the line: `DeconstructionAssignment` 19, `Tuple` 16,
`ArrayElementReference` 9, `undefined` 8, `ref-argument` 7, `InstanceReference` 7,
`call-throw-in-try` 6, `ImplicitIndexerReference` 5, `Throw` 2, `None` 2, `PropertyReference` 2,
`TypeOf` 2, `ArrayCreation` 1, `TranslatedQuery` 1, `Discard` 1, `rethrow` 0, `Unary` 0. Those
seventeen unlock 88 pairs between them alone (3.9%), and 200 in the greedy order.

`InstanceReference` shows why bodies are the wrong measure: it is the fourth largest reason by
bodies on `gitextensions-9860` (422 per side) and is in 10 changed pairs of the 2,246.

## Check against later censuses
The three runs are four and five days old, and tickets that change which pairs count as changed
have merged since. The same script over the newest census of each pair on this machine, none made
for this ticket: `gitextensions-8522` from P1-032's branch (2026-10-06, after P2-127, 859 changed pairs),
`gitextensions-9860` at `c1d1d5f` (2026-10-03, after P2-099, 722), `jellyfin-13023` on P1-029's
branch (2026-10-04, after P2-113 and P1-029, 162). 1,743 changed pairs in all; lowerable today 685
(39.3%), with every owner landed 945 (54.2%), ceiling 1,742.

| Unowned reason | Marginal unlock, the three runs (of 2,246) | Marginal unlock, later censuses (of 1,743) |
|---|---|---|
| `rebound-call` | 191 (8.5%) | 182 (10.4%) |
| `DelegateCreation` | 257 (11.4%) | 135 (7.7%) |
| `DefaultValue` | 73 (3.3%) | 58 (3.3%) |
| `InterpolatedString` | 59 (2.6%) | 54 (3.1%) |
| `LocalFunction` | not a reason yet | 48 (2.8%) |
| `CaughtException` | 51 (2.3%) | 41 (2.4%) |
| `CompoundAssignment` | 35 (1.6%) | 21 (1.2%) |
| `iterator` | 27 (1.2%) | 21 (1.2%) |
| `DeconstructionAssignment` | 19 (0.8%) | 13 (0.7%) |
| `Tuple` | 16 (0.7%) | 12 (0.7%) |

- **The order of the six holds, and so does each one's place above the line.**
- **`DelegateCreation` on Jellyfin was a runtime-row effect.** It was in 156 of Jellyfin's changed
  pairs and is in 4: P2-113 gave the `String.Equals` rows a change point, those pairs are congruent
  again, and Jellyfin's changed pairs fell from 573 to 162. What is left of the reason is Git
  Extensions: 133 of the 135.
- **`LocalFunction` is a new unowned reason.** P2-127 made a call of a local function an opaque
  (its soundness fix). On `gitextensions-8522` it is in 62 changed pairs, alone in 29, with a
  marginal unlock of 48, and that run's pairs without opaque fell from 378 to 349. The other two
  censuses predate P2-127, so 48 undercounts it. It is not in the three runs, so criterion 4 does
  not reach it and no ticket is filed here; it would rank fifth.

## Limits
- "Lands in full" is an assumption about each owner, and P2-122 and P2-087 each lower only the
  largest form of their reason. The "every owner landed" column is what the open tickets can reach
  at most, not what they will reach.
- A reason set says which reasons a pair holds, not which form of each. P2-095's share of
  `DefaultValue` and P2-102's share of `InterpolatedString` are inside those reasons' counts, and
  each new ticket's first criterion is the split by form that would take them out.
- A pair counts as changed when it is not congruent, so a change in what makes pairs congruent moves
  every number here, as P2-113 did on Jellyfin. P2-124 and P2-130 rerun these pairs on one commit.
