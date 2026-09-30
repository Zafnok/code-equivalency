# P1-019 abstraction-refinement spike: gitextensions-8522 (2026-09-30)

Question: of P2-046's `abstraction` Unknowns, how many would ARDiff-style refinement resolve, and in
which direction? Refinement here means giving the abstraction that tainted the candidate its real
meaning on the pair and querying again.

**Answer: 7 of 726 Unknowns (1.0%), all 7 to Divergent.** That is 2.9% of the 238 `abstraction`
Unknowns. ADR 0028's bar is 5% of Git Extensions' Unknowns (37 results), so criterion 4 adds a
measured line to ROADMAP's post-MVP list and no ADR is written. Only 7 results hold a closed-form
kind (`IntPtr` `==` and `!=`). The other 231 are tainted by kinds the ticket counts but does not
re-query: 195 by an `opaque:` fragment, 38 by floating point, 14 by `string` and 18 by another
user-defined operator. A result can hold kinds from several classes.

## Setup
- Pair: human, gitextensions/gitextensions PR #8522, legacy 3f4ed21998af, modern 5190ba5c1a5f
  (`tools/corpus/pairs.csv`).
- SARIF: P2-046's `full` run of that pair (equiv `bd8e379`, `runs/20260929-1918-full-rerun`),
  with 726 EQ003 results, 238 of them `unknownReason: abstraction`.
- equiv: this branch's `main` (`fd72a61`), win-x64, loaded through the production frontend with
  the default config (bound 3, 5,000 ms). This is the config the run used.
- Tool: `tools/spikes/abstraction-refinement/` (about 470 lines). Wall-clock 94 s to 104 s, almost all
  of it loading and lowering. Each re-query took under 0.5 s. Two runs printed identical tables.
- Command: see the tool's README:
  `dotnet <spike> <run>/equiv.sarif <legacySolution> <modernSolution>`. `--self-test` passes. It
  checks two hand-written pairs, `a == b` against `!(a != b)` and `a == b` against `a != b`. Both
  are Unknown(abstraction) with the operators shared. Interpreted, the first is Equivalent and the
  second Divergent.

## Method
**Kinds.** Each entry of a result's `properties.abstractions` is one kind: an `IrPure` function
name, or `opaque:<reason>`. The SARIF carries only an `opaque:` entry's fingerprint (P2-062). The
spike therefore names each fragment's reason from the `IrOpaque` with that fingerprint in the pair's
lowered bodies. Two results' fragments are not found at `fd72a61`, because the frontend has changed
since `bd8e379`. They are listed as `opaque:(fingerprint not found at this commit)`.

**Closed-form kinds.** ADR 0025's catalogue holds only `f32.*`, `f64.*`, `dec.*` and `conv.*`
functions, each of which takes or yields `float`, `double` or `decimal`, plus `op:` user-defined
operators. Integer arithmetic, integer comparisons and `bool` logic already lower to `IrBinary` and
never taint. The closed-form `IrPure` kinds are therefore the `op:` operators whose meaning is
already in the IR's own theory. On this corpus that is `IntPtr` `==` and `!=`. `IntPtr` lowers to an
uninterpreted sort, and its equality is value equality on that sort, on both runtimes, with no
exception. `UIntPtr` is in the table too but does not occur. Three other kinds were left out:
- `System.Type ==`: .NET's body falls back to a virtual `Equals`, so it is not reference equality.
- `ObjectId ==`: its meaning is a method body in Git Extensions.
- `DateTime -`: it works on a field of an uninterpreted struct.

Floating point, `decimal` and `string` are post-MVP theories (size guard). They are counted, not
encoded.

**Re-query.** In each pair holding a closed-form kind, every such `IrPure` becomes its `IrBinary`
(`eq` or `ne`), and its exception flag becomes `false`. The pair then goes to `Z3Backend.Verify`
twice with the run's options: once as lowered, with every abstraction shared, and once refined. The
first query checks that the pair is still Unknown(abstraction) at `fd72a61`, and all 7 are. A
Divergent result is reported only when its replay is untainted (ADR 0026), so after refinement it
is exact. It is split into EQ002 and EQ006 the way the SARIF writer splits it: EQ006 when a
runtime-changed callee is in the trace.

## Kind by outcome
Results holding each kind (a result can hold several), with shares of the 238 `abstraction`
Unknowns. Only closed-form kinds are re-queried. No re-query was Equivalent, still Unknown or a
timeout. Those columns are 0 in every row and are left out.

