# ADR 0058: A `[DllImport]` that crosses a runtime is compared as on one runtime, unless an interop change documented between the two runtimes reaches its declaration

Status: accepted (2026-10-14). Supersedes ADR 0054 decision 4 for `[DllImport]` functions on a pair that crosses
only .NET (Core) runtimes the runtime-changes table covers; keeps it everywhere else.

## Context
ADR 0054 decision 4 gives an `extern` method no fingerprint on a pair whose runtimes differ, so every matched one is
Unknown (`opaque`, `no-body`), and a body that declares an `extern` local function is runtime-sensitive. P2-124's
rerun measured the cost on an upgrade (`docs/runs/2026-10-12-upgrade-verdict.md`): on `gitextensions-9860` (net5.0
to net6.0) 53 of 605 changed pairs, 8.8%, its largest opaque reason, every one a `[DllImport]` whose text is the
same on both sides; on `jellyfin-13023` (net8.0 to net9.0) 2 of 122. Decision 4 was written because "no
`runtime-changes.json` row describes the marshaller". Microsoft's breaking-change pages do describe it, version by
version, in their Interop category, so for an interval those pages cover the marshaller's changes can be rows
(ticket P2-165).

## Decision
1. **A `[DllImport]` function has its fingerprint on every pair.** An `extern` method with a `[DllImport]` has the
   text ADR 0054 decision 2 gives it whatever the pair's runtimes, and is Equivalent by congruence when the two
   texts are equal and the text is not runtime-sensitive (ADR 0024 decision 1). Otherwise decision 3 of ADR 0054
   stands: Unknown, `no-body`, never Divergent. An `extern` local function in a body follows the same rule.
2. **When it is runtime-sensitive.** On a pair whose runtime interval is empty, never. On any other pair, when
   either holds:
   - the interval is not covered: its older runtime is .NET Framework, or it reaches below the table's
     `coveredFrom` (`RuntimeInterval.UncoveredRange`). ADR 0054 decision 4 is kept for every `extern` function on
     such a pair; the rule's name is `extern`, as before;
   - a `marshalling` row of `runtime-changes.json` whose `changedIn` lies in the interval reaches the declaration
     (decision 3). The rule's name is `extern:` and the row's `id`.
