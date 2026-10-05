# P1-037 lean-vc spike (throwaway)

Measures whether a rung 1 query the solvers give up on can be closed as a Lean theorem: by tactics
that need no insight (the scaffold), or by a model whose proof Lean then checks. The result is in
`docs/runs/2026-10-04-lean-vc.md`. Nothing here is built by CI, and nothing under `src/` changes.

## What is translated

The query, not the program. The input is one of the SMT-LIB files P1-034 wrote: the product
encoding's assertions and the query's terms, with the call trace compared by position, so the file
holds bit-vectors, arrays, uninterpreted functions and uninterpreted sorts and no sequence, datatype
or integer. `smt2lean.py` writes one Lean file:

```lean
open Classical in
theorem q
  {«S» : Type} [DecidableEq «S»]        -- one per declare-sort
  («c» : BitVec 32) («f» : BitVec 32 → «S» → Bool) ...   -- one per declare-fun
  (h0 : A0 = true) (h1 : A1 = true) ...  -- one per assert
  : False := by
```

That is `not (assertions and query)` with every constant universally quantified and the conjunction
curried into hypotheses. An SMT-LIB symbol keeps its own name, between guillemets. A `Bool` term is
a Lean `Bool`, so the same assertion text can be evaluated (`--self-test`).

| SMT-LIB | Lean | occurrences in the 142 files | files |
|---|---|---|---|
| `bvadd` | `a + b` | 1,808,706 | 142 |
| `=` | `a == b` (for more than two arguments, each adjacent pair, joined by `&&`) | 1,795,611 | 142 |
| `and` | `a && b` | 1,380,119 | 142 |
| `not` | `!a` | 713,216 | 142 |
| `ite` | `bif c then a else b` | 584,826 | 142 |
| `let` | `let x := e; body` (Lean binds in sequence; a parallel `let` that would differ is refused) | 196,497 | 142 |
| `=>` | `!a \|\| b` | 51,049 | 139 |
| `select` | application, `a i` | 15,994 | 142 |
| `or` | `a \|\| b` | 4,007 | 142 |
| `store` | `Smt.store a i v`, defined as `fun k => if k = i then v else a k` | 2,798 | 90 |
| `(as const (Array A B))` | `fun _ => v` | 608 | 58 |
| `bvsge` | `BitVec.sle b a` | 492 | 30 |
| `bvslt` | `BitVec.slt a b` | 362 | 20 |
| `bvsub` | `a - b` | 260 | 16 |
| `bvuge` | `BitVec.ule b a` | 228 | 19 |
| `distinct` | `a != b` for every pair, joined by `&&` | 156 | 113 |
| `(_ zero_extend k)` | `BitVec.zeroExtend (w + k) a`, `w` the argument's width | 148 | 5 |
| `bvsgt` | `BitVec.slt b a` | 112 | 23 |
| `bvshl` | `a <<< b` (zero when the shift is the width or more) | 86 | 2 |
| `bvsdiv` | `BitVec.smtSDiv a b` (by zero: 1 for a negative `a`, else all ones) | 86 | 2 |
| `bvsle` | `BitVec.sle a b` | 22 | 6 |
| `bvmul` | `a * b` | 20 | 4 |
| `(_ extract i j)` | `BitVec.extractLsb i j a` | 18 | 2 |
| `bvor` | `a \|\|\| b` | 16 | 2 |
| `(_ sign_extend k)` | `BitVec.signExtend (w + k) a` | 14 | 1 |
| `bvneg` | `-a` | 2 | 1 |
| `#x…`, `#b…`, `(_ bvN w)` | `0x…#w`, `0b…#w`, `N#w` | not counted | 142 |
| `true`, `false` | `true`, `false` | not counted | 142 |
| an applied declared function | application | not counted | 142 |

Translated and checked by `--self-test`, and in none of the 142 files: `bvudiv` (`BitVec.smtUDiv`),
`bvurem` (`%`), `bvsrem` (`BitVec.srem`), `bvsmod` (`BitVec.smod`), `bvlshr` (`>>>`), `bvashr`
(`BitVec.sshiftRight'`), `bvand`, `bvxor`, `bvnot`, `concat`, `bvult`, `bvule`, `bvugt`. `xor`
(`Bool.xor`) is translated, in none of the files, and not checked.