| Kind | Class | Results | Share | Entries | Divergent after refinement | Not re-queried |
|---|---|---|---|---|---|---|
| `opaque:DelegateCreation` | opaque | 93 | 39.1% | 206 | 0 | 93 |
| `opaque:switch-pattern` | opaque | 61 | 25.6% | 144 | 0 | 61 |
| `opaque:InterpolatedString` | opaque | 21 | 8.8% | 41 | 0 | 21 |
| `opaque:DefaultValue` | opaque | 14 | 5.9% | 30 | 0 | 14 |
| `conv.i32.f32` | floating point | 14 | 5.9% | 27 | 0 | 14 |
| `op:System.String::op_Equality(string,string)` | string | 13 | 5.5% | 22 | 0 | 13 |
| `conv.f64.i32` | floating point | 11 | 4.6% | 21 | 0 | 11 |
| `opaque:Conversion` | opaque | 10 | 4.2% | 22 | 0 | 10 |
| `f32.mul` | floating point | 10 | 4.2% | 20 | 0 | 10 |
| `conv.f32.i32` | floating point | 10 | 4.2% | 19 | 0 | 10 |
| `op:GitExtUtils.ArgumentString::op_Implicit(GitExtUtils.ArgumentBuilder)` | user operator | 9 | 3.8% | 18 | 0 | 9 |
| `opaque:Binary` | opaque | 8 | 3.4% | 20 | 0 | 8 |
| `f32.div` | floating point | 6 | 2.5% | 10 | 0 | 6 |
| **`op:System.IntPtr::op_Equality(System.IntPtr,System.IntPtr)`** | **closed form** | **6** | **2.5%** | 9 | **6 (2.5%)** | 0 |
| `opaque:ArrayCreation` | opaque | 5 | 2.1% | 9 | 0 | 5 |
| `conv.i32.f64`, `f32.add`, `f32.sub` | floating point | 4 each | 1.7% each | 8 each | 0 | 4 each |
| `opaque:Tuple` | opaque | 4 | 1.7% | 5 | 0 | 4 |
| `opaque:ArrayElementReference` | opaque | 3 | 1.3% | 12 | 0 | 3 |
| `conv.f32.f64` | floating point | 3 | 1.3% | 6 | 0 | 3 |
| `op:GitExtUtils.ArgumentString::op_Implicit(string)` | user operator | 3 | 1.3% | 6 | 0 | 3 |
| `opaque:(fingerprint not found at this commit)` | opaque | 2 | 0.8% | 6 | 0 | 2 |
| `conv.i64.f64`, `conv.u8.f32`, `f32.eq`, `f32.gt`, `f64.div`, `f64.mul` | floating point | 2 each | 0.8% each | 3 to 4 | 0 | 2 each |
| `op:` `Point` to `PointF`, `Rectangle` to `RectangleF`, `SizeF +`, `Type ==` | user operator | 2 each | 0.8% each | 3 to 4 | 0 | 2 each |
| `conv.f32.u8`, `conv.f64.i64`, `conv.u8.f64`, `f32.le`, `f32.lt`, `f32.ge`, `f64.add`, `f64.sub` | floating point | 1 each | 0.4% each | 1 to 2 | 0 | 1 each |
| `op:` `DateTime -`, `ObjectId ==`; `op:System.String::op_Inequality(string,string)` | user operator; string | 1 each | 0.4% each | 1 to 2 | 0 | 1 each |
| **`op:System.IntPtr::op_Inequality(System.IntPtr,System.IntPtr)`** | **closed form** | **1** | **0.4%** | 2 | **1 (0.4%)** | 0 |
| `opaque:PropertyReference`, `opaque:SizeOf`, `opaque:TypeOf`, `opaque:ref-argument` | opaque | 1 each | 0.4% each | 2 each | 0 | 1 each |

By class:

| Class | Results holding it | Share of `abstraction` Unknowns | Results holding only it | Re-queried |
|---|---|---|---|---|
| closed form (`IntPtr ==`, `!=`) | 7 | 2.9% | 7 | yes: 7 Divergent |
| opaque fragment | 195 | 81.9% | 166 | no |
| floating point | 38 | 16.0% | 24 | no |
| other user-defined operator | 18 | 7.6% | 2 | no |
| `string` | 14 | 5.9% | 7 | no |
| `decimal` | 0 | 0.0% | 0 | no |

All 238 are `method`-scoped (ADR 0029).

## Ten most frequent kinds

