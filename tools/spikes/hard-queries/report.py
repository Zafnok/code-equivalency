"""P2-101: prints the tables docs/runs/2026-10-07-hard-queries.md quotes.

usage: report.py <outDir>
  <outDir>   a folder under the working directory that holds located.tsv, located-r2p.tsv, tried.tsv
             and replayed.tsv, as the spike wrote them, and optionally was.tsv: index, identity, and
             what P2-050 saw time out at 20 times the budget
Identities, statuses and counts only.
"""
import collections
import os
import sys

FEATURES = ("terms sorts bvWidths arraySorts uninterpretedSorts stringSort datatypeSorts sequenceSorts intSort floatSorts "
            "mul mulNonlinear div mulOverflow otherOverflow shiftByTerm select store constArray "
            "callApps callFunctions pureApps pureFunctions otherFunctionApps datatypeOps sequenceOps intOps ite distinct quantifiers").split()
HARD = ["rung", "query", "reason", "ms", "assertions", "inlinedTerms"] + FEATURES
GROUPS = ["heap maps and calls", "heap maps, calls and pure functions", "sequence trace (rung 2)", "other"]


def folder(argument):
    """The output folder, which must be under the working directory: nothing else is ever read."""
    base = os.path.realpath(os.getcwd())
    out = os.path.realpath(argument)
    if not out.startswith(base + os.sep):
        sys.exit("the output folder must be under the working directory")
    return out


def read(out, name):
    path = os.path.join(out, os.path.basename(name))
    return [line.rstrip("\n").split("\t") for line in open(path, encoding="utf-8")] if os.path.exists(path) else []


def located(out):
    rows = []
    for c in read(out, "located.tsv"):
        row = {"index": int(c[0]), "identity": c[1], "summary": c[3], "loop": c[4], "steps": c[6], "verdict": c[7].split(" [")[0], "ladder": c[7], "hard": []}
        rest = c[8:]
        for i in range(0, len(rest), len(HARD)):
            h = dict(zip(HARD, rest[i:i + len(HARD)]))
            for k in HARD[3:]:
                if k != "bvWidths":
                    h[k] = int(h[k])
            row["hard"].append(h)
        rows.append(row)
    return sorted(rows, key=lambda r: r["index"])


def theories(h):
    t = []
    if h["mulNonlinear"] + h["div"] + h["mulOverflow"] > 0:
        t.append("multiplication or division")
    if h["select"] + h["store"] > 0:
        t.append("heap maps")
    if h["callApps"] > 0:
        t.append("calls")
    if h["pureApps"] > 0:
        t.append("pure functions")
    if h["sequenceOps"] + h["datatypeOps"] > 0:
        t.append("sequence trace")
    return t


def group(h):
    t = theories(h)
    if "sequence trace" in t:
        return GROUPS[2]
    if t == ["heap maps", "calls"]:
        return GROUPS[0]
    if t == ["heap maps", "calls", "pure functions"]:
        return GROUPS[1]
    return GROUPS[3]