Sorts: `Bool` is `Bool`, `(_ BitVec n)` is `BitVec n`, `(Array A B)` is the function type `A → B`,
and a declared sort is a type variable with decidable equality. `=` on arrays is function equality,
which is extensional as SMT-LIB's is; it is decidable only classically, hence `open Classical`.

Not translated, and reported as such: a sort with parameters, a quantifier, any theory other than
the ones above (integers, sequences, datatypes, strings, floating point), any indexed operator
other than `extract`, `zero_extend` and `sign_extend`, and a parallel `let` that rebinds a name one
of its own bound terms reads. The sequence-encoded files (`NNN.seq.smt2`) are not read: the ticket's
note prefers the positional ones, so the spike has no `List` or inductive `Event`.

The translation is trusted: a wrong mapping proves a different theorem. `--self-test` is the guard.

## The scaffold, the model, and what is admitted

- **Scaffold.** Each of `bv_decide`, `grind`, `simp_all`, `simp_all <;> grind`, `bv_omega` and
  `omega` is tried as the whole proof, for 120 seconds beyond what the statement alone takes to
  elaborate. The hypotheses are already introduced by the theorem's binders; `grind` and
  `bv_decide` split the boolean structure the encoding makes per path themselves.
- **Model.** When no tactic closes the theorem, a model is given the file and the last error and
  asked for the tactic block, at most five rounds, each answered with Lean's error. It is called
  through the `claude` command line with no tools (`claude -p --model opus --tools ""`), which
  reports the tokens. A query some solver answers satisfiable is not sent, and neither is a theorem
  over 1,500,000 characters.
- **Admitted** only when Lean compiles the file with no error and no `sorry`, the proof text holds
  no command and none of `sorry`, `native_decide` and the like, and `#print axioms q` lists nothing
  beyond `propext`, `Classical.choice` and `Quot.sound`. The harness writes the statement itself and
  appends the proof after `:= by`, so the statement is the generated one byte for byte.
- **`bv_decide` is reported apart, and is not kernel-checked.** In Lean 4.34.1 a `bv_decide` proof
  depends on a fourth axiom, `q._native.bv_decide.ax_…`: the SAT certificate is checked by compiled
  code, which the kernel trusts. Such a proof is counted as `bv_decide`, never as `kernel`.

## Reproduce

Windows. Everything installed lives under `.corpus/` and none of it is committed.

```powershell
# Lean 4.34.1 through elan 4.2.4 (elan-x86_64-pc-windows-msvc.zip, SHA-256
# fad2e980a191c15884cc1d80d170ffc5fa84f3774541020145b66d1a644c6111)
$env:ELAN_HOME = "$PWD\.corpus\elan"
.\elan-init.exe -y --no-modify-path --default-toolchain leanprover/lean4:v4.34.1
# Z3 for the self-test only
python -m venv .corpus\lean-vc\venv
.corpus\lean-vc\venv\Scripts\python -m pip install z3-solver==5.1.0.0
```

```powershell
$py = '.corpus\lean-vc\venv\Scripts\python'
& $py tools/spikes/lean-vc/leanvc.py --self-test
& $py tools/spikes/lean-vc/leanvc.py --control
# <smt> is P1-034's output directory: NNN.pos.smt2 and results.tsv. It and the output
# directory must be under this checkout's .corpus/; the tool refuses any other path.
& $py tools/spikes/lean-vc/leanvc.py --run <smt> <smt>/results.tsv .corpus/lean-vc/run --threads 8
```

`--no-model` stops after the scaffold and `--only 58,103` keeps those positions. The output
directory gets one Lean file per query and attempt, the model's answers, and `lean-vc.jsonl`. They
hold names and constants from the analysed code, so point it at `.corpus/`, never at `docs/`.

- `sexp.py`: SMT-LIB s-expressions.
- `smt2lean.py`: the translator.
- `leanvc.py`: Lean runs, the admission check, the scaffold, the model loop, the run.
- `selftest.py`: ten small queries Z3 decides, five each way. For an unsatisfiable one the scaffold
  must prove the theorem. For a satisfiable one Z3's model is written as Lean definitions and Lean
  must evaluate the assertions to `true`. The first holds every bit-vector operator on operands at
  the edges (a zero divisor, a shift past the width, the sign bit), each equated with what Z3
  computes for it.