| # | Kind | Results | Entries |
|---|---|---|---|
| 1 | `opaque:DelegateCreation` | 93 | 206 |
| 2 | `opaque:switch-pattern` | 61 | 144 |
| 3 | `opaque:InterpolatedString` | 21 | 41 |
| 4 | `opaque:DefaultValue` | 14 | 30 |
| 5 | `conv.i32.f32` | 14 | 27 |
| 6 | `op:System.String::op_Equality(string,string)` | 13 | 22 |
| 7 | `conv.f64.i32` | 11 | 21 |
| 8 | `opaque:Conversion` | 10 | 22 |
| 9 | `f32.mul` | 10 | 20 |
| 10 | `conv.f32.i32` | 10 | 19 |

## The seven re-queried results
Every one was Unknown(abstraction) with the abstraction shared, both in P2-046 and at `fd72a61`.
The "why" column gives the first observable that differs, by callee identity only.

| Procedure | Kind | Refined | Why |
|---|---|---|---|
| `EasyHook.LocalHook::GetProcAddress(string,string)` | `IntPtr ==` | Divergent, EQ002 | the legacy side alone calls `EasyHook.NativeAPI::GetModuleHandle(string)` |
| `EasyHook.LocalHook::get_HookBypassAddress()` | `IntPtr ==` | Divergent, EQ002 | the legacy side alone calls `EasyHook.NativeAPI::LhGetHookBypassAddress(System.IntPtr,out System.IntPtr)` |
| `EasyHook.LocalHook::IsThreadIntercepted(int)` | `IntPtr ==` | Divergent, EQ002 | the legacy side alone calls `EasyHook.NativeAPI::LhIsThreadIntercepted(System.IntPtr,int,out bool)` |
| `EasyHook.LocalHook::Dispose()` | `IntPtr ==` | Divergent, EQ002 | second call: `EasyHook.NativeAPI::LhUninstallHook(System.IntPtr)` against `System.Runtime.InteropServices.Marshal::FreeCoTaskMem(System.IntPtr)` |
| `EasyHook.HookAccessControl::SetInclusiveACL(int[])` | `IntPtr ==` | Divergent, EQ002 | the legacy side alone calls `System.Array::get_Length()` |
| `EasyHook.HookAccessControl::SetExclusiveACL(int[])` | `IntPtr ==` | Divergent, EQ002 | the legacy side alone calls `System.Array::get_Length()` |
| `GitUI.HelperDialogs.FormStatus::BitmapToIcon(System.Drawing.Bitmap)` | `IntPtr !=` | Divergent, EQ006 | second call, `System.Drawing.Icon::FromHandle(System.IntPtr)` (runtime-changed), gets other arguments or heap |

These divergences are real. The migration dropped the `EasyHook` project reference and copied the
file into `GitUI/Theming/LocalHook.cs`. There these bodies sit under `#if SUPPORT_THEMES`, which the
modern build does not define, so the modern methods skip the native calls.
The six EQ002 results report that change. Only the `IntPtr == IntPtr.Zero` guard in front of the
legacy calls stopped the solver from reporting it: the guard was shared and uninterpreted, so
the replay of the candidate was tainted. `BitmapToIcon`'s source is identical on both sides. Its
refined result is the same kind of EQ006 the run already reports 275 times: a runtime-changed
`System.Drawing` callee reached an observable. Refinement did not produce a spurious result.

## Reading
- **Direction.** On this corpus, refining a closed-form abstraction turns Unknowns into Divergent
  results, not into Equivalent ones. Sharing only hides a divergence when the two sides already
  differ somewhere else. Here they did, in six cases through conditional compilation. The shared
  function was never the thing that differed.
- **Size.** Refinement in the IR's current theories can reach 7 of 726 Unknowns (1.0%), below the
  bar. It would change ADR 0025's shared-function rule for two operators and resolve seven results,
  so no ADR is proposed.
- **What is outside the IR's theories.** 43 of the 238 results (5.9% of all Unknowns) hold no
  opaque fragment. That is the ceiling for any refinement of `IrPure` functions, including the 7
  above. Everything past the 7 needs a post-MVP theory: 24 need only IEEE floating point, 7 only
  `string`, 2 only operator bodies, and 3 more a mix of these. Whether those would resolve is not
  measured. Floating-point conversions to integers are runtime-sensitive (ADR 0025), so their
  refinement could only ever move toward Divergent.
- **Where the `abstraction` Unknowns really are.** 195 of 238 hold an `opaque:` fragment. The
  fragments are the reasons the census already ranks: `DelegateCreation` (P2-060), `switch-pattern`,
  `InterpolatedString` and `Conversion` (ADR 0039's IL fallback). Those tickets, not refinement,
  are what would lower the method-scoped share.
- **ServiceAnt.** P2-046's ServiceAnt run has 2 Unknowns and no `abstraction` result (M4-007 had 7
  of 18), so there is nothing to re-query there.
