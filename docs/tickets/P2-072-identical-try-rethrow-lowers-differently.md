# P2-072 Two identical bodies with a rethrowing catch lower to different call traces
Status: in-progress
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-047

## Goal
P2-047's audit classed one EQ002 as a false positive on a method whose source it took to be
byte-identical on both sides (`EasyHook.LocalHook::Create(System.IntPtr,System.Delegate,object)` on
Git Extensions, a vendored file): the model's modern trace skips the calls inside a `try` block, and
the catch body that rethrows, while the legacy trace makes them. Find why the two sides lower
differently and fix it, or show that they are right to.

Outcome: they are right to. The two bodies are not the same source (see Notes), the verdict is
correct, and nothing in `src/` changes. This ticket corrects the audit instead.

## Spec references
VERIFICATION-MODEL section 3 (exception lowering), ADR 0039 (IL fallback: the two lowerings must
agree), P2-047's audit.

## Acceptance criteria (all must hold; nothing beyond them)
1. The cause is named in `## Notes`.
2. Row 17 of `docs/runs/2026-09-30-divergent-audit.md` and the counts that depend on it are corrected,
   with a dated correction note, and `docs/ROADMAP.md` and P2-107 no longer cite a lowering bug.

Withdrawn (see the `Deviation:` line): a sample that is EQ002 today and congruent after, and the Git
Extensions method no longer being EQ002.

## Tests
None: no code changed. The ticket's own repro, with `int` in place of `IntPtr` (see Notes), was run as
a sample pair (net48 against net10.0 with `<Nullable>enable</Nullable>`) and is Equivalent by
congruence on `main`.

## Out of scope
Other vendored EasyHook methods. The `IntPtr` identity finding in Notes.

## Notes
- Found by P2-047 (row 17 of the audit's appendix).
- Cause: the matched bodies are different source. The legacy solution builds
  `Externals/EasyHook/EasyHook/LocalHook.cs`. The modern solution has no EasyHook project: that
  directory is still in the checkout, unchanged, but `GitExtensions.sln` no longer lists it, and
  `EasyHook.LocalHook` is now compiled into GitUI from `GitUI/Theming/LocalHook.cs`, an edited copy.
  In the copy the call in `Create`'s `try` block is inside `#if SUPPORT_THEMES`, a symbol no project,
  props file or source file on the modern side defines, and `catch (Exception e) { ...; throw e; }`
  became `catch (Exception) { ...; throw; }`. So the modern `try` block is empty, its catch is
  unreachable, and the modern method never makes the hook-installing call the legacy one makes. The
  model says exactly that. The audit diffed the legacy file against the leftover file of the same path
  in the modern checkout, which is why it saw identical source.
- Checked on `main` (`0639110`): `equiv compare` on the two checkouts' `Externals/EasyHook/EasyHook.sln`,
  where the file really is the same on both sides, gives EQ001 by congruence for every method,
  `Create` included. The full-solution runs of 2026-10-02 still give EQ002 for `Create`, as they should.
- Deviation: criteria 2 and 3 as written (a sample that turns from EQ002 to congruent, and the Git
  Extensions method no longer EQ002) cannot be met without making a correct Divergent unsound. There
  is no lowering difference to reproduce or fix. They are replaced by the audit correction above.
  `equiv-adr` bar test: the ticket text is wrong, the spec and ADRs stay as they are.
- Decision: row 17 becomes undetermined, "real difference by hand trace, not executed", not confirmed.
  The audit's rule is that confirmed needs a replay or a test, and replay cannot observe a call-trace
  difference.
- Decision: no sample is added. An identical `try` with a rethrowing catch is already congruent, and
  `samples/` has no gap this ticket found.
- Surprise, not fixed here: the ticket's repro as written (parameters of type `IntPtr`, net48 against
  net10.0) does not match at all. From .NET 7 Roslyn spells `System.IntPtr` as `nint`, so the legacy
  identity `Install(System.Func<global::System.IntPtr>,System.IntPtr)` and the modern
  `Install(System.Func<nint>,nint)` are reported as one EQ005 and one EQ004 on byte-identical source.
  The legacy spelling also carries a stray `global::` inside the type argument. Git Extensions did not
  show it because its modern side is net5.0. It needs its own ticket.
