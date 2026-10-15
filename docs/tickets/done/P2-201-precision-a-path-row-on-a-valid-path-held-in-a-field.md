# P2-201 Precision bug: a path row fires on a valid path that a field or get-only property holds
Status: done (PR #506)
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-188

## Goal
P2-188 (Notes) follows a valid path through one of the solution's own methods; a field is still not
followed. Four of the 16 false EQ006 P2-130 adjudicated to P2-188 reach the call through a field or a
get-only property:
- `GitCommandsTests.Git.Tag.GitTagControllerTest::Setup()` (P2-047's row 61): a `readonly` field whose
  initializer is `TestContext.CurrentContext.TestDirectory`, a listed source;
- `GitUITests...FormFileHistoryControllerTests::TryGetExactPathName_should_check_if_path_matches_case(...)`
  (`gitextensions-8522` #51): a get-only auto-property the constructor sets from a local function that
  returns `Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())`;
- `GitUI.CommandsDialogs.FormResolveConflicts::Mergetool_Click(...)` (#55): `GitModule.WorkingDir`, set
  in the constructor through the solution's `NormalizePath` extension, which ends in `Path.GetFullPath`;
- `OpenRA.FileFormats.ReplayMetadata::RenameFile(string)` (#8): a property with a private setter whose
  writers store null or a path a `FileStream` or `File.Move` accepted.

Over all 181 path-row EQ006 of the three migration pairs, P2-188's census counts 53 whose flagged
argument is a field or property (33 of the method's own type, 20 of another object), the second
largest shape after a caller's parameter. When done, a `readonly` field or get-only auto-property
whose every write (initializer, and assignment in a constructor of its type) is a valid path by
P2-188's rules is one.

## Spec references
`docs/tickets/done/P2-188-*.md` and `P2-074-*.md`, the `validPaths` header of
`src/Equiv.Core/RuntimeChanges/runtime-changes.json`, ADR 0019.

## Acceptance criteria (all must hold; nothing beyond them)
1. A read of such a field or property is a valid path, with the soundness argument in Notes (what
   writes a `readonly` field outside its type's constructors, a `ref` to it, reflection).
2. Rerun the three migration pairs in compare mode quick; Notes give the four procedures' new results
   and the path-row EQ006 count next to P2-188's (59, 100, 16).

## Tests
Unit tests: a `readonly` field set from a source stays unflagged, one set from a constructor parameter
is flagged, a mutable field is flagged.

## Size guard
A writer outside the field's own type, or a property with a setter: stop, that is not this ticket.

## Out of scope
OpenRA #8's private-set property (its writers are in other methods), unless criterion 1's rule
reaches it without more.

## Notes
- Filed 2026-10-14 by P2-188 (criterion 1).
- Decision: the members followed are a `readonly` field and a get-only auto-property (no setter, a getter with no body,
  `get;`) of a class. Their writers are read where the language allows them: the member's initializer, and every write
  in a constructor (instance or static) or `init` accessor of its declaring type. There must be at least one, as P2-188
  asks of a callee's returns, so a field nothing assigns (always null) is not one. Each written value is read by P2-188's
  and P2-200's rules in its own body, with the compilation that holds it, so a parameter the constructor guards with a
  validating guard is one and a parameter it stores unchecked is not.
- Decision: a read of such a member counts as one call toward P2-200's bound of four (`ValidPaths.CalleeDepth`). Fields
  read each other (`static readonly string B = Path.Combine(A, "x")`), and the bound ends a cycle of static fields
  (`A = B; B = A`), which is flagged.
- Decision: the property's getter is the one that runs, found as P2-200 finds a callee's body: the getter itself when no
  override can replace it, or a sealed receiver type's own override. So a sealed type's `override string P { get; }` is
  read, and a `virtual` auto-property on a receiver that is not sealed is not. A partial property's definition (`get;`)
  is not an auto-property, since its implementation part has the body; a C# 14 getter that uses `field` has a body and
  is not followed either.
- Decision: a struct's field or property is not followed. `this = other;` in any member of a struct that is not
  `readonly` writes every field, readonly ones included, and that member is not a constructor.
- Decision: `LocalValues.Write`, which this rule shares with P2-074's local rule, now also counts as a write of a value
  not known a `ref` local's initializer, a `ref` reassignment's value (`r = ref f`) and an operand of a `ref` conditional
  (`(b ? ref f : ref g) = x`), the ways a constructor can alias a `readonly` field and write it through the alias. For a
  local this only makes the local rule stricter.
- Criterion 1, soundness. The C# compiler accepts a write to a `readonly` field only in its variable initializer, or in
  a constructor or `init` accessor (an instance field) or static constructor (a static field) of the class that declares
  it (CS0191, CS0198), and a writable `ref` to it (a `ref` or `out` argument, a `ref` local, a `ref` return) only there
  too (CS0192, CS0199); everywhere else a `ref` to it is `ref readonly`. A get-only auto-property is compiled to a
  `readonly` backing field and can be assigned only in its initializer and the declaring type's constructors (CS0200);
  its getter returns that field. A derived type, a nested type and a lambda or local function inside a constructor
  cannot write either. So the writes the rule reads are every write the language allows; each that is not a plain
  assignment (a `ref`, `out` or `in` argument, a `ref` local, `ref` reassignment or `ref` conditional, a compound or
  `??=` assignment, a deconstruction) has a value not known and makes the member not valid. Inside its own type a
  generic member is its definition, which is what the read maps to, so a constructed type's field is read through the
  definition's writes. The value the read returns is then one of the values written, each a valid path by the
  existing rules, and where a writer's value is valid only because a validator's row fires on another runtime (a
  constructor storing `Path.GetFullPath(p)`), that row fires in the writer, the procedure where the divergence starts,
  as P2-188's argument for validators has it. What writes such a member outside these rules, and is not followed, as
  P2-074's and P2-188's known gaps are not, all on the side of not flagging:
  - reflection: `FieldInfo.SetValue` writes an instance `readonly` field on every runtime (a static one only on .NET
    Framework; the `staticFieldSet` row is the change), and so do serializers that set fields
    (`DataContractSerializer`, `BinaryFormatter`); `Unsafe.AsRef` turns a `ref readonly` into a writable `ref`; unsafe
    code can write a string's characters in place through `fixed`, which P2-074's local rule already does not see;
  - a read before the write: a static field initializer that reads a later static field, a base constructor's virtual
    call, or a constructor that reads the member before assigning it sees null, not the written value.
- Decision: the before runs are at `main` (`7dc353e8`), not only P2-188's numbers: P2-200, P2-184 and P2-165 merged after
  P2-188's runs, so only a run at `main` separates this ticket's change. `main` ran from a second worktree at a short
  path (`D:\eqm`, P2-200's path-length note), both builds against P2-200's `.corpus` (its checkouts and restores, linked
  into each worktree). Compare mode quick, `--jobs 4`, one run at a time; Git Extensions started only once another
  session's run on the same checkouts was past its load phase. Every run exited 1 (a Divergent result), no project
  skipped.
- Criterion 2, the four procedures, P2-188's run, then `main`, then this branch:

  | Pair | # | Procedure | P2-188 | `main` | P2-201 |
  |---|---|---|---|---|---|
  | Git Extensions | row 61 | `GitCommandsTests.Git.Tag.GitTagControllerTest::Setup()` | EQ006 | EQ006 | Unknown (`opaque`, `rebound-call`) |
  | Git Extensions | 51 | `GitUITests...FormFileHistoryControllerTests::TryGetExactPathName_should_check_if_path_matches_case(string,bool,bool)` | EQ006 | EQ006 | EQ006 |
  | Git Extensions | 55 | `GitUI.CommandsDialogs.FormResolveConflicts::Mergetool_Click(object,System.EventArgs)` | EQ006 | EQ006 | EQ006 |
  | OpenRA | 8 | `OpenRA.FileFormats.ReplayMetadata::RenameFile(string)` | EQ006 | EQ006 | EQ006 |

  Row 61's `Path.Combine(_workingDir, "TAGMESSAGE")` reads a `readonly` field initialized from NUnit's test directory, a
  listed source, and is no longer flagged; the procedure's other difference is NSubstitute's `Returns` bound to another
  overload by the package upgrade, P2-179's `rebound-call`. The three left, from the legacy checkouts, need more than a
  field's writers:
  - #51: `GitModuleTestHelper.TemporaryPath` is a get-only auto-property of a class in another project, written once in
    its constructor, which this rule reads. The value written is a local function's result, `Path.Combine(tempPath,
    Path.GetRandomFileName())`, `tempPath` being `Path.GetTempPath()` reassigned to `"/private" + tempPath` under a macOS
    check. It needs a local function read as a callee (P2-188 reads none), a constant prefix joined to a valid path, and
    `Path.GetRandomFileName` as a source.
  - #55: `GitModule.WorkingDir` is a get-only auto-property written in the constructor as
    `(workingDir ?? "").NormalizePath().EnsureTrailingPathSeparator()`. `NormalizePath` returns `string.Empty` for a blank
    path (a blank path meets `invalidPath`, though `Directory.SetCurrentDirectory("")` throws on both runtimes), and
    `EnsureTrailingPathSeparator` appends a separator with `+=` to its parameter. It needs an empty string's reach read
    per row and a separator appended to a valid path.
  - OpenRA #8: a property with a private setter whose writers are in other methods; out of scope (Size guard).

  These three are P2-208, filed by this ticket.
- Criterion 2, path-row EQ006 (counted as P2-188 did: an EQ006 whose `reviewGroup` names a row with the `invalidPath`
  precondition) and all EQ006:

  | Pair | Path-row EQ006: P2-188, P2-200, `main`, P2-201 | All EQ006: P2-188, P2-200, `main`, P2-201 |
  |---|---|---|
  | `gitextensions-8522` | 59, 60, 58, 56 | 157, 141, 139, 137 |
  | `duplicati-3124` | 100, 98, 98, 98 | 130, 117, 117, 117 |
  | `openra-17989` | 16, 16, 16, 16 | 45, 37, 37, 37 |
  | All three | 175, 174, 172, 170 | 332, 295, 293, 291 |

  `main` has two path-row EQ006 fewer on Git Extensions than P2-200's branch; P2-184 and P2-165 merged in between, and
  that difference is not attributed. Between `main` and this branch every other result of the three pairs is the same.
  The three that changed, all on Git Extensions and all correct by reading the source: row 61 above;
  `GitUITests.Editor.Diff.DiffLineNumAnalyzerTests::.ctor()`, EQ006 to Equivalent (`congruence`), whose
  `File.ReadAllText(Path.Combine(TestDataDir, "Sample.diff"))` reads a `static readonly` field initialized to
  `Path.Combine` of NUnit's test directory and two constants; and
  `GitUI.CommandsDialogs.FormGitIgnore::AddDefaultClick(object,System.EventArgs)`, Unknown (`abstraction`) to Equivalent
  (`congruence`), whose `File.Exists` and `File.ReadAllLines` read a `static readonly` field initialized to
  `Path.Combine(Environment.GetFolderPath(SpecialFolder.ApplicationData), ...)`. No result became Divergent or worse.
  Run times at `main` and this branch: Git Extensions 362 s and 321 s, Duplicati 803 s and 574 s, OpenRA 329 s and
  253 s; one run each, so the difference is not attributed.
- Decision: no `Release:` footer. A precision fix that changes no surface is a patch (`equiv-release`).
- Two open tickets share the id P2-202 (P2-200's and P2-184's); left as they are, not this ticket's to renumber.
