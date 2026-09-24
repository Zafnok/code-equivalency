# Tickets

SonarQube debt is the one kind of work that does not live here: it is filed as GitHub issues
labelled `sonar`, whose body carries the goal, findings and acceptance criteria. See
`docs/adr/0016-sonar-issue-triage.md` and `.claude/skills/equiv-sonar-fix`.

One file per ticket, named `M<n>-<nnn>-<slug>.md`. Status is the first line after the
title: `Status: todo | in-progress | done (PR #n)`. Agents update it in the PR.

Open tickets live directly in this folder. When a ticket's PR is opened, its last commit
sets `Status: done (PR #n)` and moves the file to `done/` (step 8 of
`.claude/skills/equiv-task-loop`), so on `main` this folder lists only unfinished work and
`done/` is the archive. A dependency is met when its file is in `done/`.

Template:

```
# M1-002 IR types, interpreter, dump format
Status: todo
Effort: L
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: M0-003

## Goal
One paragraph. What exists when this is done, and roughly how big it is.

## Spec references
VERIFICATION-MODEL.md section 2, section 7

## Acceptance criteria (all must hold; nothing beyond them)
1. Numbered, testable statements. Each names a file, type, command or observable
   output. "Works" and "handles" are not criteria.

## Files
The exact files to create or edit. A file not listed is a scope question.

## Tests
Test names. A behaviour without a named test is not required.

## Size guard
A number of files or tests that means "you have misread the ticket" when exceeded.

## Out of scope
What a tempted agent must not do here.

## Notes
Left empty by the author; the implementer records surprises and Decision: lines.
```

The `Model:` line is mandatory and is checked first by the task-loop skill. It names
the least capable model family and effort the ticket was planned for. An agent of a
weaker family than named, or of the named family below the named effort, must stop
before reading further and ask the user to switch; it must not "give it a try".
Effort L tickets are the design-heavy ones and carry a Design section with the
intended algorithm, types and pitfalls; S and M tickets are sized for Sonnet.

Tickets leave implementation details open on purpose. An agent settles them with the
`equiv-decide` skill and a `Decision:` line in Notes; it does not ask.

Every ticket is written in full: the Acceptance criteria are the definition of done,
the Files and Tests lists are the expected shape, and the Size guard is the signal to
stop and re-read. Effort labels are calibrated to those lists, not to how hard the
topic sounds. An agent does not expand or re-plan a ticket; it implements the criteria
in order and logs `Decision:` lines for anything the criteria leave open.
