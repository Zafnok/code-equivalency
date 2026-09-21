# Architecture Decision Records

Short, numbered, immutable once accepted. To change a decision, write a new ADR that
supersedes the old one. Template and procedure: `.claude/skills/equiv-adr/SKILL.md`.

| ADR | Decision |
|---|---|
| 0001 | .NET 10 / C# 14 for the whole MVP |
| 0002 | Dependency register (living) |
| 0003 | Lower from Roslyn's IOperation control-flow graph |
| 0004 | MSBuildWorkspace on Windows for the MVP loader; fact check on "Windows bindings" |
| 0005 | Direct Z3 encoding; Lean and Boogie rejected for the MVP |
| 0006 | SARIF only, headless only, no UI in the MVP |
| 0007 | Test and gate stack |
| 0008 | Loop ladder instead of bounded-only verdicts; runtime-changes table |
| 0009 | SonarQube Cloud as a PR-blocking changegate for code smells and duplication |
| 0010 | Narrow the Equiv.Core Roslyn/Z3 architecture rule to exclude Sarif.Sdk's namespace |
| 0011 | EQ003-EQ005 stay non-`fail` SARIF results; visibility of Unknown is the exit code's job |
| 0012 | Matched pairs are skipped (null backend) until M3-001 wires a real backend |
| 0013 | Lower C# `char` to `BitVec(16)`, not `BitVec(32)` |
| 0014 | Reaching an `IrOpaque` makes that input's outcome unknown |
| 0015 | A call's heap effect and array aliasing are named limits, not defects |
| 0016 | SonarQube's overall backlog becomes batched GitHub issues, filtered by a checked-in policy |
| 0017 | BUSL-1.1 with a three-seat, 50k-LOC free tier; permissive-only dependencies |
| 0018 | The final heap is observable, and a call is a function of its position and the heap |
| 0019 | Verdicts are modular, and an Equivalent names the callee pairs it assumed |
| 0020 | A shipped catalogue of known-equivalent API pairs, applied visibly |
| 0021 | Source-language parameters are shared by position, synthesised inputs by name |
| 0022 | A Sonar batch over 25 findings splits into a parent issue and sub-issues |
| 0023 | A pair whose verification crashes is reported and skipped, and the run exits 5 |
