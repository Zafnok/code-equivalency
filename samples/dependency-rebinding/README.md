# dependency-rebinding

A dependency upgrade rebinds a call site whose source did not change (ADR 0042, ticket P2-069). Each
side is two projects: the application (`App`) and the library it references (`Files`). The library
changes between the sides. `IFs.File` is the abstract class `FileBase` on the legacy side and the
interface `IFile` on the modern side.

- `Probe.Has(IFs, string)` is `fs.File.Exists(p)` on both sides. The text is the same, but the call
  binds to `FileBase::Exists(string)` on the legacy side and to `IFile::Exists(string)` on the modern
  side. Nothing in the run says whether the two members behave alike, so the call is possibly the
  same function. Each side lowers it as an opaque with reason `rebound-call`. The result is Unknown
  (`opaque`, scope `line`), its related locations are the two call sites, and
  `properties.reboundCalls` names the pair. Before ADR 0042 this was a false Divergent.
- `Probe.Clear(IFs, string)` calls `fs.File.Exists(p)` on the legacy side and `fs.File.Delete(p)`
  on the modern side. That is another member at other text, an edit the developer made. So both
  calls are ordinary calls, the traces differ, and the pair stays Divergent.
- `NoFiles.Exists(string)` and `NoFiles.Delete(string)` are the library's own bodies, unchanged, and
  Equivalent by congruence. (A project with no method body is treated as a load failure, P2-018, so
  the library has one implementation.)

## Expected verdicts

| Procedure | Verdict | `reboundCalls` |
|---|---|---|
| `Probe.Has(IFs, string)` | Unknown (`opaque`, `rebound-call`) | `FileBase::Exists(string)` and `IFile::Exists(string)` |
| `Probe.Clear(IFs, string)` | Divergent | none |
| `NoFiles.Exists(string)` | Equivalent | none |
| `NoFiles.Delete(string)` | Equivalent | none |

Exit code: 1 (a new Divergent result).
