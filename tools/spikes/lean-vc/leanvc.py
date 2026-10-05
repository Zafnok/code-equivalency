"""P1-037 spike: a rung 1 query as a Lean theorem, closed by a scaffold or by a model, and admitted
only when Lean accepts the file. See README.md.

  leanvc.py --self-test                         criterion 2
  leanvc.py --control                           criterion 3
  leanvc.py --run <smtDir> <results.tsv> <out>  criteria 1, 4
  leanvc.py --report <out>                      the run's tables
"""
import argparse
import concurrent.futures
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import threading
import time

import smt2lean

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, '..', '..', '..'))
LEAN = os.path.join(ROOT, '.corpus', 'elan', 'toolchains', 'leanprover--lean4---v4.34.1', 'bin',
                    'lean.exe')
WORK = os.path.join(ROOT, '.corpus', 'lean-vc', 'work')

ELABORATE_S = 600          # the ticket's ten minutes
CLOSER_S = 120             # one scaffold tactic
PROOF_S = 600              # one of the model's proofs
ROUNDS = 5
MODEL_CHARS = 1_500_000    # a larger theorem does not fit the model's context beside its answer
# What needs no insight. `grind` and `bv_decide` split the boolean structure (each path's `bif` and
# disjunction) themselves, then close a branch by congruence or by bit-blasting.
CLOSERS = ['bv_decide', 'grind', 'simp_all', 'simp_all <;> grind', 'bv_omega', 'omega']
STANDARD = {'propext', 'Classical.choice', 'Quot.sound'}
# A proof is a tactic block. None of these may start a line of it or appear in it.
COMMAND = re.compile(r'^\s*(#|theorem|lemma|def|abbrev|axiom|instance|macro|syntax|elab|notation|'
                     r'open|namespace|section|end|import|attribute|unsafe|opaque|example|'
                     r'structure|inductive|class|initialize|deriving|universe|variable)\b', re.M)
BANNED = re.compile(r'\b(sorry|admit|native_decide|implemented_by|extern|sorryAx|ofReduceBool|'
                    r'trustCompiler)\b')
AXIOMS = '\n#print axioms q\n'


def lean(path, limit):
    """Runs Lean on a file. Returns (seconds, output or None on a timeout)."""
    start = time.time()
    try:
        done = subprocess.run([LEAN, path], capture_output=True, timeout=limit, text=True,
                              encoding='utf-8', errors='replace')
        return time.time() - start, done.stdout + done.stderr
    except subprocess.TimeoutExpired:
        return time.time() - start, None


def verdict(statement, proof, path, limit):
    """Admits a proof of the generated statement, or says why not.

    Returns (kind, seconds, detail): kind is 'kernel' (Lean's three axioms at most), 'bv_decide'
    (those and the axiom `bv_decide` adds for its certificate check, which Lean's compiler runs,
    not its kernel), 'rejected' or 'timeout'. The statement is written by this function, so it is
    the generated one byte for byte; the proof is only ever text after `:= by`."""
    if COMMAND.search(proof) or BANNED.search(proof):
        return 'rejected', 0.0, 'the proof holds a command or a banned word (sorry, an axiom, ...)'
    with open(path, 'w', encoding='utf-8') as f:
        f.write(smt2lean.PRELUDE + statement + proof.rstrip() + '\n' + AXIOMS)
    secs, out = lean(path, limit)
    if out is None:
        return 'timeout', secs, 'Lean did not finish in %d s' % limit
    if re.search(r'\berror\b', out) or 'sorry' in out:
        return 'rejected', secs, out
    m = re.search(r"'q' depends on axioms: \[([^\]]*)\]", out)
    if "'q' does not depend on any axioms" in out:
        used = set()
    elif m:
        used = {a.strip() for a in m.group(1).split(',')}
    else:
        return 'rejected', secs, 'no axiom report: ' + out
    extra = used - STANDARD
    if not extra:
        return 'kernel', secs, ''
    if all(re.fullmatch(r'q\._native\.bv_decide\.ax_\d+(_\d+)*', a) for a in extra):
        return 'bv_decide', secs, ''
    return 'rejected', secs, 'axioms beyond the standard three: ' + ', '.join(sorted(extra))


