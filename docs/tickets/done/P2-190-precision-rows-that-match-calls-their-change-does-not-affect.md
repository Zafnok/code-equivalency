# P2-190 Precision bug: six runtime-change rows match calls their documented change does not affect
Status: done (PR #499)
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
P2-075 (done) narrowed two rows to the overloads their change affects. P2-130's adjudication
(`docs/runs/2026-10-14-migrations-verdict.md`) found 13 false EQ006 on all three migration pairs where
a row matches a call its change cannot reach:
- the ICU `System.String::IndexOf(` row matches `IndexOf(char)` (and by prefix the other `char`
  overloads), which compare ordinally on every runtime (1, `gitextensions-8522`); done by P2-181, whose
  `cultureSensitiveText` is never met by a call whose every argument but the receiver is a `char` or an
  `int`;
- the `Environment.OSVersion` row fires where the value is compared with 6.0 or 6.1, a threshold the
  compatibility shim (which reports 6.2 on Windows 8.1 and later) never crosses (2,
  `gitextensions-8522`), or where only `.Platform` is read (1, `openra-17989`);
- the `System.Math::Ceiling(double)` row (-0 against +0) fires on a result converted to `int` at once
  (2, `gitextensions-8522`);
- the `FieldInfo::SetValue(` row (static `readonly` fields) fires on a field from `GetField` or
  `GetFields` with constant flags that include `Instance` and not `Static` (3, `openra-17989`), and once
  behind a Mono-only guard that is false on both runtimes (1, `duplicati-3124`);
- the UTF-8 decoding rows fire on an `Encoding.ASCII` receiver (2, `openra-17989`);
- the `Process::Start(` row (the `UseShellExecute` default) fires on a `ProcessStartInfo` whose
  initializer sets `UseShellExecute` (1, `duplicati-3124`).

When done, each row matches only calls that can reach its change.

## Spec references
`docs/tickets/done/P2-075-*.md` and `P2-073-*.md`, `runtime-changes.json`, `RowPrecondition`, ADR 0040.

## Acceptance criteria (all must hold; nothing beyond them)
1. Each case above is either excluded by the row's match or precondition, with the documentation that
   shows the change cannot be reached, or recorded in Notes as kept, with why.
2. Rerun the three migration pairs in compare mode quick; Notes give the 13 procedures' new results.

## Tests
A precondition or match test per case, and for each a call that does reach the change and stays EQ006.

## Size guard
A data-flow analysis beyond one method: stop.

## Out of scope
Path rows (P2-188), constant-argument preconditions (P2-189).

## Notes
- Filed 2026-10-14 by P2-130 (criterion 4).
- 2026-10-14: P2-181 excluded the `char` searches from the ICU rows (the first case above).
- Decision: each row's narrowing is a `requires` fact (P2-114's `RowPrecondition`, checked once per method by
  `RowPreconditions.Suppress`) that holds when the method makes a call to the row's own member, or to a forwarder to it,
  that can reach the change. Alternatives: a per-call `RuntimeChangePrecondition` read from the call's operands (a
  result's use and a local's history are no operand, and the call site the lowering hands `CallIdentityFactory` comes from
  a control-flow graph that has neither the parent nor the method around it), a new per-call fact passed to `TryMatch`
  (a second route by which the lowering and the fingerprint could disagree). Rule: 1 (mirrors the existing `requires`
  path, which both consumers already read). It is coarser than per call only in a method that also makes a call that
  can reach, which then fires the row as before.
- Decision: a call to a forwarder (ADR 0047) to the row's member counts as a call to it, read at the forwarder's call
  site, because the lowering and the fingerprint flag the forwarded call by its target; a method reference to the member
  always reaches. Alternatives: ignoring forwarders (would suppress the row for a forwarded call no site in the method
  names: unsound). Rule: 1.
- Decision: the local-variable helpers (`Initializer`, `Writes`, `Root`, `Symbol`) move unchanged from `ValidPaths` to a
  new `LocalValues`, which also gets `HoldsOnly` (a value, or a local only ever assigned such values) and `References`.
  Alternatives: a second copy in `RowPreconditions`. Rule: 4.
- Decision: a guard test, `NoApiEquivalenceRewritesACallIntoARowThatRequiresACallFact`: a legacy call an API-equivalence
  entry rewrote into one of these members would be suppressed in a method with no direct call to it. No entry does
  today. Alternatives: teaching `Suppress` the rewrites (no entry needs it). Rule: 4.