3. **The rows.** `runtime-changes.json` gains a top-level `marshalling` list beside `rows`, one entry per item of
   the Interop category of Microsoft's breaking-change pages from .NET Core 3.0 to .NET 10, each with `id`,
   `reach`, `changedIn`, `reason`, `url` and `source` (`documented`). `reach` says which declarations it touches,
   read off the declaration alone:

   | `id` | `changedIn` | What changed | `reach`: the declarations it touches |
   |---|---|---|---|
   | `suffixProbing` | net5.0 | No `A`/`W` suffix probing off Windows ([page](https://learn.microsoft.com/en-us/dotnet/core/compatibility/interop/5.0/function-suffix-pinvoke)) | `inexactEntryPoint`: an import whose `ExactSpelling` is false |
   | `winrtRemoved` | net5.0 | Built-in WinRT support removed ([page](https://learn.microsoft.com/en-us/dotnet/core/compatibility/interop/5.0/built-in-support-for-winrt-removed)) | `comObject` |
   | `inspectableCast` | net5.0 | Casting an RCW to an `InterfaceIsIInspectable` interface throws ([page](https://learn.microsoft.com/en-us/dotnet/core/compatibility/interop/5.0/casting-rcw-to-inspectable-interface-throws-exception)) | `comObject` |
   | `staticAbstractInterfaces` | net6.0 | Static abstract members in interfaces ([page](https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/6.0/static-abstract-interface-methods)) | `none`: a language feature, no marshalling |
   | `osArchitectureEmulation` | net7.0 | `RuntimeInformation.OSArchitecture` under emulation ([page](https://learn.microsoft.com/en-us/dotnet/core/compatibility/interop/7.0/osarchitecture-emulation)) | `none`: a property, already a member row |
   | `comWrappersUnwrap` | net8.0 | `CreateObjectFlags.Unwrap` unwraps only the target instance's wrappers ([page](https://learn.microsoft.com/en-us/dotnet/core/compatibility/interop/8.0/comwrappers-unwrap)) | `comObject` |
   | `customMarshallerShapes` | net8.0 | Custom marshallers need both shapes ([page](https://learn.microsoft.com/en-us/dotnet/core/compatibility/interop/8.0/marshal-modes)) | `none`: the source generator's analyzer, at compile time |
   | `idispatchImplRemoved` | net8.0 | `IDispatchImplAttribute` removed ([page](https://learn.microsoft.com/en-us/dotnet/core/compatibility/interop/8.0/idispatchimplattribute-removed)) | `comObject` |
   | `jsFunctionBinding` | net8.0 | `JSFunctionBinding`'s implicit constructor removed ([page](https://learn.microsoft.com/en-us/dotnet/core/compatibility/interop/8.0/jsfunctionbinding-constructor)) | `none`: JavaScript interop, no `[DllImport]` |
   | `safeHandleConstructor` | net8.0 | `SafeHandle` types need a public constructor ([page](https://learn.microsoft.com/en-us/dotnet/core/compatibility/interop/8.0/safehandle-constructor)) | `none`: `[LibraryImport]` and `[GeneratedComInterface]` only, at compile time |
   | `netcoredeps` | net8.0 | Linux native library resolution no longer uses `netcoredeps` ([page](https://learn.microsoft.com/en-us/dotnet/core/compatibility/interop/8.0/linux-netcoredeps)) | `everyImport` |
   | `cet` | net9.0 | CET supported by default ([page](https://learn.microsoft.com/en-us/dotnet/core/compatibility/interop/9.0/cet-support)) | `everyImport` |
   | `idispatchExCast` | net10.0 | Casting an `IDispatchEx` COM object to `IReflect` fails ([page](https://learn.microsoft.com/en-us/dotnet/core/compatibility/interop/10.0/idispatchex-ireflect-cast)) | `comObject` |
   | `singleFileSearch` | net10.0 | Single-file apps no longer look in the executable's directory ([page](https://learn.microsoft.com/en-us/dotnet/core/compatibility/interop/10.0/native-library-search)) | `searchPathsWithoutAssemblyDirectory`: search paths given, by the function or its assembly, without `AssemblyDirectory` |
   | `assemblyDirectoryOnly` | net10.0 | `DllImportSearchPath.AssemblyDirectory` alone searches only there ([page](https://learn.microsoft.com/en-us/dotnet/core/compatibility/interop/10.0/search-assembly-directory)) | `assemblyDirectoryOnly`: search paths, by the function or its assembly, of `AssemblyDirectory` alone |

   `comObject` is a declaration whose return value or a parameter is marshalled as a COM or WinRT object: its type,
   or a type that type is made of (an array's element, a pointer's target, a type argument, and the fields, field-like
   events and delegate signatures of a type the solution declares, as P2-146 walks them), is an interface, `object`,
   `dynamic` or a `[ComImport]` class, or carries a `[MarshalAs]` whose `UnmanagedType` or `ArraySubType` is
   `IUnknown`, `IDispatch`, `Interface`, `Struct`, `SafeArray`, `AsAny`, `IInspectable` or `HString`.
4. **What a reached pair gets.** Unknown, `no-body`, as ADR 0054 decision 3 gives every `extern` pair that is not
   congruent. Not EQ006: an EQ006 carries an input on which a runtime-changed call decides the outcome, and an
   `extern` pair has no input to give. `properties.notCongruent` names the rows (`runtime-rule`, `extern:<id>`).
5. **An `InternalCall` function** keeps ADR 0054 decision 4 on every pair that crosses a runtime: its runtime
   answers it from its own table, which no page lists.
6. **The platform.** Three rows describe one operating system (`suffixProbing` off Windows, `netcoredeps` on Linux,
   `cet` on Windows x64). `equiv` does not know where the code runs, so every row applies on every platform.

## Why
- ADR 0054 decision 4's reason was a fact about the table, not about the runtimes: no row described the marshaller.
  The Interop category of each version's breaking-change page is Microsoft's own list of what changed for interop
  between that version and the one before; read from .NET Core 3.0 (the table's `coveredFrom`) to .NET 10, it is
  fifteen items. Read on 2026-10-14 from `learn.microsoft.com/dotnet/core/compatibility/3.0`, `5.0` … `10.0` and the
  item pages they link; the 3.0 page has no Interop category.
- Every row is kept, including the five that reach nothing, so the table test can say the list is the pages' list
  and a later page adds a row, not a guess.
- The .NET Framework boundary cannot be listed. The `fx-core` page has no Interop category, yet the marshaller
  differs: on .NET the last error is cleared before a call marked `SetLastError` and on .NET Framework it is not
  (`DllImportAttribute.SetLastError`'s remarks), and built-in COM and WinRT were reimplemented or dropped. ADR 0054's
  "Why" placed the `SetLastError` change at .NET 6; the API reference places it at the Framework boundary, which this
  decision keeps under decision 4 either way. With no complete list there, the ticket's own fallback applies: keep
  decision 4 for those intervals. The same holds below `coveredFrom`, whose pages were never read.
- `reach` errs wide. `netcoredeps` and `cet` touch the deployment and the native code, not the declaration, so they
  reach every import; `comObject` takes in every interface and `object` though only some go through COM. A missed
  reach is a false Equivalent; a wide one costs one Unknown on a pair that crosses the version.
- The declaration is all `reach` reads: what an import's marshalling depends on is in its attributes, its
  signature's types and its assembly's settings, which P2-145 and P2-146 put in its text.

## Rejected
- **Keep decision 4.** Every unedited import of a .NET upgrade stays Unknown, with nothing behind it: on
  `gitextensions-9860` no row reaches anything between net5.0 and net6.0.
- **Marshalling rows in `rows`.** A row there is a member prefix matched against a call's identity; a declaration
  is not a call, and every reader of `rows` would have to skip rows with no member.
- **List the Framework boundary from what is known.** No page enumerates it, and a missing item is a false
  Equivalent on the most common migration `equiv` is for.
- **EQ006 under the row.** No counterexample exists to carry (ADR 0054 decision 3).
- **Narrow a row to its operating system.** `equiv` does not know the deployment's.
- **Extend the rule to `InternalCall`.** Its runtime's table of internal calls is undocumented.

## Consequences
- On a pair between two covered .NET (Core) runtimes, an unedited `[DllImport]` that no row in the interval reaches is
  one more EQ001 by congruence; P2-165's Notes count `gitextensions-9860` before and after. A .NET Framework migration
  is unchanged.
- `RuntimeChangeTable` parses and exposes the `marshalling` list; the frontend decides `reach`. The JSON header
  describes the list. A new Interop item on a later version's page is a new entry.
- ADR 0054's Status line says decision 4 is superseded in part by this ADR. VERIFICATION-MODEL.md section 1 and its
  `runtime-rule` row, and ARCHITECTURE.md's frontend step 2, say what this decides.
- `[LibraryImport]` partial methods across runtimes stay as P2-107 left them (ticket P2-165, out of scope).
