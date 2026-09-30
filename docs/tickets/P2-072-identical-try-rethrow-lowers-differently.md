# P2-072 Two identical bodies with a rethrowing catch lower to different call traces
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-047

## Goal
P2-047's audit found one false Divergent on a method whose source is byte-identical on both sides
(`EasyHook.LocalHook::Create(System.IntPtr,System.Delegate,object)` on Git Extensions, a vendored
file). The model's modern trace skips the calls inside a `try` block, and the catch body that
rethrows, while the legacy trace makes them. Identical source must lower to the same IR. The
difference comes from the frontend, not the code: a different language version, nullable context
or reference assemblies on the two sides. Minimal repro to start from, as a sample pair (the same file
on both sides, legacy targeting net48):

```csharp
static int Install(Func<IntPtr> get, IntPtr h)
{
    int r = 0;
    try { r = Hook(get(), h); }
    catch (Exception e) { throw e; }
    return r + 1;
}
static int Hook(IntPtr a, IntPtr b) => a == b ? 1 : 0;
```

Find the construct that makes the two lowerings differ, reproduce it in this sample, and fix the
lowering. If this repro does not trigger it, rebuild the repro from the Git Extensions method under
`.corpus/` (source never committed) until one does.

## Spec references
VERIFICATION-MODEL section 3 (exception lowering), ADR 0039 (IL fallback: the two lowerings must
agree), P2-047's audit.

## Acceptance criteria (all must hold; nothing beyond them)
1. The cause is named in `## Notes`.
2. A sample under `samples/` that shows the cause is congruent or Equivalent, where today it is EQ002.
3. `equiv compare` on the Git Extensions pair, restricted to that method (on a copy under `.corpus/`),
   is not EQ002.

## Tests
- Integration test on the new sample.
- A unit test in the affected frontend test project that lowers the construct under both targets and
  compares the IR.

## Out of scope
Other vendored EasyHook methods unless they share the cause.

## Notes
- Found by P2-047 (row 17 of the audit's appendix).
