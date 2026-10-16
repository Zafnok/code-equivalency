# runtime-row-call-cannot-reach

The same calls on .NET Framework 4.8 and on .NET 10, in a Windows Forms project (tickets P2-216 and P2-220). `Calls.cs` is the same
text on both sides, so every method is a body nobody edited. Each calls a member whose row in `runtime-changes.json`
`requires` a fact about the method's calls to it: the row fires only where one of them can reach its change. A body whose
call cannot is congruent; one whose call can is not, and a runtime rule keeps it out of congruence.

- `NewProcessFile` reads `StartInfo` of a `Process` it creates with `new`. .NET throws only for a process this code did not
  start (`foreignProcess`).
- `Katakana` decodes three bytes it sets to constants that form well-formed UTF-8. Only ill-formed input decodes
  differently (`utf8Decoding`).
- `ShowLastImage` sets a tree node's image indexes to `Images.Count - 1`, never below -1. .NET throws only below -2
  (`belowNoneIndex`).
- `IsVistaOrNewer` compares `OSVersion.Version.Major` with 6. .NET Framework's shim reports 6 where the real major is 6 or
  more, and `>= 6` is true for both (`shimmedVersionRead`).
- `ProcessFile`, `Truncated`, `ShowImage` and `IsWindows10OrNewer` make the same four calls where the change is
  reachable: a process passed in, two bytes that end mid-sequence, an index passed in, and a comparison with 10. Each
  method stays Divergent.
- `RelativeUrisOnMono` sets a static field through reflection, the one case the `FieldInfo.SetValue` row is about, but only
  under `IsMono`, a getter that returns `Type.GetType("Mono.Runtime") != null`. No assembly the sample references defines
  `Mono.Runtime`, and neither .NET Framework's core library nor .NET's does, so the branch runs on neither
  (`staticFieldSet`, read through the getter, ticket P2-220). `RelativeUrisOn64Bit` makes the same set under `Is64Bit`,
  `IntPtr.Size == 8`, which is true in a 64-bit process on both runtimes, and stays Divergent.
- `IsMono` calls `Type.GetType`, whose row (.NET 8's stricter type-name parsing) has no precondition, so it stays Divergent
  although `Mono.Runtime` is a name that change does not touch; `RelativeUrisOnMono` names it as an assumption.

## Expected verdicts

| Procedure | Verdict | `proofMethod` |
|---|---|---|
| `Calls.NewProcessFile(string)` | Equivalent | `congruence` |
| `Calls.Katakana()` | Equivalent | `congruence` |
| `Calls.ShowLastImage(TreeNode, ImageList)` | Equivalent | `congruence` |
| `Calls.IsVistaOrNewer()` | Equivalent | `congruence` |
| `Calls.RelativeUrisOnMono()` | Equivalent, assuming `get_IsMono()` equivalent | `congruence` |
| `Calls.get_Is64Bit()` | Equivalent | `congruence` |
| `Calls.ProcessFile(Process)` | Divergent (EQ006, a runtime-changed API) | none |
| `Calls.Truncated()` | Divergent (EQ006, a runtime-changed API) | none |
| `Calls.ShowImage(TreeNode, int)` | Divergent (EQ006, a runtime-changed API) | none |
| `Calls.IsWindows10OrNewer()` | Divergent (EQ006, a runtime-changed API) | none |
| `Calls.RelativeUrisOn64Bit()` | Divergent (EQ006, a runtime-changed API) | none |
| `Calls.get_IsMono()` | Divergent (EQ006, a runtime-changed API) | none |

Exit code: 1 (a new Divergent result).
