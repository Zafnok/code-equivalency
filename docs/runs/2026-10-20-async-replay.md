# P2-252: Duplicati's two `async` rows, probed with drivers that wait for a task (2026-10-20)

`probe` (`equiv mcp --execute`) on `duplicati-3124`, at equiv `53c549c7` (branch `P2-252-drivers-await-a-task-result`),
from one Release build, on the checkouts and restores P2-237 made, with the committed pair config
(`tools/corpus/configs/duplicati-3124.json`). Only the drivers changed since P2-245's probes at `bceeea62`: a driver now
waits for a returned `Task`, `Task<T>`, `ValueTask` or `ValueTask<T>` and answers with the task's outcome (its result,
the first inner exception of a fault, `TaskCanceledException` for a cancellation). The legacy side ran on the .NET
Framework 4.8 CLR, the modern side on `Microsoft.NETCore.App` 5.0.17, each case under the invariant culture.

**This pair takes no part in ADR 0028's rule table here.** This file adjudicates two rows of P2-212's sample; it has no
continue, re-scope or stop line.

## The two rows

P2-237's method, as P2-245 applied it (`docs/runs/2026-10-19-execution-reach.md`): **confirmed** needs a `probe` run on
both runtimes whose outcomes differ for the claimed reason; a receiver built by fields confirms only with a trace that
the solution reaches that state.

| Row | Procedure | Rule | Probes | P2-245 | Here |
|---|---|---|---|---|---|
| 14 | `Duplicati.Library.Backend.AzureBlob.AzureBlobWrapper::AddFileStream(string,System.IO.Stream,System.Threading.CancellationToken)` | EQ002 | 14-nullcontainer (receiver with every field at its default, a null stream): both threw `NullReferenceException`; 14-nullsource (receiver's `_container` by `CloudBlobContainer(Uri)` on an `.invalid` address, a null stream, a live token): legacy threw `ArgumentNullException`, modern `NullReferenceException` | undetermined (an `async Task` no driver awaits; a non-null container needs a network address) | **confirmed**: the modern body reads `source.Length` to pass it to the six-argument `UploadFromStreamAsync`, the code change the result claims, and faults on it; the legacy body hands the stream to the two-argument overload, whose argument check faults first |
| 47 | `Duplicati.Library.Backend.SharePointBackend::uploadFileSlicePerSlice(Microsoft.SharePoint.Client.ClientContext,Microsoft.SharePoint.Client.Folder,System.IO.Stream,string,System.Threading.CancellationToken)` | EQ006 | 47-pipe (receiver by `SharePointBackend()`, a file name holding `\|`, null client objects and stream): legacy threw `ArgumentException`, modern `NullReferenceException`; 47-plain (the same with a file name holding no invalid character): both threw `NullReferenceException` | undetermined (an `async Task<File>` no driver awaits) | **confirmed**: `Path.GetFileName` rejects the invalid path character on .NET Framework and returns on .NET 5, the runtime change the result claims; the control shows the two sides alike without it |

- Decision: row 14's receiver is built by fields, so it needs a trace: `AzureBlobWrapper(string,string,string)` sets
  `_container` to `CloudBlobClient.GetContainerReference(...)`, which is never null, on both sides; the probe's state is a
  non-null container, which is what the difference depends on (`GetBlockBlobReference` is local, and both sides then fail
  on the null stream before any request). The container's address is under `.invalid`, a name RFC 6761 reserves so that
  it never resolves, and no request is made: P2-245's "needs a network address (unsafe)" does not apply to it.
- Decision: a null stream is a legitimate input to the member, whose parameters are the procedure's inputs; the
  difference is the edit's (a new dereference before the call), not the runtime's, which is what an EQ002 claims.
- Every outcome above is a fault of the returned task, which drivers before this ticket answered as `NotComparable`;
  47-pipe and 14-nullcontainer are P2-245's own inputs, run again.

## Re-adjudication and precision

P2-245's table, with P2-250's correction (Git Extensions rows 0, 3, 7 and 18 confirmed,
`docs/runs/2026-10-19-framework-static-fields.md`) and P2-253's (Git Extensions rows 22 and 41 confirmed,
`docs/runs/2026-10-19-desktop-replay.md`) applied, and these two rows moved from undetermined to confirmed; nothing
else changes.

| Pair | Rule | Rows | Confirmed | False positive | Still undetermined |
|---|---|---|---|---|---|
| duplicati-3124 | EQ002 | 5 | 1 | 1 | 3 |
| | EQ006 | 14 | 11 | 0 | 3 |
| **All three pairs** | **all** | **51** | **30** | **3** | **18** |

| Pair | Rule | Before (P2-245 with P2-250 and P2-253) | Here |
|---|---|---|---|
| duplicati-3124 | EQ002 | 0% (0 of 8) | 11.1% (1 of 9) |
| | EQ006 | 87.0% (20 of 23) | 87.5% (21 of 24) |
| | **all** | **64.5% (20 of 31)** | **66.7% (22 of 33)** |
| **Migrations** | EQ002 | 29.2% (7 of 24) | 32.0% (8 of 25) |
| | EQ006 | 69.1% (38 of 55) | 69.6% (39 of 56) |
| | **all** | **57.0% (45 of 79)** | **58.0% (47 of 81)** |

The ceiling (every undetermined result real) stays 71.4% (85 of 119): two undetermined results became confirmed. P2-245's
"an `async` result no driver awaits" obstacle has no row left.

- Decision: the scoreboard is not changed here, as P2-245 and P2-253 decided for their own re-adjudications: its
  migration row is P2-237's measurement, and P2-247 is the re-adjudication that refreshes it.
