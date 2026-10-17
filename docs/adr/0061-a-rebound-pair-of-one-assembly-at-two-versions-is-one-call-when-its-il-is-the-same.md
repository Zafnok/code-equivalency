# ADR 0061: A rebound pair whose callees are one assembly's members at two versions is one call when the two members run the same IL

Status: accepted (2026-10-17). Narrows ADR 0042: a rebound pair that this rule proves is no longer opaque. Proposed and
accepted by ticket P2-179.

## Context
ADR 0042 makes both calls of a rebound pair opaque because "the library's code is not in the run". On `gitextensions-8522`
`rebound-call` is the only opaque reason in 121 of 938 changed pairs (12.9%, P2-130), and the callees are mostly three
third-party packages at two versions each. Both versions of each package are restored for the run, so their code can be
read. P2-179's Notes count the pairs by package, FluentAssertions 5.2.0 to 5.10.3 first. Its two `Throw<T>` bodies are
not the same code: 5.10.3 fails first on a null subject, asks a virtual property whether the delegate may be async, and
words the failure message differently.

## Decision
A rebound pair (L, M) is **one call** when L and M are declared in assemblies with the same simple name, and their code,
read from the assembly each side's compilation references, is the same:
1. **Where the IL comes from.** For a package, the file its metadata reference points at. For a project of the solution,
   that project's compilation emitted in memory, as ADR 0039 emits one. A reference assembly
   (`ReferenceAssemblyAttribute`) holds stubs and is never read: such a pair stays rebound.
2. **The two members.** Each call runs the member's own body. That means it is not dispatched virtually: the member is
   not `virtual`, `abstract` or an interface member, or else it or its type is sealed. It is not declared in a value
   type, and both members are static or both are instance members. Their parameter and return types and their type
   arguments at the call site are the same.
3. **The same body.** The two bodies have the same instructions with the same immediate operands and branch offsets,
   the same exception regions, the same local types, and the same `initlocals` and implementation flags. A metadata
   token in one is the same as the token at the same place in the other when both name the same string, or a member or
   type that one of the following rules makes the same.
4. **What the bodies reach inside the assembly.** A method of the assembly, called non-virtually or created with
   `newobj` or `ldftn`, is the same when rules 2 and 3 hold for it in turn. A pair already being compared counts as the
   same, which is ADR 0019's mutual-summary rule for a cycle. A static field needs its type's static constructor (or
   its absence) to be the same as well. A type of the assembly is the same when it has the same name, flags, base type
   and interfaces on both sides, and is not a value type. A virtual call to a method of the assembly, `calli`, `jmp`
   and a field with initial data (RVA) are never the same.
5. **What the bodies reach outside the assembly.** A member or type of another assembly has the same name on both
   sides, and either:
   - it is a framework member (its assembly is a reference assembly). No row of the runtime-changes table names its
     member or a prefix of it, at any runtime. When it is called virtually, neither version of the assembly declares
     a virtual method of that name, so no override from the package can run; or
   - its assembly is the same build on both sides, with the same name, version and module version id.
6. **A bound.** At most 256 member pairs are compared for one rebound pair. Past that, the pair stays rebound.

When (L, M) is one call, every call to L in the legacy body, in both lowerings, is lowered as the same call to M. It is
no longer opaque and is no longer in `properties.reboundCalls`. Instead it is in `properties.reboundCallsProved`, one
`{ legacy, modern }` per pair, sorted like `reboundCalls`. When L or M is also in another rebound pair, or any rule above
fails, the pair stays a rebound pair under ADR 0042.

## Why
- This is ADR 0024's congruence argument applied to the library. The same instructions over the same callees compute
  the same function. Rules 4 and 5 make "the same callees" true transitively: every member of the package that the two
  members can run is compared the same way. So a difference in what the two versions do inside the package is a
  difference in some compared body, and the pair stays Unknown. A difference outside the package can only come from
  three places, and none can make the pair one call. A runtime change is excluded by the table, at every runtime, as
  everywhere in the run. A dependency at another build is refused. An override in the package is excluded by refusing
  virtual dispatch into it. **That is why a pair whose two versions behave differently cannot be proved:** a behaviour
  difference needs a different instruction in some body both members reach, or a callee this rule refuses.
- Static state is the one way the same body can read a different value with no call in it. A static field's value
  starts in its type's static constructor, which rule 4 compares. A value written later goes through calls, and those
  are compared too or are calls the run already shares by identity (below).
- A token is compared by the name it resolves to, not by its number. Two builds number their tables differently, and
  `mscorlib` and `netstandard` name one framework type through different assemblies.
- Measured on `gitextensions-8522`: none of its third-party pairs is the same code. FluentAssertions' bodies differ
  (Context), `System.IO.Abstractions`' callees are abstract and interface members with no body, and NSubstitute's
  `Returns<T>` calls `ConfigureReturn` in 4.2.1 where 3.1.0 calls a private `Returns`. So the rule moves none of them,
  and it says why. That is the right result: the pairs it leaves Unknown are pairs whose library code changed.

## Rejected
- **Lowering both callees from IL and verifying them with the solver as a pair of the run.** It could prove a library
  body that was refactored without changing behaviour, which identity cannot. No pair on the corpus is one: every
  third-party pair there has a real change or no body. Roslyn does not import a referenced assembly's private members
  (`MetadataImportOptions.Public`), so ADR 0039's resolution through the compilation would leave every private helper
  opaque. This is the extension to make when a corpus shows a refactored but equal library pair.
- **A per-package equivalence entry, a third-party catalogue beside ADR 0020's.** It would be judged by hand, not
  proved, and it would need a review per package version. ADR 0059's `callIdentityRenames` already lets a user declare
  such a pair, as an assumption the result names.
- **Comparing only the two members' own bodies.** A private helper they call could change between the versions, and
  the pair would be one call over a different behaviour.
- **Comparing decompiled C#.** The decompiler's output depends on its heuristics, and the IL is what runs.
- **Matching by NuGet package id instead of assembly name.** The compilation holds assembly references, not package
  ids. The assembly name also covers a project of the solution whose member moved, for example a method moved to its
  base class.
- **Calling a pair whose bodies differ Divergent.** Different code is not proof of different behaviour. The pair stays
  Unknown, as ADR 0042 decides.

## Consequences
- The proof assumes what every call the run shares by identity already assumes (ADR 0019): a value that comes into the
  two members from outside their package is the same on both sides. That includes the object a receiver holds, built
  by a call to an unchanged member of the same package. It also assumes the compiled reference is the assembly loaded
  at run time, binding redirects aside.
- A pair whose only rebound calls this rule proves moves from Unknown (`opaque`, `rebound-call`) to whatever the solver
  decides, with the pairs named in `properties.reboundCallsProved`. The two fingerprints still name different callees,
  so such a pair is never congruent; the solver decides it.
- SARIF gains `properties.reboundCallsProved`, and verdicts change on existing input. The change that implements this
  carries `Release: minor`. VERIFICATION-MODEL sections 3 and 6 change in the same PR.
- Ticket P2-179 implements this, with `samples/package-version-rebound`. Its Notes record that on `gitextensions-8522`
  `rebound-call`'s counts do not move, because no package pair there is the same code.
