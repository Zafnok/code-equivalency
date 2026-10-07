# P1-028 opaque-tail count (throwaway)

Counts, over several runs' censuses, the changed pairs each opaque reason is in, keeps opaque alone,
and would unlock once every reason an open ticket owns is gone. The result is in
`docs/runs/2026-10-07-opaque-tail.md`. This directory is deleted if nothing builds on it.

`opaque_tail.py` reads `run.properties.loweringCensus` (`opaqueByReason`, `changedReasonSets`) from
each SARIF file and prints the report's tables as Markdown. It loads no solution and runs no pair.
Which reasons count as owned is the `OWNED` table at its top, the ticket state of 2026-10-07; the
report says by what rule. A reason spelled `Name:detail` is counted as `Name`.

It is not in `Equiv.slnx` and no CI gate runs it. It needs Python 3 and nothing else.

## Reproduce

With the three runs' SARIF under `.corpus/pairs/<slug>/runs/<run>/` (ADR 0028; never committed):

```
python tools/spikes/opaque-tail/opaque_tail.py `
  gitextensions-8522=.corpus/pairs/gitextensions-8522/runs/20261002-1814-full-after/equiv.sarif `
  gitextensions-9860=.corpus/pairs/gitextensions-9860/runs/20261003-0055-full/equiv.sarif `
  jellyfin-13023=.corpus/pairs/jellyfin-13023/runs/20261003-0055-full/equiv.sarif
```
