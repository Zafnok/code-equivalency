"""P1-028: the opaque reasons no ticket owns, counted over three runs' loweringCensus.

Usage: python opaque_tail.py <slug>=<equiv.sarif> [<slug>=<equiv.sarif> ...]

Reads run.properties.loweringCensus (opaqueByReason, changedReasonSets) from each SARIF and prints
the three tables of docs/runs/2026-10-07-opaque-tail.md as Markdown. Reads nothing else and writes
nothing. The owner map below is the ticket state of 2026-10-07; see the report for the rule.
"""
import json
import sys
from collections import Counter
from itertools import combinations

# Reasons a ticket lowers as a whole: open today, or merged after the runs' commit (8e0ed3c).
OWNED = {
    'Conversion': 'P2-099 and P2-123 (merged after the runs), P2-095',
    'switch-pattern': 'P2-122 (P2-093, P2-103, P2-104)',
    'Binary': 'P2-087',
    'await-using': 'P1-029 (merged after the runs)',
    'CollectionExpression': 'P2-120 (merged after the runs), P2-128',
    'no-body': 'P2-118',
}
# Opaque by decision: out of scope, and never unlocked by any column.
BY_DECISION = {
    'DynamicInvocation': 'opaque by decision (P2-029)',
    'TypeParameterObjectCreation': 'opaque by decision (P2-028)',
}
# Unowned, with the ticket that owns one form of the reason or that already ran.
PARTIAL = {
    'rebound-call': 'none (ADR 0042 keeps a rebound call opaque until a catalogue entry names it; P2-070 added three and is done)',
    'DelegateCreation': 'none (P2-067 done before the runs)',
    'InterpolatedString': 'none (P2-086 done before the runs; P2-102 owns one form)',
    'DefaultValue': 'none (P2-095 owns `default(T?)` only)',
}


def load(path):
    with open(path, encoding='utf-8-sig') as f:
        census = json.load(f)['runs'][0]['properties']['loweringCensus']
    sets = Counter()
    for k, v in census['changedReasonSets'].items():
        # A build that names a reason's form spells it `Name:detail`; count it as `Name`.
        sets[frozenset(r.split(':')[0] for r in k.split('+')) if k else frozenset()] += v
    assert sum(sets.values()) == census['changedPairs']
    return census, sets


def pct(n, d):
    return f'{100.0 * n / d:.1f}%'


def unlocked(sets, removed):
    return sum(v for s, v in sets.items() if s <= removed)


def rule(columns):
    return '|' + '---|' * columns


def is_in(sets, r):
    return sum(v for s, v in sets.items() if r in s)


def alone(sets, r):
    return sets.get(frozenset([r]), 0)


def owner(r):
    return OWNED.get(r) or BY_DECISION.get(r) or PARTIAL.get(r) or 'none'


def bodies(census, r):
    b = {'legacy': 0, 'modern': 0}
    for k, v in census['opaqueByReason'].items():
        if k.split(':')[0] == r:
            b = {side: b[side] + v[side] for side in b}
    return b


def table1(runs, merged, total, reasons):
    print('## Table 1\n')
    print('| Reason | ' + ' | '.join(f'{s}: bodies (legacy / modern), in, alone' for s in runs)
          + ' | Sum: bodies | Sum: in | Sum: alone | Open owner |')
    print(rule(len(runs) + 5))
    for r in reasons:
        cells, bl, bm = [], 0, 0
        for census, sets in runs.values():
            b = bodies(census, r)
            bl += b['legacy']
            bm += b['modern']
            cells.append(f'{b["legacy"]} / {b["modern"]}, {is_in(sets, r)}, {alone(sets, r)}')
        print(f'| `{r}` | ' + ' | '.join(cells)
              + f' | {bl} / {bm} | {is_in(merged, r)} | {alone(merged, r)} ({pct(alone(merged, r), total)}) | {owner(r)} |')