def median(values):
    values = sorted(values)
    return values[len(values) // 2]


def main():
    out = folder(sys.argv[1])
    rows = located(out)
    was = {int(c[0]): c[2] for c in read(out, "was.tsv")}
    # The query between a pair and a proof is the last the ladder gave up on: rung 2's obligation for a looping pair.
    hard = [r for r in rows if r["verdict"] == "Unknown(Timeout)"]
    by = {r["index"]: r for r in rows}

    print("### verdict at this commit")
    for verdict, n in collections.Counter(r["verdict"] + " | " + ("with a rung 1 query that times out" if r["hard"] else "no query times out") for r in rows).most_common():
        print(f"| {verdict} | {n} |")
    print("\n### the query, for the pairs still Unknown(timeout):", len(hard))
    for key, n in collections.Counter((r["hard"][-1]["rung"], r["hard"][-1]["query"], r["loop"]) for r in hard).most_common():
        print(f"| {key} | {n} |")
    print("reason:", collections.Counter(r["hard"][-1]["reason"] for r in hard))

    print("\n### sizes")
    for name in ("assertions", "terms", "inlinedTerms", "sorts", "uninterpretedSorts", "arraySorts", "ms"):
        v = sorted(r["hard"][-1][name] for r in hard)
        print(f"| {name} | {v[0]} | {median(v)} | {v[int(len(v) * .9)]} | {v[-1]} |")

    print("\n### operators: pairs with at least one, median count among them")
    for f in FEATURES:
        if f != "bvWidths":
            v = [r["hard"][-1][f] for r in hard if r["hard"][-1][f] > 0]
            print(f"| {f} | {len(v)} | {median(v) if v else 0} |")
    print("widths:", collections.Counter(w for r in hard for w in r["hard"][-1]["bvWidths"].split(",")))

    print("\n### feature sets")
    sets = collections.defaultdict(list)
    for r in hard:
        sets[", ".join(theories(r["hard"][-1]))].append(r)
    for key, members in sorted(sets.items(), key=lambda kv: -len(kv[1])):
        print(f"| {key} | {len(members)} | {median([m['hard'][-1]['terms'] for m in members])} | {collections.Counter(m['hard'][-1]['rung'] + ' ' + m['hard'][-1]['query'] for m in members).most_common()} |")
    print("\n### groups")
    for g in GROUPS:
        members = [r for r in hard if group(r["hard"][-1]) == g]
        if members:
            print(f"| {g} | {len(members)} | {median([m['hard'][-1]['terms'] for m in members])} | {median([m['hard'][-1]['sorts'] for m in members])} | {collections.Counter(m['hard'][-1]['query'] for m in members).most_common()} | {','.join(str(m['index']) for m in members)} |")

    print("\n### per pair")
    print("| # | procedure identity | timed out in P2-050 at 20 times the budget | verdict now | rung, query that times out now | assertions | terms | sorts | operators |")
    print("|---|---|---|---|---|---|---|---|---|")
    for r in rows:
        cells = [str(r["index"]), f"`{r['identity']}`", was.get(r["index"], ""), r["verdict"].replace("Timeout", "timeout").replace("Opaque", "opaque").replace("Abstraction", "abstraction")]
        if r["hard"]:
            h = r["hard"][-1]
            detail = [f"{label} {h[key]}" for key, label in (("mulNonlinear", "mul"), ("div", "div"), ("select", "select"), ("store", "store"), ("callApps", "call"), ("pureApps", "pure"), ("sequenceOps", "seq"), ("datatypeOps", "datatype")) if h[key] > 0]
            cells += [f"{h['rung'][1]}, `{h['query']}`", str(h["assertions"]), str(h["terms"]), str(h["sorts"]), ", ".join(detail)]
        else:
            cells += ["none", "", "", "", ""]
        print("| " + " | ".join(cells) + " |")

    tried = collections.defaultdict(dict)
    larger = collections.defaultdict(dict)
    for c in read(out, "tried.tsv"):
        (tried if c[3] == "2000000" else larger)[(c[1], c[2]) if c[3] == "2000000" else (c[1], c[2], c[3])][int(c[0])] = c
    first = collections.defaultdict(dict)
    for c in read(out, "tried-run1.tsv"):
        first[(c[1], c[2])][int(c[0])] = c
    print("\n### tried: file, variant, group, pairs, unsat, sat (model holds), sat (model fails), gave up, error; then the first ten of the group")
    order = []
    for (suffix, variant), answers in tried.items():
        for g in GROUPS:
            mine = {i: c for i, c in answers.items() if i in by and by[i]["verdict"] == "Unknown(Timeout)" and by[i]["hard"][-1]["rung"] == suffix[1:] and group(by[i]["hard"][-1]) == g}
            if not mine:
                continue

            def count(cs):
                return (sum(1 for c in cs if c[4] == "unsat"), sum(1 for c in cs if c[4] == "sat" and c[5] == "model holds"),
                        sum(1 for c in cs if c[4] == "sat" and c[5] != "model holds"), sum(1 for c in cs if c[4] == "unknown"), sum(1 for c in cs if c[4] == "error"))

            ten = [mine[i] for i in sorted(mine)[:10]]
            order.append((suffix, GROUPS.index(g), variant, f"| {suffix} | {variant} | {g} | {len(mine)} | " + " | ".join(map(str, count(mine.values()))) + f" | {len(ten)}: " + " / ".join(map(str, count(ten))) + " |"))
    for line in sorted(order):
        print(line[3])
    print("\n### a larger resource limit: file, variant, limit, pairs, unsat, sat (model holds), sat (model fails), gave up, of them on the wall-clock backstop")
    for (suffix, variant, limit), answers in sorted(larger.items()):
        mine = [c for i, c in answers.items() if i in by and by[i]["verdict"] == "Unknown(Timeout)" and by[i]["hard"][-1]["rung"] == suffix[1:]]
        print(f"| {suffix} | {variant} | {limit} | {len(mine)} | {sum(1 for c in mine if c[4] == 'unsat')} | {sum(1 for c in mine if c[4] == 'sat' and c[5] == 'model holds')} | {sum(1 for c in mine if c[4] == 'sat' and c[5] != 'model holds')} | {sum(1 for c in mine if c[4] == 'unknown')} | {sum(1 for c in mine if c[5] in ('timeout', 'interrupted'))} |")
    print("\n### models found within the limit (spent at most 10% over it)")
    for key in sorted(tried):
        sat = [c for i, c in tried[key].items() if c[4] == "sat" and i in by and by[i]["verdict"] == "Unknown(Timeout)" and by[i]["hard"][-1]["rung"] == key[0][1:]]
        if sat:
            print(key, len(sat), "within:", sum(1 for c in sat if int(c[7]) <= 2200000), "largest spent:", max(int(c[7]) for c in sat))
    print("\n### same answer in both runs")
    for key in sorted(tried):
        if key in first:
            common = [i for i in tried[key] if i in first[key]]
            print(key, len(common), "differ:", [i for i in common if tried[key][i][4] != first[key][i][4]])
    print("\n### errors")
    print(collections.Counter((c[2], c[5][:80]) for c in read(out, "tried.tsv") if c[4] == "error"))

    print("\n### rung 2 by position")
    for c in sorted(read(out, "located-r2p.tsv"), key=lambda c: int(c[0])):
        print(f"| {c[0]} | {by[int(c[0])]['verdict']} | {by[int(c[0])]['steps']} | {c[4]} |")

    print("\n### replayed: variant, outcome of rung 1 with the variant behind Z3, pairs")
    replayed = read(out, "replayed.tsv")
    for key, n in sorted(collections.Counter((c[1], c[2], c[3] if len(c) > 3 else "") for c in replayed if int(c[0]) in by and by[int(c[0])]["verdict"] == "Unknown(Timeout)").items()):
        print(f"| {key[0]} | {key[1]} | {key[2]} | {n} |")
    print("\n### replayed, by pair")
    for c in sorted(replayed, key=lambda c: (c[1], int(c[0]))):
        if c[2] not in ("Unknown(timeout)",) and int(c[0]) in by and by[int(c[0])]["verdict"] == "Unknown(Timeout)":
            print(f"| {c[1]} | {c[0]} | `{by[int(c[0])]['identity']}` | {c[2]} | {c[4] if len(c) > 4 else ''} |")


main()
