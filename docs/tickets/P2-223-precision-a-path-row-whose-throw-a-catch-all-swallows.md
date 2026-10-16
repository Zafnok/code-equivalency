# P2-223 Precision: a path row fires where a catch-all swallows the throw and the other runtime reaches only guards that reject the path
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-217

## Goal
P2-212 adjudicated `GitCommands.PathUtil::TryFindShellPath(string,out string)` on `gitextensions-8522` a false EQ006 and
gave it to P2-217, which decided not to take it (P2-217's Notes). The body is the same text on both sides:
`try { shellPath = Path.Combine(<program files>, "Git", shell); if (File.Exists(shellPath)) return true; shellPath =
Combine(AppSettings.GitBinDir, shell); if (File.Exists(shellPath)) return true; if (TryFindFullPath(shell, out
shellPath)) return true; } catch { } shellPath = null; return false;`. For a `shell` with an invalid character, .NET
Framework throws in the first `Path.Combine`, the catch swallows it and the method returns false. .NET 10 runs on: both
`File.Exists` calls return false for that path, and `TryFindFullPath(shell, ...)`, one of the solution's own methods,
decides the result. It too returns false there (`File.Exists(shell)`, then `Combine` and `File.Exists` over the
environment's paths, inside its own catch-all), so both sides return false with `shellPath` null. The solver leaves
`File.Exists` unconstrained and has `TryFindFullPath` called on one side only.

When done, the shape is followed for the characters both runtimes reject, or `equiv-adr` records why it is not.

## Spec references
`docs/tickets/done/P2-217-*.md` (the decision and the length caveat), `P2-074-*.md` (the guards), VERIFICATION-MODEL
section 3, ADR 0019 (callee pairs assumed equivalent).

## Acceptance criteria (all must hold; nothing beyond them)
1. Notes state, per character class of `invalidPath`, whether `File.Exists` returns false on .NET 10 for every such
   path, from the runtime's source or a `tools/runtime-diff` witness. A path of 248 characters or more with no invalid
   character is not one: .NET 10 reads long paths, so `File.Exists` is true for such a file that exists.
2. A path row whose throw a catch-all swallows does not fire when every path the call's argument reaches on the other
   runtime before the catch's join is passed only to listed guards, or to a solution callee that P2-188's rules show
   passes it only to listed guards, for the character classes criterion 1 confirms. Notes give the soundness argument.
3. Rerun `gitextensions-8522` in compare mode quick; Notes give `TryFindShellPath`'s new result.

## Tests
Unit tests: the swallowed shape with only a guard after the call is not flagged; with a call that is not a guard after
it, or with no catch, it is.

## Out of scope
A catch that filters by exception type (the row's exception type would have to match). Paths from callers (P2-207).

## Notes
- Filed 2026-10-15 by P2-217 (criterion 2).
