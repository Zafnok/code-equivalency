# Architecture Decision Records

Short, numbered, immutable once accepted, except for dated `## Clarifications` bullets
that apply a decision to a case it did not spell out. To change a decision, write a new
ADR that supersedes the old one. Whether something needs an ADR at all is the bar test in
the `equiv-adr` skill. Template and procedure: `.claude/skills/equiv-adr/SKILL.md`.

| ADR | Decision |
|---|---|
| 0001 | .NET 10 / C# 14 for the whole MVP |
| 0002 | Dependency register (living) |
| 0003 | Lower from Roslyn's IOperation control-flow graph (superseded in part by 0039) |
| 0004 | MSBuildWorkspace on Windows for the MVP loader; fact check on "Windows bindings" |
| 0005 | Direct Z3 encoding; Lean and Boogie rejected for the MVP ("Z3 alone" superseded by 0050) |
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
| 0018 | The final heap is observable, and a call is a function of its position and the heap (amended by 0041 and 0043) |
| 0019 | Verdicts are modular, and an Equivalent names the callee pairs it assumed |
| 0020 | A shipped catalogue of known-equivalent API pairs, applied visibly |
| 0021 | Source-language parameters are shared by position, synthesised inputs by name |
| 0022 | A Sonar batch over 25 findings splits into a parent issue and sub-issues |
| 0023 | A pair whose verification crashes is reported and skipped, and the run exits 5 |
| 0024 | Identical bound code is Equivalent by congruence; an unlowerable fragment on both sides is shared |
| 0025 | Floating-point, decimal and user-defined operators are shared pure functions |
| 0026 | A Divergent must not depend on an abstraction; otherwise it is Unknown(Abstraction) (amended by 0056) |
| 0027 | The Unknown rate is measured before it is optimised, and every Unknown points at lines |
| 0028 | Success is measured on a pinned public corpus, against criteria fixed before the first run |
| 0029 | A failure's blast radius is the smallest unit it touches (project, method, line), and every Unknown says which |
| 0030 | Z3 comes from its official GitHub release nupkg through a hash-pinned local feed |
| 0031 | Linux parity is a release requirement; the Windows-only loader is not shippable |
| 0032 | The hosted tier runs on Azure Container Apps Jobs, not AKS |
| 0033 | `equiv mcp` exposes the pipeline to coding agents as an MCP server (stdio, SARIF results) |
| 0034 | The census measures what the solver will see: changed pairs, their reason sets, and what congruence cannot vouch for |
| 0035 | The real runtimes are a second oracle: execution measures, confirms and bounds, and never proves |
| 0036 | A proposed invariant, contract or table row is a hypothesis until a checker admits it; callee contracts replace unproven assumptions |
| 0037 | An Unknown says whether the modern side can fail where the legacy side does not (superseded in part by 0049) |
| 0038 | A run reports its progress on stderr, with a clock per phase, an ETA, and a writer that never blocks the pipeline |
| 0039 | A pair that IOperation leaves with an unshared opaque is lowered from ILAst instead; IOperation stays primary (superseded in part by 0049) |
| 0040 | Each side's runtime is detected from its projects, and a runtime rule applies only if the pair crosses it |
| 0041 | A call to a string or primitive member with only string and primitive arguments reaches no heap map |
| 0042 | A call site with the same text on both sides that binds to a different callee is possibly the same call, and its outcome is unknown |
| 0043 | A catalogued BCL member that runs no observable code is not a call |
| 0044 | Evaluating a hole of an interpolated string leaves the current culture's integer formatting as it found it |
| 0045 | A call to a private helper that exists on one side only is replaced by the helper's body (narrows 0019's rejection of inlining) |
| 0046 | A caller file path or line number the compiler supplies is an input both sides share, not a constant |
| 0047 | A call to a static one-call forwarder is a call to its target (narrows 0019's rejection of inlining) |
| 0048 | A result that is not Equivalent may carry a proved input condition under which the pair agrees (`properties.agreesWhen`); no verdict, rule id, exit code or fingerprint depends on it |
| 0049 | `equiv compare` has a thorough mode and a quick mode (its default is superseded by 0052); thorough is quick plus further passes over what is still Unknown; measured yield chooses a technique's mode, not whether it exists |
| 0050 | cvc5 is asked the rung 1 queries Z3 gives up on (supersedes 0005's "Z3 alone") |
| 0051 | Proposed: a Lean proof a model writes may close a rung 1 query no solver decides; Lean checks the query, never the program, and nothing is built until it decides a query no solver does (narrows 0005's rejection of Lean) |
| 0052 | Quick is the default mode of `equiv compare`; thorough is asked for with `--mode thorough` (reverses the default of 0049 decision 1, from P1-032's measurement: 37 times the time for one more proof) |
| 0053 | An interpretable pure function a candidate counterexample depends on (`IntPtr` equality, `float` and `double` arithmetic as IEEE 754) is given its real meaning and rung 1 is asked again; never from the start, never on x87 or a runtime-sensitive function (narrows 0025's rejection of IEEE floating point) |
| 0054 | An `extern` method is a procedure: matched, Equivalent by congruence only when both sides import the same thing with the same attributes on one runtime, otherwise Unknown (`no-body`), never Divergent, never run by `--execute` (reverses the exclusion of `extern` members in ARCHITECTURE.md's frontend step 2; extends 0024 decision 1) |
| 0055 | A `[GeneratedRegex]` member is compared by its declaration (signature and bound attribute) on every runtime pair, Equivalent by congruence when equal and otherwise Unknown (`no-body`); the matcher the regex generator emits is no procedure and is counted in `run.properties.generatedProcedures` (narrows ARCHITECTURE.md's frontend step 2; extends 0024 decision 1) |
| 0056 | In the replay a closed call is an abstraction: its outputs and event are tainted, so a Divergent never rests on an answer the solver chose for a BCL member, and the gate's rule 2 excuses only open calls (amends 0026 for closed calls) |
| 0057 | A solver Equivalent may carry a proof certificate: under `--certify`, cvc5 writes an Alethe proof of each query the proof rests on and Carcara checks it; `properties.certificate` is `certified`, `holey`, `uncertified` or `rejected`, and never changes a verdict, rule id or exit code; covers the query, not the lowering or the encoder (closes the check ADR 0050 left to P1-026) |
| 0058 | A `[DllImport]` that crosses a runtime is compared as on one runtime: Equivalent by congruence when its text is equal, unless a `marshalling` row of `runtime-changes.json` (the Interop items of Microsoft's breaking-change pages, .NET Core 3.0 to .NET 10) inside the interval reaches its declaration; an interval touching .NET Framework or below the table's coverage, and an `InternalCall`, keep 0054 decision 4 (supersedes 0054 decision 4 in part) |
| 0059 | A callee the config's call-identity map (`callIdentityRenames`) maps to another member, a solution's own shim of a member the new runtime lacks included, is a callee pair the result assumes: its legacy identity is in `assumedCallees` and `unprovenAssumptions`, and `properties.callIdentitiesMapped` names its counterpart (extends 0019 to declared pairs) |
| 0060 | A matched pair whose texts are equal and that only a runtime rule keeps out of congruence (`notCongruent` `runtime-rule`) is a runtime exposure, EQ007 (`kind: fail`, `level: warning`), instead of EQ006 or EQ003: analysed as before, with the solver's input when it found one, the rules and their preconditions named, every exit code unchanged, and EQ006 only when the real runtimes show the change; path rows are not assumed valid by default, and a `suppressRuntimeChanges` entry (a member or `precondition:<name>`) is an assumption the result names (supersedes 0040 decision 2, 0054 and 0058 decision 4 in part; extends 0019) |
| 0061 | A rebound pair whose two callees are one assembly's members at two versions is one call when the two members run the same IL: the same instructions, regions and locals, with every token naming the same thing, every member of the assembly they reach compared the same way, and every other member a framework one no runtime row names or one of the same build; the pair is then lowered as one call and listed in `properties.reboundCallsProved` (narrows 0042) |
| 0062 | A replay or `probe` driver may use reflection in its generated source (never in `src/`) to call a member the friend assembly cannot name, bound once by exact signature, and to build a solution type's instance without its constructor carrying the model's fields; the arguments the model determines (`CancellationToken`, arrays, delegates answering by the model, objects, structs with reference fields) are built, and each input's provenance is in `properties.replayInputs`; an `uninitialized` input confirms a Divergent only with a trace showing the state reachable; a delegate the model's runs call waits for P2-249 (accepted; supersedes 0035's "no reflection anywhere" in part) |
