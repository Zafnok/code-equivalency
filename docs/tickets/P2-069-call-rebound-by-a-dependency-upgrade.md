# P2-069 An unchanged call site that a dependency upgrade rebinds is still the same call
Status: todo
Effort: L
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-047

## Goal
P2-047's audit found 9 false Divergents where the method's source is unchanged but a dependency
upgrade changed what a call site binds to. A property's type became an interface where it was a
class, so `fs.File.Exists(p)` calls `IFile::Exists` instead of `FileBase::Exists`. A generic call
got a new type argument (`Returns<FileBase>` became `Returns<IFile>`). A generated class moved
namespace. Each side then calls a different uninterpreted function, and the solver gives them
different results. Minimal repro, as a sample pair with a library project that changes between the sides:

```csharp
// library, legacy side
public abstract class FileBase { public abstract bool Exists(string p); }
public interface IFs { FileBase File { get; } }
// library, modern side
public interface IFile { bool Exists(string p); }
public interface IFs { IFile File { get; } }
// the method, identical on both sides
static bool Has(IFs fs, string p) => fs.File.Exists(p);
```

Today this is EQ002. It is not a false Divergent that can be proved Equivalent, since the library's
implementations are not visible. It should be Unknown, naming the rebinding, not Divergent. Decide,
through `equiv-adr`, when two different callee identities at textually identical call sites are
treated as possibly the same function, and what verdict that gives.

## Spec references
ADR 0018, ADR 0019, ADR 0026 (Divergent only when untainted), VERIFICATION-MODEL section 3
(the equivalence table and call identity), P2-047's audit.

## Acceptance criteria (all must hold; nothing beyond them)
1. The `equiv-adr` outcome is merged before any code change.
2. The pair above, as `samples/dependency-rebinding`, is not Divergent, and its SARIF names the two
   identities it treated as possibly the same.
3. A variant in which the modern method really calls a different member (for example `Delete`
   instead of `Exists`) stays Divergent.

## Tests
- Integration test on `samples/dependency-rebinding` (both variants).
- Unit tests the decision names.

## Out of scope
Matching members whose names changed. BCL overload rebinding (P2-070).

## Notes
- Found by P2-047: 9 false positives on Git Extensions (7 from one System.IO.Abstractions upgrade,
  1 generic instantiation, 1 generated `Resources` class that moved namespace).
