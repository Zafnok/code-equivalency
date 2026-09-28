# P2-040 `compare --execute` runs solution code with the caller's working directory
Status: todo
Effort: S
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M4-009, P1-008

## Goal
M4-007's `--execute` run on Git Extensions left eight new directories in the repository checkout
it was launched from, all created within 0.4 seconds at 20:46:16, seven holding two files each (a
garbage-named file and the same name plus `.backup`) and one empty. Their names were random
non-ASCII strings. The likely source is settings-save code in the solution under test
(`SaveImpl` writing a file and a `.backup` copy) called by differential testing with generated
string inputs used as a path, relative to the current directory. The drivers inherit the working
directory of `equiv`, so relative writes land in whatever directory the user ran the command from,
here the repository root, where `git status` listed them as untracked.

ADR 0035 already says `--execute` "runs code from both solutions on this machine". The note it
prints does not say that the code can write next to the caller.

## Spec references
ADR 0035 consequences; `src/Equiv.Execute` (driver launch); `docs/VERIFICATION-MODEL.md` replay and
differential-testing sections.

## Acceptance criteria (all must hold; nothing beyond them)
1. Every driver process starts with its working directory set to a fresh folder under the
   temporary folder `--execute` already creates (`equiv-execute-*`), so a relative write lands
   there and is deleted with it.
2. A test with a driver stub that writes a relative file: after the run, the caller's directory is
   unchanged.
3. The note `--execute` prints says the code runs with a temporary working directory and is not
   sandboxed (absolute paths, the registry and the network are still reachable).

## Size guard
Working directory only. A real sandbox (a job object, a container, a low-integrity process) is a
separate ADR-sized decision.

## Out of scope
Blocking absolute-path writes, the registry or the network.

## Notes
