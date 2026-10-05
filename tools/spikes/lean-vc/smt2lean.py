"""Translates one quantifier-free SMT-LIB query into one Lean 4 theorem.

The theorem says the query is unsatisfiable: every declared sort and constant is a binder, every
assertion `A` is a hypothesis `A = true`, and the conclusion is `False`. That is
`not (assertions and query)` with the conjunction curried. The README lists every operator's form.
"""
import collections

from sexp import forms, name


class Unsupported(Exception):
    """The file holds something the translator has no Lean form for."""


PRELUDE = '''import Std.Tactic.BVDecide
set_option maxHeartbeats 0
set_option maxRecDepth 1000000
set_option linter.unusedVariables false

/-- SMT-LIB `store`: the array that holds `v` at `i` and is `a` elsewhere. -/
def Smt.store {α β : Type} [DecidableEq α] (a : α → β) (i : α) (v : β) : α → β :=
  fun k => if k = i then v else a k

@[grind =, simp] theorem Smt.store_apply {α β : Type} [DecidableEq α] (a : α → β) (i : α) (v : β)
    (k : α) : Smt.store a i v k = if k = i then v else a k := rfl
'''

# operator -> (least arguments, most arguments or None, Lean form of the arguments)
INFIX_CHAIN = {'and': '&&', 'or': '||', 'bvadd': '+', 'bvmul': '*', 'bvand': '&&&', 'bvor': '|||',
               'bvxor': '^^^', 'concat': '++'}
BINARY = {
    'bvsub': '({0} - {1})', 'bvudiv': '(BitVec.smtUDiv {0} {1})', 'bvurem': '({0} % {1})',
    'bvsdiv': '(BitVec.smtSDiv {0} {1})', 'bvsrem': '(BitVec.srem {0} {1})',
    'bvsmod': '(BitVec.smod {0} {1})', 'bvshl': '({0} <<< {1})', 'bvlshr': '({0} >>> {1})',
    'bvashr': "(BitVec.sshiftRight' {0} {1})",
    'bvult': '(BitVec.ult {0} {1})', 'bvule': '(BitVec.ule {0} {1})',
    'bvugt': '(BitVec.ult {1} {0})', 'bvuge': '(BitVec.ule {1} {0})',
    'bvslt': '(BitVec.slt {0} {1})', 'bvsle': '(BitVec.sle {0} {1})',
    'bvsgt': '(BitVec.slt {1} {0})', 'bvsge': '(BitVec.sle {1} {0})',
    '=>': '(!{0} || {1})', 'xor': '(Bool.xor {0} {1})', 'select': '({0} {1})',
}
UNARY = {'not': '(!{0})', 'bvnot': '(~~~{0})', 'bvneg': '(-{0})'}


def quote(sym):
    """A Lean identifier for an SMT-LIB symbol: the symbol itself between guillemets."""
    text = name(sym)
    if '»' in text or '\n' in text:
        raise Unsupported('symbol with » or a newline')
    return '«' + text + '»'


