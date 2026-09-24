---
name: equiv-task-loop
description: The only way to implement a ticket in this repo. Use whenever asked to work on, pick up, start, or finish a ticket (M0-001 ... M4-007, P1-001 ...) or any change to src/ or tests/.
---

# Ticket loop (definition of done)

0. Model check, before anything else: read the ticket's `Model:` line. If you are a
   weaker model family than it names, or the named family at a lower effort than it
   names, stop immediately and reply with one sentence telling the user which model and
   effort to switch to. Do not read further, do not start, do not "try". Fable may take
   any ticket.
1. Read, in order: `CLAUDE.md`, the ticket file in `docs/tickets/`, every spec section it
   references, `docs/QUALITY-GATES.md`. Do not read the whole repo; read what the ticket names.
2. Check dependencies: every ticket in "Depends on" must be in `docs/tickets/done/` (and
   say `Status: done`). If one is still directly in `docs/tickets/`, stop and say which
   one is missing. Open tickets are exactly the files directly in `docs/tickets/` (not
   `README.md`, not `IOPERATION-COVERAGE.md`); `docs/ROADMAP.md` gives their order.
3. Do not plan. The ticket's Acceptance criteria, Files and Tests sections are the
   plan. Copy the criteria into a checklist in your first message and work them in
   order. If the Size guard trips, stop and re-read Out of scope before writing more.
   Effort labels are calibrated to the Files and Tests lists; a ticket labelled S is
   small no matter how the topic sounds.
4. Branch: `git switch -c <ticket-id>-<slug>` from `main`. Set `Status: in-progress`.
5. Test first, per deliverable: write the failing test (unit, and property/snapshot if
   the ticket says so), then the smallest code that passes, then run the targeted
   `dotnet test <project>` for the project you touched (fast, not the full gate).
   Never write more than one deliverable ahead of green.
   When a choice comes up that the ticket does not settle, do not ask: apply
   `equiv-decide`, log a `Decision:` line in the ticket's Notes, and keep going.
6. Gates: `./build.ps1` (build with warnings as errors, format, tests, 100% line+branch
   coverage on `src/`, architecture tests) must be green before the PR merges. CI runs
   this identically on every PR (`.claude/skills/equiv-quality-gates`), so do not run the
   full `./build.ps1` locally before committing or pushing — trust the CI gate. Only run
   it locally if CI has already failed on the same issue more than once and a local
   debug-mode run is cheaper than another CI round-trip. Do not lower a gate, add an
   exclusion, or disable an analyzer to get green. If a gate is wrong, stop and report;
   do not work around it.
7. Commit small, Conventional Commits, footer `Ticket: <id>`. Snapshot files
   (`*.verified.*`) are committed and described in the PR.
8. Finish: confirm every acceptance criterion holds (quote each with the test or
   command that proves it in the PR body), fill Notes with anything surprising (toolchain quirks,
   spec ambiguities), open the PR with `gh pr create` using the ticket title; body = the
   Deliverables list with results, plus the attribution line from the session. Then, in
   one final commit on the same branch (`chore(tickets): file <id> as done`, footer
   `Ticket: <id>`): set `Status: done (PR #n)` and `git mv` the ticket file into
   `docs/tickets/done/`. Fix any live path to it (`grep -rn "tickets/<id>" .` outside
   `docs/tickets/done/`; done tickets' Notes are history and stay as written). Push. The
   move merges with the PR, so `main`'s `docs/tickets/` only ever lists unfinished work.
9. Stop after the PR. Do not start the next ticket in the same session.

## Rules that trip agents up

- `Equiv.Core` must never gain a reference to Roslyn or Z3. The architecture test will
  fail; do not "fix" the test.
- Unsupported constructs in the frontend become `IrOpaque`, never `throw NotSupported`.
- Do not create helper projects, shared test-utility packages, or "Common" folders.
  Test helpers live in the test project that uses them. The one exception is
  `tests/Equiv.TestSupport` (created in M1-002): IR generators and fixture loaders that
  three test projects need. Nothing else goes there without an ADR.
- No `Console.WriteLine` outside `Equiv.Cli`. Use the abstraction the ticket names.
- If the toolchain misbehaves for more than about 15 minutes, record what you saw in
  the ticket's Notes and stop.
