---
name: equiv-task-loop
description: The only way to implement a ticket in this repo. Use whenever asked to work on, pick up, start, or finish a ticket (M0-001 ... M3-005, P1-001 ...) or any change to src/ or tests/.
---

# Ticket loop (definition of done)

1. Read, in order: `CLAUDE.md`, the ticket file in `docs/tickets/`, every spec section it
   references, `docs/QUALITY-GATES.md`. Do not read the whole repo; read what the ticket names.
2. Check dependencies: every ticket in "Depends on" must be `Status: done`. If not, stop
   and say which one is missing.
3. Expand the ticket if its Deliverables still say "expand into concrete items": rewrite
   them as checkboxes naming files, types and test names. Commit that first
   (`docs(tickets): expand M2-003`). This is the design step; keep it under 40 lines.
4. Branch: `git switch -c <ticket-id>-<slug>` from `main`. Set `Status: in-progress`.
5. Test first, per deliverable: write the failing test (unit, and property/snapshot if
   the ticket says so), then the smallest code that passes, then run `./build.ps1`.
   Never write more than one deliverable ahead of green.
6. Gates: `./build.ps1` must be fully green before every commit: build with warnings as
   errors, format, tests, 100% line+branch coverage on `src/`, architecture tests. Do not
   lower a gate, add an exclusion, or disable an analyzer to get green. If a gate is
   wrong, stop and report; do not work around it.
7. Commit small, Conventional Commits, footer `Ticket: <id>`. Snapshot files
   (`*.verified.*`) are committed and described in the PR.
8. Finish: tick every checkbox, fill Notes with anything surprising (toolchain quirks,
   spec ambiguities), open the PR with `gh pr create` using the ticket title; body = the
   Deliverables list with results, plus the attribution line from the session. Then set
   `Status: done (PR #n)` in a final commit.
9. Stop after the PR. Do not start the next ticket in the same session.

## Rules that trip agents up

- `Equiv.Core` must never gain a reference to Roslyn or Z3. The architecture test will
  fail; do not "fix" the test.
- Unsupported constructs in the frontend become `IrOpaque`, never `throw NotSupported`.
- Do not create helper projects, shared test-utility packages, or "Common" folders.
  Test helpers live in the test project that uses them.
- No `Console.WriteLine` outside `Equiv.Cli`. Use the abstraction the ticket names.
- If the toolchain misbehaves for more than about 15 minutes, record what you saw in
  the ticket's Notes and stop.
