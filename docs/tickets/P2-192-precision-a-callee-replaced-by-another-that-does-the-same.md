# P2-192 Precision bug: a pull request that replaces a callee one for one by another member that does the same is Divergent
Status: in-progress
Effort: L
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
A migration replaces APIs the new runtime lacks. P2-130's adjudication of `duplicati-3124`
(`docs/runs/2026-10-14-migrations-verdict.md`) found 11 false EQ002 where every call site of one
member is replaced, one for one, by a member that behaves the same, and no rule can say so:
- 7: `System.Runtime.Remoting.Messaging.CallContext::LogicalSetData(string,object)` and
  `::LogicalGetData(string)` (absent from net5.0) replaced by a solution-local
  `Duplicati.Logging.Duplicati.Library.Logging.CallContext` over `AsyncLocal<object>`: the same named
  slot with the same flow. It is not a forwarder (ADR 0047), so the two callees are unrelated and the
  shim's `threw` is free;
- 3: a one-line helper forwarding to the third-party `FasterHashing.FasterHash::Create(string)`
  replaced by `System.Security.Cryptography.HashAlgorithm::Create(string)`, with the constant name of
  SHA-256 on both sides;
- 1: a synchronous AWS SDK call replaced by its `Async` twin followed by `GetAwaiter().GetResult()`.

ADR 0020's catalogue maps one base class library member to another. None of these fits: the modern
member is the solution's own, or the legacy one is a third party's, or the modern side is two calls.

When done, a user (or the catalogue) can say that two such members are one call, and how that is kept
from hiding a real difference is decided.

## Spec references
ADR 0020, ADR 0047, ADR 0045, `api-equivalences.json`, `.claude/skills/equiv-adr`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Decide, through `equiv-adr`, how such an equivalence is declared and checked: for example a
   pair-level mapping whose modern side is a solution member that is itself verified against a
   specification, or a catalogue entry for `sync = Async + GetAwaiter().GetResult()`.
2. Implement the largest case (the `CallContext` replacement), with a sample pair.
3. Rerun `duplicati-3124` in compare mode quick; Notes give the 11 procedures' new results.

## Tests
As the decision requires; a pair whose replacement behaves differently stays Divergent.

## Size guard
More than one new kind of equivalence: stop and split.