def table2(runs, merged, total, owned, tail):
    base = unlocked(merged, owned)
    print('\n## Table 2\n')
    print('| Unowned reason | ' + ' | '.join(runs) + f' | Sum (marginal unlock) | Share of {total} | Sum: in |')
    print(rule(len(runs) + 4))
    marginal = {r: unlocked(merged, owned | {r}) - base for r in tail}
    for r in sorted(tail, key=lambda r: (-marginal[r], -is_in(merged, r), r)):
        per = [unlocked(sets, owned | {r}) - unlocked(sets, owned) for _, sets in runs.values()]
        print(f'| `{r}` | ' + ' | '.join(map(str, per)) + f' | {marginal[r]} | {pct(marginal[r], total)} | {is_in(merged, r)} |')

    print('\n### Pairs of unowned reasons (unlock beyond the two marginals), top 10\n')
    print('| Two reasons landed together | Changed pairs unlocked | Of which need both |')
    print(rule(3))
    both = []
    for a, b in combinations(tail, 2):
        n = unlocked(merged, owned | {a, b}) - base
        both.append((n - marginal[a] - marginal[b], n, a, b))
    for extra, n, a, b in sorted(both, reverse=True)[:10]:
        print(f'| `{a}` + `{b}` | {n} ({pct(n, total)}) | {extra} |')


def greedy(merged, total, owned, tail):
    print('\n### Greedy order (each step lands the reason that unlocks most, given all before it)\n')
    print(f'| Step | Reason | Changed pairs this step unlocks | Cumulative lowerable | Share of {total} |')
    print(rule(5))
    done, left, at = set(owned), set(tail), unlocked(merged, owned)
    step = 0
    while left:
        best = max(sorted(left), key=lambda r: unlocked(merged, frozenset(done | {r})))
        gain = unlocked(merged, frozenset(done | {best})) - at
        if gain == 0:
            break
        step += 1
        done.add(best)
        left.discard(best)
        at += gain
        print(f'| {step} | `{best}` | {gain} | {at} | {pct(at, total)} |')
    print(f'\nleft after the greedy order, each unlocking nothing more: {", ".join(sorted(left)) or "none"}')


def table3(runs, merged, total, owned, tail):
    print('\n## Table 3\n')
    print('| Run | Changed pairs | Lowerable today | With every owner landed | With the whole tail landed |')
    print(rule(5))
    everything = owned | frozenset(tail)
    rows = [(s, c['changedPairs'], sets) for s, (c, sets) in runs.items()] + [('Sum', total, merged)]
    for s, n, sets in rows:
        a, b, c = unlocked(sets, frozenset()), unlocked(sets, owned), unlocked(sets, everything)
        print(f'| {s} | {n} | {a} ({pct(a, n)}) | {b} ({pct(b, n)}) | {c} ({pct(c, n)}) |')


def reason_sets(runs, merged, owned):
    print('\n## Reason sets with no owned subset reading: top 25 sets holding an unowned reason\n')
    print('| Reason set | ' + ' | '.join(runs) + ' | Sum |')
    print(rule(len(runs) + 2))
    held = [(v, s) for s, v in merged.items() if s - owned]
    for v, s in sorted(held, key=lambda t: (-t[0], sorted(t[1])))[:25]:
        per = [str(sets.get(s, 0)) for _, sets in runs.values()]
        print(f'| `{"+".join(sorted(s))}` | ' + ' | '.join(per) + f' | {v} |')
    print(f'\nsets holding an unowned reason: {len(held)} sets, {sum(v for v, _ in held)} changed pairs')


def main(args):
    runs = {}
    for a in args:
        slug, path = a.split('=', 1)
        runs[slug] = load(path)
    total = sum(c['changedPairs'] for c, _ in runs.values())
    merged = Counter()
    for _, sets in runs.values():
        merged.update(sets)
    reasons = sorted({r for s in merged for r in s}, key=lambda r: (-is_in(merged, r), r))
    owned = frozenset(OWNED)
    tail = [r for r in reasons if r not in OWNED and r not in BY_DECISION]

    print('changed pairs: ' + ', '.join(f'{s} {c["changedPairs"]}' for s, (c, _) in runs.items()) + f', total {total}\n')
    table1(runs, merged, total, reasons)
    table2(runs, merged, total, owned, tail)
    greedy(merged, total, owned, tail)
    table3(runs, merged, total, owned, tail)
    reason_sets(runs, merged, owned)


if __name__ == '__main__':
    main(sys.argv[1:])
