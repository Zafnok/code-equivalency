"""Criterion 2: the translation is checked against Z3 on small queries Z3 decides.

For an unsatisfiable query the Lean theorem must be proved by the scaffold. For a satisfiable one
Z3's model is written as Lean definitions and Lean must evaluate the query's assertions to `true`
under them, which falsifies the theorem. The first query holds every bit-vector operator applied to
operands chosen at the edges (a zero divisor, a shift past the width, the sign bit), each equated
with what Z3 itself computes for it.
"""
import os
import re

import z3

import leanvc
import smt2lean

BV8 = '(_ BitVec 8)'
EDGES = ['#x00', '#x01', '#x07', '#x08', '#x09', '#x7f', '#x80', '#x85', '#xff']
BINARY_BV = ['bvadd', 'bvsub', 'bvmul', 'bvudiv', 'bvurem', 'bvsdiv', 'bvsrem', 'bvsmod', 'bvshl',
             'bvlshr', 'bvashr', 'bvand', 'bvor', 'bvxor']
COMPARE = ['bvult', 'bvule', 'bvugt', 'bvuge', 'bvslt', 'bvsle', 'bvsgt', 'bvsge']


def z3_value(text):
    """What Z3 computes for a closed term."""
    s = z3.Solver()
    s.from_string('(declare-fun r! () %s)(assert (= r! %s))' % (
        'Bool' if text.split()[0][1:] in COMPARE else '(_ BitVec %d)' % width(text), text))
    assert s.check() == z3.sat
    v = s.model()[s.model().decls()[0]]
    return str(v).lower() if z3.is_bool(v) else '(_ bv%d %d)' % (v.as_long(), v.size())


def width(text):
    if text.startswith('(concat'):
        return 16
    m = re.match(r'\(\(_ (zero|sign)_extend (\d+)\)', text)
    if m:
        return 8 + int(m.group(2))
    m = re.match(r'\(\(_ extract (\d+) (\d+)\)', text)
    return int(m.group(1)) - int(m.group(2)) + 1 if m else 8


def operator_table():
    """Every operator on edge operands, equal to Z3's own value for it. Satisfiable by x = #x85."""
    terms = []
    for a in EDGES:
        for b in EDGES:
            terms += ['(%s %s %s)' % (op, a, b) for op in BINARY_BV + COMPARE]
        terms += ['(bvnot %s)' % a, '(bvneg %s)' % a, '(concat %s #x5a)' % a,
                  '((_ zero_extend 8) %s)' % a, '((_ sign_extend 8) %s)' % a,
                  '((_ extract 6 2) %s)' % a]
    lines = ['(declare-fun x () %s)' % BV8, '(assert (= x #x85))']
    lines += ['(assert (= %s %s))' % (t, z3_value(t)) for t in terms]
    # the same edges through a variable, so nothing is folded away before Lean sees it
    lines += ['(assert (= (%s x #x00) %s))' % (op, z3_value('(%s #x85 #x00)' % op))
              for op in ('bvudiv', 'bvurem', 'bvsdiv', 'bvsrem', 'bvsmod')]
    lines += ['(assert (= (%s x #x09) %s))' % (op, z3_value('(%s #x85 #x09)' % op))
              for op in ('bvshl', 'bvlshr', 'bvashr')]
    return '\n'.join(lines)