## Out of scope
Base class library one-call pairs (P2-137's catalogue).

## Notes
- Filed 2026-10-14 by P2-130 (criterion 4).
- Criterion 1. `equiv-adr`'s bar test: a new SARIF property, so a new ADR, ADR 0058 (accepted in this PR, as P2-118 did
  with ADR 0055). The config's `callIdentityRenames` already makes a legacy identity the same function as a modern one
  in every encoder (`TraceEncoder.Canonical`): on `samples/callee-replacement`, without any code change, it turned the
  two callers that make the same calls from EQ002 into EQ001, and the caller that stores another value stayed EQ002.
  What was missing is ADR 0019's stance: nothing said the Equivalent rested on the user's entry. So the declaration
  stays that map, and the check is the report. The mapped legacy identity is an assumed callee no result of the run
  proves (`assumedCallees`, `unprovenAssumptions`, the Equivalent's message), with its counterpart in
  `properties.callIdentitiesMapped`, and the contracts pass never takes it. ADR 0058's Rejected list gives the
  alternatives: a new config key, a catalogue entry, a specification of the remoting member, real runtimes, inference.
- Decision: spell the declared pair in `unprovenAssumptions` by its legacy identity, the map key, not by the modern one,
  which can be a matched pair proved Equivalent and would then hide the assumption (ADR 0058, Rejected).
- Decision: a sample's own `equiv.config.json` is passed by every end-to-end run of that sample
  (`SamplesEndToEndTests.ConfigPath`), so two runs of `callee-replacement` differ only in what a test varies. Only this
  sample has one. The MCP parity test runs both sides without it, and they agree.
- Size guard: the three groups are two kinds. The `CallContext` shim (7) and the hashing helper (3) are one call for
  one call, which the map declares. The AWS SDK call (1) is one call against `Async` plus `GetAwaiter().GetResult()`,
  another kind: filed as P2-202. The hashing helper is not an ADR 0047 forwarder in the run (the legacy calls stay
  `Duplicati.Library.Utility.HashAlgorithmHelper::Create(string)`), so its entry maps the helper itself.
- Criterion 3. `duplicati-3124`, compare mode quick, `--jobs 4`, on this branch (main `0e18ba4c` plus this change),
  `./tools/corpus/corpus.ps1 -Compare duplicati-3124 -Mode quick -Tag p2-192 -CompareArgs '--jobs', '4', '--config', <file>`:
  exit 1, 917 s (lower 63 s, verify 798 s; the box also ran this repo's sample tests for the first 4 minutes). The
  config, kept under `.corpus/` and not committed, declares three entries:
  `System.Runtime.Remoting.Messaging.CallContext::LogicalGetData(string)` →
  `Duplicati.Logging.Duplicati.Library.Logging.CallContext::GetData(string)`, `::LogicalSetData(string,object)` →
  `...CallContext::SetData(string,object)`, and `Duplicati.Library.Utility.HashAlgorithmHelper::Create(string)` →
  `System.Security.Cryptography.HashAlgorithm::Create(string)`. "Before" is the 2026-10-14 quick run P2-189 made on the
  same box with no config, where all 13 were EQ002. Row numbers are P2-130's sample rows.

  | row | procedure | before | after | unproven assumption |
  |---|---|---|---|---|
  | 0 | `LocalizationContext::Dispose()` | EQ002 | EQ001 bounded | `LogicalSetData` |
  | 3 | `LocalizationContext::.ctor(CultureInfo)` | EQ002 | EQ002 | `LogicalGetData`, `LogicalSetData` |
  | 8 | `CallContextSettings`1.ContextGuard::Dispose()` | EQ002 | EQ001 bounded | `LogicalSetData` |
  | 11 | `CallContextSettings`1::get_ContextID()` | EQ002 | EQ001 bounded | `LogicalGetData` |
  | 13 | `CallContextSettings`1.ContextGuard::.ctor()` | EQ002 | EQ001 bounded | `LogicalSetData` |
  | 14 | `LocalizationService::get_Current()` | EQ002 | EQ001 bounded | `LogicalGetData` |
  | 23 | `Log::set_CurrentScope(LogScope)` | EQ002 | EQ001 bounded | `LogicalSetData` |
  | 2 | `Utility::CalculateHash(Stream)` | EQ002 | EQ002 | `HashAlgorithmHelper::Create` |
  | 7 | `BackendManager::CalculateFileHash(Stream)` | EQ002 | EQ001 bounded | `HashAlgorithmHelper::Create` |
  | 16 | `BackendManager::GetFileHasherStream(...)` | EQ002 | EQ001 bounded | `HashAlgorithmHelper::Create` |
  | 20 | `S3IAM::CanCreateUser(string,string)` | EQ002 | EQ002 | none (P2-202) |
  | 12 | `HashCalculatingStream::.ctor(Stream,string)` (undetermined) | EQ002 | EQ001 bounded | `HashAlgorithmHelper::Create` |
  | 22 | `Metahash::.ctor(...)` (undetermined) | EQ002 | EQ001 bounded | `HashAlgorithmHelper::Create` |

  8 of the 11 move to EQ001: 6 of the 7 `CallContext` ones and 2 of the 3 hashing ones. Each names its declared callee
  in `unprovenAssumptions` and `callIdentitiesMapped`. In all, 27 results of the run carry `callIdentitiesMapped`, 14 of
  them EQ001. The two undetermined hashing rows are EQ001 too. They rest on the declaration that FasterHashing's
  factory and the framework's agree for every name, which P2-130 could not check (binary only), and the results say
  so. The three still Divergent:
  - row 3: the modern side stores `CallContext.GetData(key) as string` into an `object` field where the legacy side
    stored the value itself. With the callee declared, the counterexample is a slot holding a non-string. That is a real
    difference at procedure level: P2-130 counted the pair false on the whole program, where only strings are stored.
    No ticket: Divergent is the right verdict for the procedure.
  - row 2: the legacy side passes the `public static` property `Utility.HashAlgorithm`, the modern side the new
    `private static` `Utility.BlockHashAlgorithm`, and both getters are `=> "SHA256"`. ADR 0045 reads neither (an
    accessor; the legacy one is public). Filed as P2-203.
  - row 20: one call against `Async` plus `GetAwaiter().GetResult()`, P2-202.
  Totals are not compared: the before run predates P2-188 and P2-190 on `main`.