def scaffold(statement, stem, base=0.0):
    """Tries each closer as the whole proof, for CLOSER_S beyond what the statement alone takes
    to elaborate. Returns (closer or None, kind, [(closer, outcome, s)],
    the last error text)."""
    tried, last = [], ''
    for closer in CLOSERS:
        path = '%s.%s.lean' % (stem, re.sub(r'\W+', '_', closer))
        kind, secs, detail = verdict(statement, '  ' + closer, path, CLOSER_S + base)
        tried.append((closer, kind, secs))
        if kind in ('kernel', 'bv_decide'):
            return closer, kind, tried, ''
        if closer == 'grind' or not last:
            last = detail
    return None, None, tried, last


SYSTEM = ('You write Lean 4 proofs (Lean 4.34.1, core and Std only, no Mathlib). You are given a '
          'theorem whose proof is a hole. Reply with the tactic proof that replaces the hole, in '
          'one ```lean fenced block, every line indented by two spaces, and nothing that is not a '
          'tactic: no `theorem`, no `sorry`, no `native_decide`, no axiom, no command. The '
          'statement is fixed and is not repeated in your answer. Lean compiles it and you are '
          'given the error if it fails.')


def ask(prompt, session):
    """One model turn through the `claude` command line, with no tools. Returns (text, session,
    tokens in, tokens out)."""
    cmd = [shutil.which('claude'), '-p', '--model', 'opus', '--output-format', 'json', '--tools', '']
    cmd += ['--resume', session] if session else ['--system-prompt', SYSTEM]
    cwd = os.path.join(tempfile.gettempdir(), 'leanvc-model')
    os.makedirs(cwd, exist_ok=True)
    done = subprocess.run(cmd, input=prompt, capture_output=True, text=True, encoding='utf-8',
                          errors='replace', cwd=cwd)
    try:
        reply = json.loads(done.stdout)
    except ValueError:
        raise RuntimeError('model call failed: ' + (done.stdout + done.stderr)[:500])
    use = reply.get('usage') or {}
    tin = sum(use.get(k, 0) for k in ('input_tokens', 'cache_creation_input_tokens',
                                      'cache_read_input_tokens'))
    return reply.get('result') or '', reply.get('session_id'), tin, use.get('output_tokens', 0)