CASES = [
    ('operators against Z3 (division by zero, shifts past the width, both orders)', 'sat', None),
    ('division and remainder by zero', 'unsat', '''
(declare-fun x () (_ BitVec 8))
(assert (or (not (= (bvudiv x #x00) #xff)) (not (= (bvurem x #x00) x))
            (not (= (bvsrem x #x00) x)) (not (= (bvsmod x #x00) x))
            (not (= (bvsdiv x #x00) (ite (bvslt x #x00) #x01 #xff)))))'''),
    ('signed and unsigned comparison', 'sat', '''
(declare-fun x () (_ BitVec 8))
(declare-fun y () (_ BitVec 8))
(assert (bvult x y)) (assert (bvsgt x y)) (assert (bvuge y #x80)) (assert (bvsle #x01 x))'''),
    ('signed and unsigned comparison', 'unsat', '''
(declare-fun x () (_ BitVec 8))
(declare-fun y () (_ BitVec 8))
(assert (bvult x y)) (assert (bvslt y x)) (assert (bvsge x #x00)) (assert (bvsgt y #x00))'''),
    ('shifts past the width', 'sat', '''
(declare-fun x () (_ BitVec 8))
(declare-fun n () (_ BitVec 8))
(assert (bvugt n #x08)) (assert (= (bvshl x n) #x00)) (assert (= (bvlshr x n) #x00))
(assert (= (bvashr x n) #xff)) (assert (distinct x #xff))'''),
    ('shifts past the width', 'unsat', '''
(declare-fun x () (_ BitVec 8))
(declare-fun n () (_ BitVec 8))
(assert (bvuge n #x08))
(assert (or (distinct (bvshl x n) #x00) (distinct (bvlshr x n) #x00)
            (distinct (bvashr x n) (ite (bvslt x #x00) #xff #x00))))'''),
    ('store and select', 'sat', '''
(declare-fun a () (Array (_ BitVec 8) (_ BitVec 8)))
(declare-fun i () (_ BitVec 8))
(declare-fun j () (_ BitVec 8))
(declare-fun v () (_ BitVec 8))
(assert (distinct (select (store a i v) j) v))
(assert (= (select (store ((as const (Array (_ BitVec 8) (_ BitVec 8))) #x2a) i v) j) #x2a))
(assert (=> (= i j) false))'''),
    ('store and select', 'unsat', '''
(declare-sort S 0)
(declare-fun a () (Array S (_ BitVec 8)))
(declare-fun b () (Array S (_ BitVec 8)))
(declare-fun i () S)
(declare-fun j () S)
(declare-fun v () (_ BitVec 8))
(assert (= b (store a i v)))
(assert (let ((r (select b j))) (and (= i j) (distinct r v))))'''),
    ('an uninterpreted function', 'sat', '''
(declare-fun f ((_ BitVec 8) Bool) (_ BitVec 8))
(declare-fun x () (_ BitVec 8))
(declare-fun y () (_ BitVec 8))
(assert (distinct (f x true) (f y true))) (assert (= (f x false) (bvadd (f y true) #x01)))'''),
    ('an uninterpreted function over a sort', 'unsat', '''
(declare-sort S 0)
(declare-fun f ((_ BitVec 8) S) S)
(declare-fun x () (_ BitVec 8))
(declare-fun y () (_ BitVec 8))
(declare-fun s () S)
(assert (= x (bvadd y #x00))) (assert (not (= (f x s) (f y s))))'''),
]


def lean_value(v, model):
    """A Z3 model value as Lean text."""
    if z3.is_bv_value(v):
        return '(%d#%d)' % (v.as_long(), v.size())
    if z3.is_true(v) or z3.is_false(v):
        return str(v).lower()
    if z3.is_const_array(v):
        return '(fun _ => %s)' % lean_value(v.arg(0), model)
    if z3.is_store(v):
        return '(Smt.store %s %s %s)' % tuple(lean_value(v.arg(i), model) for i in range(3))
    if z3.is_as_array(v):
        return lean_function(model[z3.get_as_array_func(v)], model)
    if z3.is_lambda(v):
        raise ValueError('a lambda in a model')
    raise ValueError('a model value the self-test has no Lean form for: %s' % v)


def lean_function(interp, model):
    names = ['a%d' % i for i in range(interp.arity())]
    body = lean_value(interp.else_value(), model)
    for k in reversed(range(interp.num_entries())):
        e = interp.entry(k)
        cond = ' && '.join('%s == %s' % (names[i], lean_value(e.arg_value(i), model))
                           for i in range(interp.arity()))
        body = '(if %s then %s else %s)' % (cond, lean_value(e.value(), model), body)
    return 'fun %s => %s' % (' '.join(names), body)


def run():
    os.makedirs(leanvc.WORK, exist_ok=True)
    failed = 0
    for n, (title, expected, text) in enumerate(CASES):
        text = operator_table() if text is None else text
        solver = z3.Solver()
        solver.from_string(text)
        answer = str(solver.check())
        tr = smt2lean.translate(text)
        stem = os.path.join(leanvc.WORK, 'selftest%d' % n)
        if answer != expected:
            ok, how = False, 'Z3 answers %s' % answer
        elif answer == 'unsat':
            closer, kind, _, _ = leanvc.scaffold(tr.statement(), stem)
            ok, how = closer is not None, 'theorem proved by %s (%s)' % (closer, kind)
        else:
            model = solver.model()
            values = {}
            for d in model.decls():
                interp = model[d]
                values[d.name()] = lean_function(interp, model) if d.arity() else \
                    lean_value(interp, model)
            for c in tr.consts:               # a constant the model leaves free
                values.setdefault(smt2lean.name(c), 'default')
            values = {c: values[smt2lean.name(c)] for c in tr.consts}
            with open(stem + '.lean', 'w', encoding='utf-8') as f:
                f.write(smt2lean.PRELUDE + tr.evaluation(values))
            _, out = leanvc.lean(stem + '.lean', 300)
            ok, how = (out or '').strip() == 'true', 'Z3 model evaluates in Lean to %s' % (
                (out or 'a timeout').strip()[:200])
        failed += not ok
        print('%s  %-5s %-70s %s' % ('ok  ' if ok else 'FAIL', expected, title, how))
    print('self-test: %d of %d pass' % (len(CASES) - failed, len(CASES)))
    return 1 if failed else 0
