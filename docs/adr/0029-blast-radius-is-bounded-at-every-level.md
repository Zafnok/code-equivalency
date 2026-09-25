# ADR 0029: A failure's blast radius is the smallest unit it touches, and every Unknown says which

Status: accepted (2026-09-23)

## Context
An Unknown is only useful if it is small. Several paths make it large:
- **Solution.** M2-001's loader aborts the whole run with exit 4 on any unresolved reference, any
  workspace failure, or any project that is not C#. In `MsBuildSolutionLoader.ThrowIfAborting`,
  one `.vcxproj`, `.wixproj` or broken test project in a 60-project solution means no result at
  all. Git Extensions, the first corpus pair (ADR 0028), ships a C++ shell extension.
- **Method.** `IrLowerer.Lower` makes the whole body one `IrOpaque` when it contains `foreach`,
  `using`, `lock`, a `catch` filter or a bare `catch`. It does the same for a body that is not an
  `IMethodBodyOperation` (constructors, arrow-bodied members, accessors). That opaque's span is the
  whole body, so M3-016's line pointer points at the whole method.
- **Implicit.** Under ADR 0014 an Unknown(opaque) already implies a claim: query 1 proved that no
  input avoiding the opaque nodes diverges. The SARIF drops that claim, so a reader cannot tell a
  one-line Unknown from a whole-method Unknown.

Already contained: a crashing pair (ADR 0023), an ambiguous overload (VERIFICATION-MODEL section
4), a callee's verdict (ADR 0019 names callee assumptions instead of propagating Unknown), and
Unknowns that point at lines (ADR 0027, M3-016).

## Decision
The containment ladder is **project, then method, then line**. Nothing in the pipeline fails per
file: Roslyn binds each method on its own, and a syntax error surfaces as diagnostics on the
methods it breaks. So there is no file level.

1. **Project, not solution.** A C# project that fails to load, or has unresolved references, is
   skipped. So is a project that is not C#. Each skipped project gets:
   - a `toolExecutionNotifications` entry naming the project and the diagnostics;
   - its procedures listed in `run.properties.unverified`, with baseline results carried as
     `unchanged` with `properties.unverified: true` (as ADR 0023 does for a crash);
   - no EQ004 or EQ005 for procedures whose counterpart project, matched by assembly name, was
     skipped on the other side.

   The run writes every other result, and then exits 4. Exit 5 outranks 4, and both outrank 1 and
   2, because tool faults outrank verdicts. Exit 4 with no SARIF at all remains only for "no C#
   project loaded on some side". The census gains `projectsSkipped: {legacy, modern}`.
2. **Unbound code is a method-level Unknown.** When loading is allowed to degrade, a method whose
   bound body carries an error diagnostic, or references an error-type symbol, is
   `Unknown(Unbound)`. Its causes are the diagnostics' spans. It is never Equivalent by
   congruence, because two error symbols with the same name are not evidence of the same
   behaviour.
3. **A whole-body opaque points at its construct.** While a construct still makes the whole body
   opaque, the `IrOpaque` span is the first offending operation (the `foreach`, the `lock` or the
   filtered `catch`), not the body. The census keeps counting `pairsWholeBodyOpaque` by reason.
   `business-layer`'s count may never rise, and every whole-body reason has an owning ticket until
   the count reaches zero:
   - `foreach`, `using` and constructors: M4-001;
   - `lock`: M4-003;
   - `catch` filters, bare `catch`, arrow-bodied members and accessors: M4-008.
4. **Every Unknown states its scope and its residual claim.** Each Unknown result carries
   `properties.scope`, which is one of:
   - `line`: every cause is a sub-method span, and ADR 0014 query 1 was unsatisfiable;
   - `method`: a whole-body opaque, a timeout, an exhausted loop ladder, or unbound code;
   - `project`: the pair sits in a skipped project (listed only, never a result).

   A `line`-scoped Unknown also carries `properties.residualClaim: "equivalent unless a
   relatedLocation is reached"`, and its message says the same. `line` means the tool proved every
   other path. With a verdict run, the census gains `unknownByScope`.
5. **No run-level budget produces Unknowns.** Solver timeouts are per pair (M3-002). If a run-level
   time budget is ever added, the pairs it does not reach are listed in `properties.unverified`
   and are not reported as Unknown results, so a slow pair cannot turn a whole solution Unknown.

## Why
- A reviewer's cost is the lines they must read (ADR 0027). A `line` Unknown with a residual claim
  asks them to check one construct, while a `method` Unknown asks them to reread the method. The
  two must be distinguishable to be triaged.
- The residual claim already follows from ADR 0014's two queries. Reporting it costs no solver
  time.
- On real solutions, all-or-nothing loading is the largest blast radius in the system, and it is
  the likeliest first result of the corpus run.
- Keeping unbound code out of congruence keeps it sound, which is what made the hard abort
  necessary in the first place.

## Rejected
- **Keep the hard abort and ask users to fix their solution.** The corpus cannot be edited, and a
  real migration branch is often half-broken on purpose.
- **Treat a skipped project's procedures as Unknown results.** That puts hundreds of results in
  front of a reviewer for one fact about the tool, and ADR 0023 already has the shape for this
  (`unverified`).
- **A `file` scope.** No failure mode is per file, so it would be an empty category.
- **Narrow a whole-body opaque by lowering around the construct as a region opaque with several
  exits.** It needs an IR change (outputs for every written local and heap map). Lowering the
  constructs properly (M4-001, M4-003, M4-008) costs about the same and removes them.

## Consequences
- Tickets: M3-024 (project containment, unbound methods), M3-025 (scope, residual claim,
  construct spans) and M4-008 (the whole-body reasons M4-001 and M4-003 do not cover). M3-015
  excludes unbound bodies from congruence. M3-003 depends on M3-024 and M3-025, and M3-022
  depends on M3-024.
- ARCHITECTURE.md (exit code 4 and its precedence) and VERIFICATION-MODEL sections 1, 6 and 7
  (skipped projects, `Unbound`, scope, residual claim) changed in the PR that accepted this ADR.
- M2-001's "a partial load is a hard failure" no longer holds. The loader test that expects an
  abort on a missing reference changes to expect a skipped project.

## Clarifications
- 2026-09-25 (M3-025). Decision 3's owners, for the whole-body reasons the list did not name: M4-001
  left `field-initializer` (a constructor that omits its type's initializers) and
  `ConstructorBodyOperation` (a static constructor, a primary constructor with base arguments)
  whole-body, and `async` is M4-006's. M4-008 owns the two constructor reasons with the rest. Its
  span is the first omitted initializer. `unbound` is not a lowering gap (decision 2) and has no
  removing ticket; the owners table lists it under M3-024.
- 2026-09-25 (M3-025). Decision 4's `line` needs the first query over every input, so an Unknown on
  a pair with a loop or self-call, whose rung 1 query runs on the unrolled pair, is `method`. So is
  an `abstraction` Unknown: its candidate came from that query, which was satisfiable.