- Criterion 1, case by case (all rows' documentation is the row's own `url`):
  - `String::IndexOf(char)`: done by P2-181 (`VerifyAffixKey` is EQ001 at this ticket's baseline).
  - `Environment::get_OSVersion(` (`shimmedVersionRead`): the documented change is the version .NET Framework reports
    without a manifest, 6.2.9200 on every Windows newer than 8, and the real one up to it. A read whose only use is
    `.Platform`, or `.Version` compared (`CompareTo` or an operator) with `new Version(major, minor)` of constants no newer
    than 6.2, gives the same answer on both: a two-part version has no build, so it sits strictly below 6.2.9200 and the
    shim never crosses it. Excluded: `IsWindowsVistaOrGreater` (6.0), `IsWindows7OrGreater` (6.1),
    `Platform::GetCurrentPlatform` (`.Platform` only).
  - `Math::Ceiling(double)` (`signedZeroResult`): the measured change is -0 against +0; an explicit conversion to an
    integer type turns both into 0. Excluded: `GetFontHeight`, `GetCursorPosition` (`(int)Math.Ceiling(...)`). The
    conversion itself is still the separate `float-to-integer` rule where a pair crosses .NET 9.
  - `FieldInfo::SetValue(` (`staticFieldSet`): the documented change is for static readonly fields only, and
    `GetField`/`GetFields` with flags that have `Instance` and not `Static` return no static field. Excluded:
    `ValueActorInit`1::Initialize`, `SubCellInit::Initialize` (`GetType().GetField("value", NonPublic | Instance)` in a
    local). Kept: `FieldLoader::Load`, whose `FieldInfo` comes from a cache another method (`BuildTypeLoadInfo`) fills
    with `Instance` flags (size guard: data flow beyond one method), and `OAuthHttpClient::.cctor()`, whose guard
    `Utility.IsMono` is a solution member read in another method (size guard) and whose field is looked up with
    `Static` flags, the one case the row is about.
  - `Encoding::GetCharCount(`, `GetChars(`, `GetString(` (`utf8Decoding`): the documented change is UTF-8's
    replacement of ill-formed input, and the rows flag the `Encoding` base members only because `Encoding.UTF8` binds to
    them. Excluded: a receiver that is `Encoding.ASCII`, or a local only ever assigned it: `StreamExts::ReadASCII`,
    `ReadASCIIZ`.
  - `Process::Start(` (`defaultShellExecute`): the documented change is `UseShellExecute`'s default; the row's reason
    also names `WindowStyle` being honoured without the shell. Excluded: `Start(ProcessStartInfo)` given a new
    `ProcessStartInfo` whose initializer sets `UseShellExecute`, or a local declared with one, never reassigned, used only
    for its properties (never `WindowStyle`) and as `Process.Start`'s argument, whose initializer or an earlier statement
    of a block the call runs in (a block with no label, and not across a lambda or local function) sets
    `UseShellExecute`: `OSInfoHelper::RunProgramAndReadOutput` (initializer) and the `Process.Start` in
    `Platform::GetCurrentPlatform` (statement), which would have kept that body EQ006 once its `OSVersion` read was
    excluded.
- The fingerprint renders a property by all its accessors, so a body that only writes `UseShellExecute` still names the
  `ProcessStartInfo::get_UseShellExecute(` row and is not congruent; the lowering calls the setter, which has no row, so
  no EQ006 comes of it. Left as it is: it costs congruence, not a false Divergent.
- Criterion 2, the reruns: 2026-10-14, compare mode quick, `--jobs 4`, one after the other, on the checkouts
  P2-130 fetched and restored. Baseline at `main`'s head `fc7d46ca`, after at `8281dbe4` (this branch's fix):
  `gitextensions-8522` 470 s and 551 s, `duplicati-3124` 621 s and 513 s, `openra-17989` 406 s and 289 s, each
  exit 1. The 13 procedures, baseline then after:

  | Pair | Procedure | Row | Baseline | After |
  |---|---|---|---|---|
  | gitextensions-8522 | `WordDictionary::VerifyAffixKey(string,char)` | `String::IndexOf(` | EQ001 (P2-181) | EQ001 |
  | gitextensions-8522 | `EnvUtils::IsWindowsVistaOrGreater()` | `get_OSVersion(` | EQ006 | EQ001 |
  | gitextensions-8522 | `EnvUtils::IsWindows7OrGreater()` | `get_OSVersion(` | EQ006 | EQ001 |
  | gitextensions-8522 | `TextView::GetFontHeight(Font)` | `Math::Ceiling(double)` | EQ006 | EQ001 |
  | gitextensions-8522 | `EditNetSpell::GetCursorPosition()` | `Math::Ceiling(double)` | EQ006 | EQ001 |
  | duplicati-3124 | `OSInfoHelper::RunProgramAndReadOutput(string,string)` | `Process::Start(` | EQ006 | EQ001 |
  | duplicati-3124 | `OAuthHttpClient::.cctor()` | `FieldInfo::SetValue(` | EQ006 | EQ006 (kept) |
  | openra-17989 | `ValueActorInit`1::Initialize(T)` | `FieldInfo::SetValue(` | EQ006 | EQ001 |
  | openra-17989 | `SubCellInit::Initialize(int)` | `FieldInfo::SetValue(` | EQ006 | EQ001 |
  | openra-17989 | `FieldLoader::Load(object,MiniYaml)` | `FieldInfo::SetValue(` | EQ006 | EQ006 (kept) |
  | openra-17989 | `StreamExts::ReadASCII(Stream,int)` | `Encoding::GetChars(` | EQ006 | EQ001 |
  | openra-17989 | `StreamExts::ReadASCIIZ(Stream)` | `Encoding::GetChars(` | EQ006 | EQ001 |
  | openra-17989 | `Platform::GetCurrentPlatform()` | `get_OSVersion(`, `Process::Start(` | EQ006 | EQ001 |

  10 of the 13 are now proved Equivalent; `VerifyAffixKey` was already; 2 are kept, as above.
- Criterion 2, every result that moved: EQ006 159 to 151, 134 to 126 and 45 to 37 (24 fewer in all); EQ002 unchanged
  (29, 24, 2); 11 Unknowns proved (4, 6, 1: EQ003 585 to 581, 333 to 328, 255 to 254, one Duplicati EQ006 became
  Unknown); results not congruent 896 to 884, 675 to 672, 306 to 298. Each of the 24 results that left EQ006 was read
  by hand, and in each every call to the row's member is one the rows' documentation shows cannot reach the change:
  - the 12 above (less the two kept);
  - `gitextensions-8522`: `EnvUtils::IsWindows8OrGreater()` (`Version.CompareTo(new Version(6, 2))`) and
    `RunningOnWindows()`, `RunningOnUnix()`, `RunningOnMacOSX()` (`.Platform` only). `IsWindows8Point1OrGreater()`
    (6.3) stays EQ006, as it must;
  - `duplicati-3124`: `Platform::_RetrieveUname`, `MacOSPowerSupplyState::GetSourcePmset`, `Utility::Which`,
    `SynologyAuthenticationHandler::ShellExec` (EQ001) and `ProcessController::StartSleepPrevention` (now Unknown,
    opaque), each a `ProcessStartInfo` whose initializer sets `UseShellExecute = false` and no `WindowStyle`;
    `ConsolePasswordInput::ReadPassphraseFromConsole` and `ReportSet::DoInitOS` (`OSVersion.Platform` only);
  - `openra-17989`: `CompositeActorInit::Initialize(Dictionary)` (a `foreach` over
    `GetFields(Public | NonPublic | Instance)`) and `OwnerInit::Initialize(MiniYaml)`, `OwnerInit::Initialize(Player)`
    (`GetField(..., Instance)`).
- New ticket P2-199 (S): the fingerprint renders a property by all its accessors, so a body that only writes
  `UseShellExecute` still names the `ProcessStartInfo::get_UseShellExecute(` row and goes to the solver: 9 results on
  `duplicati-3124` and `openra-17989` name only that row after this ticket (8 proved, 1 Unknown). It costs a solver
  run, never a Divergent, so it is not this ticket's.
- Run time: the first after-run of `gitextensions-8522` spent 164 s lowering against the baseline's 107 s. A second
  after-run on the same build (448 s, lowering 91 s) gave the same results to the procedure, so the box's load made
  the difference, not the per-method scan; `duplicati-3124` (43 s to 42 s) and `openra-17989` (100 s to 75 s) lowered
  no slower. The baselines shared the box with this ticket's test runs, so no time here compares with P2-181's.