def clip(text, n=6000):
    return text if len(text) <= n else text[:n * 2 // 3] + '\n...\n' + text[-n // 3:]


def model_loop(statement, stem, hint):
    """At most ROUNDS proofs from the model, each answered with Lean's error. Returns a dict."""
    start = time.time()
    prompt = ('The theorem. Each hypothesis is one assertion of an SMT query; the goal is `False`.'
              '\n\n```lean\n' + smt2lean.PRELUDE + statement + '  sorry\n```\n\n'
              'The tactics ' + ', '.join('`%s`' % c for c in CLOSERS) + ' each fail as '
              'the whole proof. The last error:\n\n' + clip(hint))
    session, tin, tout = None, 0, 0
    for rnd in range(1, ROUNDS + 1):
        text, session, i, o = ask(prompt, session)
        tin, tout = tin + i, tout + o
        blocks = re.findall(r'```(?:lean4?|)\n(.*?)```', text, re.S)
        proof = blocks[-1] if blocks else text
        with open('%s.round%d.txt' % (stem, rnd), 'w', encoding='utf-8') as f:
            f.write(text)
        kind, _, detail = verdict(statement, proof, '%s.round%d.lean' % (stem, rnd), PROOF_S)
        if kind in ('kernel', 'bv_decide'):
            return {'closed': kind, 'rounds': rnd, 'seconds': time.time() - start,
                    'tokensIn': tin, 'tokensOut': tout}
        prompt = 'Lean rejects that proof:\n\n' + clip(detail) + '\n\nReply with a corrected proof.'
    return {'closed': None, 'rounds': ROUNDS, 'seconds': time.time() - start, 'tokensIn': tin,
            'tokensOut': tout}


# criterion 3 -----------------------------------------------------------------------------------
TRIVET = '''(set-logic QF_BV)
(declare-fun x () (_ BitVec 32))
(declare-fun y () (_ BitVec 32))
(assert (not (bvule (bvmul (bvudiv x y) y) x)))
(check-sat)
'''


def control():
    """Trivet's `(x /u y) * y <=u x` at 32 bits, as the query that says it fails, through the same
    scaffold and model loop as a corpus query."""
    os.makedirs(WORK, exist_ok=True)
    statement = smt2lean.translate(TRIVET).statement()
    stem = os.path.join(WORK, 'control')
    closer, kind, tried, hint = scaffold(statement, stem)
    for c, k, s in tried:
        print('scaffold %-10s %-9s %6.1f s' % (c, k, s))
    if closer:
        print('control: closed by the scaffold alone (%s, %s)' % (closer, kind))
        return 0
    result = model_loop(statement, stem, hint)
    print('control: model %s' % json.dumps(result))
    return 0 if result['closed'] else 1


# criteria 1 and 4 ------------------------------------------------------------------------------
def best_answer(row):
    """What P1-034's run says of the positional query: some solver's `sat` or `unsat`, else none."""
    answers = [c.split('=', 1)[1].split('/')[0] for c in row[8:] if 'positional' in c]
    for answer in ('unsat', 'sat'):
        if answer in answers:
            return answer
    return 'undecided'


def one(smt_dir, out, row, use_model):
    index, identity, query = int(row[0]), row[1], row[2]
    rec = {'index': index, 'identity': identity, 'query': query, 'solvers': best_answer(row)}
    src = os.path.join(smt_dir, '%03d.pos.smt2' % index)
    stem = os.path.join(out, '%03d' % index)
    try:
        with open(src, encoding='utf-8') as f:
            tr = smt2lean.translate(f.read())
        statement = tr.statement()
    except smt2lean.Unsupported as e:
        rec['outcome'] = 'not translated: %s' % e
        return rec, {}
    except (MemoryError, RecursionError) as e:
        rec['outcome'] = 'not translated: %s' % type(e).__name__
        return rec, {}
    rec.update(smtBytes=os.path.getsize(src), leanChars=len(statement), consts=len(tr.consts),
               hypotheses=len(tr.asserts))
    with open(stem + '.lean', 'w', encoding='utf-8') as f:
        f.write(smt2lean.PRELUDE + statement + '  sorry\n')
    secs, text = lean(stem + '.lean', ELABORATE_S)
    rec['elaborateSeconds'] = round(secs, 1)
    if text is None or re.search(r'\berror\b', text):
        rec['outcome'] = 'too large' if text is None else 'does not elaborate: ' + clip(text, 300)
        return rec, tr.ops
    closer, kind, tried, hint = scaffold(statement, stem, secs)
    rec['scaffold'] = ['%s=%s/%.0fs' % t for t in tried]
    if closer:
        rec.update(outcome='closed by the scaffold alone', closer=closer, axioms=kind)
    elif rec['solvers'] == 'sat':
        rec['outcome'] = 'not closed: a solver answers satisfiable, so the model is not asked'
    elif len(statement) > MODEL_CHARS:
        rec['outcome'] = 'not closed: the theorem is larger than the model reads'
    elif not use_model:
        rec['outcome'] = 'not closed: model not asked'
    else:
        result = model_loop(statement, stem, hint)
        rec['model'] = result
        rec['outcome'] = 'closed with the model' if result['closed'] else 'not closed'
        rec['axioms'] = result['closed']
    return rec, tr.ops


def run(smt_dir, results, out, threads, use_model, only):
    os.makedirs(out, exist_ok=True)
    with open(results, encoding='utf-8') as f:
        rows = [line.rstrip('\n').split('\t') for line in f if line.strip()]
    if only:
        rows = [r for r in rows if int(r[0]) in only]
    ops, recs, lock = {}, [], threading.Lock()
    log = open(os.path.join(out, 'lean-vc.jsonl'), 'a', encoding='utf-8')

    def work(row):
        rec, used = one(smt_dir, out, row, use_model)
        with lock:
            for k, v in used.items():
                ops.setdefault(k, [0, 0])
                ops[k][0] += v
                ops[k][1] += 1
            recs.append(rec)
            log.write(json.dumps(rec) + '\n')
            log.flush()
            print('%03d %s' % (rec['index'], rec['outcome'][:90]), flush=True)

    with concurrent.futures.ThreadPoolExecutor(threads) as pool:
        list(pool.map(work, rows))
    print('\noperator\toccurrences\tfiles')
    for k, (n, files) in sorted(ops.items(), key=lambda kv: -kv[1][0]):
        print('%s\t%d\t%d' % (k, n, files))
    counts = {}
    for r in recs:
        key = r['outcome'].split(':')[0]
        counts[key] = counts.get(key, 0) + 1
    print()
    for k, v in sorted(counts.items()):
        print('%4d  %s' % (v, k))
    return 0


def report(out):
    """Prints the run's tables as Markdown: identities and counts, never query or proof text."""
    last = {}
    with open(os.path.join(out, 'lean-vc.jsonl'), encoding='utf-8') as f:
        for line in f:
            rec = json.loads(line)
            last[rec['index']] = rec
    recs = [last[k] for k in sorted(last)]
    short = lambda r: r['outcome'].split(':')[0] if r['outcome'].startswith(
        ('not translated', 'does not elaborate')) else r['outcome']
    counts = {}
    for r in recs:
        counts.setdefault(short(r), []).append(r)
    print('| outcome | queries | of them, some solver says unsatisfiable |\n|---|---|---|')
    for k in sorted(counts):
        print('| %s | %d | %d |' % (k, len(counts[k]),
                                    sum(r['solvers'] == 'unsat' for r in counts[k])))
    done = [r for r in recs if 'elaborateSeconds' in r and r['outcome'] != 'too large'
            and not r['outcome'].startswith('does not')]
    secs = sorted(r['elaborateSeconds'] for r in done)
    if secs:
        print('\nelaborated: %d, median %.1f s, slowest %.1f s; largest statement elaborated: '
              '%d characters' % (len(secs), secs[len(secs) // 2], secs[-1],
                                 max(r['leanChars'] for r in done)))
    tally = {}
    for r in recs:
        for t in r.get('scaffold', []):
            name, rest = t.split('=')
            kind = rest.split('/')[0]
            tally.setdefault(name, {}).setdefault(kind, 0)
            tally[name][kind] += 1
    print('\n| tactic | closes | fails | time limit |\n|---|---|---|---|')
    for name in CLOSERS:
        t = tally.get(name, {})
        print('| `%s` | %d | %d | %d |' % (name, t.get('kernel', 0) + t.get('bv_decide', 0),
                                           t.get('rejected', 0), t.get('timeout', 0)))
    print('\n| procedure identity | query | solvers (P1-034) | statement, characters | '
          'elaborates, s | outcome |\n|---|---|---|---|---|---|')
    for r in recs:
        m = r.get('model')
        extra = '' if not m else ' (%d rounds, %.0f s, %d tokens in, %d out)' % (
            m['rounds'], m['seconds'], m['tokensIn'], m['tokensOut'])
        print('| `%s` | `%s` | %s | %s | %s | %s%s |' % (
            r['identity'], r['query'], r['solvers'], '{:,}'.format(r['leanChars'])
            if 'leanChars' in r else 'n/a', r.get('elaborateSeconds', 'n/a'), short(r), extra))
    return 0


def in_corpus(path):
    """The path, resolved, when it is under this checkout's `.corpus/`; the run stops otherwise.
    What is read and written holds text from the analysed code, which lives nowhere else."""
    base = os.path.realpath(os.path.join(ROOT, '.corpus'))
    real = os.path.realpath(path)
    if not real.startswith(base + os.sep):
        print('%s is not under %s' % (path, base), file=sys.stderr)
        raise SystemExit(2)
    return real


def main():
    sys.setrecursionlimit(1_000_000)
    ap = argparse.ArgumentParser()
    ap.add_argument('--self-test', action='store_true')
    ap.add_argument('--control', action='store_true')
    ap.add_argument('--run', nargs=3, metavar=('SMTDIR', 'RESULTS', 'OUT'))
    ap.add_argument('--report', metavar='OUT')
    ap.add_argument('--threads', type=int, default=6)
    ap.add_argument('--no-model', action='store_true')
    ap.add_argument('--only', default='')
    a = ap.parse_args()
    if a.self_test:
        import selftest
        return selftest.run()
    if a.control:
        return control()
    if a.run:
        only = {int(x) for x in a.only.split(',') if x}
        smt, results, out = (in_corpus(p) for p in a.run)
        return run(smt, results, out, a.threads, not a.no_model, only)
    if a.report:
        return report(in_corpus(a.report))
    ap.print_help()
    return 2


if __name__ == '__main__':
    threading.stack_size(255 * 1024 * 1024)
    done = {'code': 1}                 # what the process exits with if `main` raises
    t = threading.Thread(target=lambda: done.update(code=main()))
    t.start()
    t.join()
    sys.exit(done['code'])