class Translator:
    def __init__(self):
        self.sorts = []                       # declared sorts, in order
        self.consts = collections.OrderedDict()   # symbol -> (argument sorts, result sort)
        self.asserts = []                     # Lean text of each assertion
        self.ops = collections.Counter()      # operator -> occurrences, for the README's table
        self.logic = None

    # sorts ---------------------------------------------------------------------------------
    def sort(self, s):
        if isinstance(s, str):
            if s == 'Bool':
                return 'Bool'
            if s in self.sorts:
                return quote(s)
            raise Unsupported('sort ' + s)
        if s[0] == '_' and s[1] == 'BitVec':
            return '(BitVec %d)' % int(s[2])
        if s[0] == 'Array':
            return '(%s → %s)' % (self.sort(s[1]), self.sort(s[2]))
        raise Unsupported('sort ' + str(s[0]))

    def width(self, t, env):
        """The width of a bit-vector term; only `zero_extend` and `sign_extend` ask."""
        s = self.sort_of(t, env)
        if isinstance(s, list) and s[0] == '_':
            return int(s[2])
        raise Unsupported('extension of a term that is not a bit-vector')

    def sort_of(self, t, env):
        bv = lambda n: ['_', 'BitVec', str(n)]
        if isinstance(t, str):
            if t in env:
                return env[t]
            if t in self.consts:
                return self.consts[t][1]
            if t.startswith('#x'):
                return bv(4 * (len(t) - 2))
            if t.startswith('#b'):
                return bv(len(t) - 2)
            return 'Bool'
        h = t[0]
        if isinstance(h, list):
            if h[0] == 'as':
                return h[2]
            if h[1] == 'extract':
                return bv(int(h[2]) - int(h[3]) + 1)
            return bv(self.width(t[1], env) + int(h[2]))
        if h == '_':
            return bv(int(t[2]))
        if h == 'let':
            inner = dict(env)
            for var, bound in t[1]:
                inner[var] = self.sort_of(bound, env)
            return self.sort_of(t[2], inner)
        if h == 'ite':
            return self.sort_of(t[2], env)
        if h == 'select':
            return self.sort_of(t[1], env)[2]
        if h == 'concat':
            return bv(sum(self.width(a, env) for a in t[1:]))
        if h in self.consts:
            return self.consts[h][1]
        if h.startswith('bv') and h not in BINARY or h in ('bvsub', 'bvudiv', 'bvurem', 'bvsdiv',
                                                          'bvsrem', 'bvsmod', 'bvshl', 'bvlshr',
                                                          'bvashr', 'store'):
            return self.sort_of(t[1], env)
        return 'Bool'

    # terms ---------------------------------------------------------------------------------
    def term(self, t, env):
        if isinstance(t, str):
            if t in env or t in self.consts:
                return quote(t)
            if t in ('true', 'false'):
                return t
            if t.startswith('#x'):
                return '(0x%s#%d)' % (t[2:], 4 * (len(t) - 2))
            if t.startswith('#b'):
                return '(0b%s#%d)' % (t[2:], len(t) - 2)
            raise Unsupported('atom ' + t)
        h = t[0]
        args = t[1:]
        tr = lambda a: self.term(a, env)
        if isinstance(h, list):
            if h[0] == 'as' and h[1] == 'const':
                self.ops['(as const)'] += 1
                return '((fun _ => %s) : %s)' % (tr(args[0]), self.sort(h[2]))
            if h[0] == '_' and h[1] == 'extract':
                self.ops['(_ extract)'] += 1
                return '(BitVec.extractLsb %d %d %s)' % (int(h[2]), int(h[3]), tr(args[0]))
            if h[0] == '_' and h[1] in ('zero_extend', 'sign_extend'):
                self.ops['(_ %s)' % h[1]] += 1
                to = self.width(args[0], env) + int(h[2])
                fn = 'BitVec.zeroExtend' if h[1] == 'zero_extend' else 'BitVec.signExtend'
                return '(%s %d %s)' % (fn, to, tr(args[0]))
            raise Unsupported('indexed operator ' + ' '.join(map(str, h[:2])))
        if h == '_' and args[0].startswith('bv'):
            return '(%d#%d)' % (int(args[0][2:]), int(args[1]))
        if h == 'let':
            self.ops['let'] += 1
            inner = dict(env)
            out = []
            for var, bound in args[0]:          # parallel: each bound term sees the outer scope
                out.append('let %s := %s; ' % (quote(var), tr(bound)))
                inner[var] = self.sort_of(bound, env)
            if len(args[0]) > 1 and any(v in str(b) for v, _ in args[0] for _, b in args[0]):
                self.check_parallel(args[0], env)
            return '(' + ''.join(out) + self.term(args[1], inner) + ')'
        if h in self.consts and h not in env:
            if len(args) != len(self.consts[h][0]):
                raise Unsupported('arity of ' + h)
            return '(' + ' '.join([quote(h)] + [tr(a) for a in args]) + ')'
        self.ops[h] += 1
        if h in INFIX_CHAIN and args:
            return '(' + (' %s ' % INFIX_CHAIN[h]).join(tr(a) for a in args) + ')'
        if h in BINARY and len(args) == 2:
            return BINARY[h].format(tr(args[0]), tr(args[1]))
        if h in UNARY and len(args) == 1:
            return UNARY[h].format(tr(args[0]))
        if h == '=' and len(args) >= 2:
            lean = [tr(a) for a in args]
            return '(' + ' && '.join('(%s == %s)' % p for p in zip(lean, lean[1:])) + ')'
        if h == 'distinct' and len(args) >= 2:
            lean = [tr(a) for a in args]
            return '(' + ' && '.join('(%s != %s)' % (a, b) for i, a in enumerate(lean)
                                     for b in lean[i + 1:]) + ')'
        if h == 'ite' and len(args) == 3:
            return '(bif %s then %s else %s)' % tuple(tr(a) for a in args)
        if h == 'store' and len(args) == 3:
            return '(Smt.store %s %s %s)' % tuple(tr(a) for a in args)
        raise Unsupported('operator ' + h)

    def check_parallel(self, bindings, env):
        """A `let` binds in parallel and Lean's in sequence. They differ only when a bound term
        names a variable the same `let` binds, and that variable already had another meaning."""
        bound = {v for v, _ in bindings}
        stack = [b for _, b in bindings]
        while stack:
            x = stack.pop()
            if isinstance(x, str):
                if x in bound and (x in env or x in self.consts):
                    raise Unsupported('a parallel let that rebinds a name it reads')
            else:
                stack.extend(x)

    # commands ------------------------------------------------------------------------------
    def read(self, text):
        for form in forms(text):
            cmd = form[0]
            if cmd == 'declare-sort':
                if form[2] != '0':
                    raise Unsupported('sort with parameters')
                self.sorts.append(form[1])
            elif cmd in ('declare-fun', 'declare-const'):
                args, res = (form[2], form[3]) if cmd == 'declare-fun' else ([], form[2])
                self.consts[form[1]] = (args, res)
                self.sort(res)
                for a in args:
                    self.sort(a)
            elif cmd == 'assert':
                self.asserts.append(self.term(form[1], {}))
            elif cmd == 'set-logic':
                self.logic = form[1]
            elif cmd in ('set-option', 'set-info', 'check-sat', 'get-value', 'get-model', 'exit'):
                pass
            else:
                raise Unsupported('command ' + cmd)
        return self

    def binder(self, sym):
        args, res = self.consts[sym]
        return '(%s : %s)' % (quote(sym), ' → '.join([self.sort(a) for a in args] + [self.sort(res)]))

    def statement(self):
        """The theorem up to and including `:= by`. A proof is whatever follows it."""
        out = ['open Classical in', 'theorem q']
        for s in self.sorts:
            out.append('  {%s : Type} [DecidableEq %s]' % (quote(s), quote(s)))
        out.extend('  ' + self.binder(c) for c in self.consts)
        out.extend('  (h%d : %s = true)' % (i, a) for i, a in enumerate(self.asserts))
        out.append('  : False := by')
        return '\n'.join(out) + '\n'

    def evaluation(self, values):
        """The same assertions over definitions in place of binders: `values` maps each sort and
        constant to Lean text. Lean prints `true` when the values satisfy the query, which
        falsifies the theorem's hypotheses-to-`False`."""
        out = []
        for s in self.sorts:
            out.append('abbrev %s : Type := %s' % (quote(s), values[s]))
        for c in self.consts:
            args, res = self.consts[c]
            ty = ' → '.join([self.sort(a) for a in args] + [self.sort(res)])
            out.append('def %s : %s := %s' % (quote(c), ty, values[c]))
        out.append('#eval (%s)' % ' && '.join('(%s)' % a for a in self.asserts))
        return '\n'.join(out) + '\n'


def translate(text):
    return Translator().read(text)
