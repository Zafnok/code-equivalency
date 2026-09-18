# ADR 0011: Whether EQ003-EQ005 need a visible severity in SARIF consumers

Status: proposed (2026-09-18)

## Context
VERIFICATION-MODEL.md section 6 assigns `warning`/`note` "SARIF level" to Unknown (EQ003) and
Added/Removed (EQ004/EQ005). SARIF 2.1.0 s3.27.9 requires a result's own `level` to be absent
or `none` whenever `kind` is not `fail` — so `SarifReportWriter.ToResult` writes `level: none`
for these three rows (M1-004), which is spec-correct. Separately, Sarif.Sdk omits a rule's
`defaultConfiguration.level` from the JSON when it equals SARIF's own baseline default
(`warning`); EQ003's intended level *is* that baseline default, so its `defaultConfiguration`
is not written at all (verified via `tests/Equiv.Core.Tests/Reporting/SarifReportWriterTests.
Unknown.verified.txt`). EQ004/EQ005's `note` level, being non-default, is written.

The result: for a consumer that reads a result's own `level` and stops there (does not walk to
`rules[].defaultConfiguration.level` when a result's `level` is present-but-`none`), all three
rows read as `none` — indistinguishable from "not a problem." Whether GitHub Code Scanning or
SonarQube actually perform that walk for a `kind != fail` result is not verified by this repo
(no integration ticket has run real output through either consumer yet). This is a real
open question, not an implementation detail: it changes what VERIFICATION-MODEL.md section 6's
`level` column means in practice, and equiv-decide's own checklist excludes "SARIF shape"
changes from being decided without an ADR.

## Decision
Not yet made. Two options, both compatible with the code already merged in M1-004:

**Option A — accept as-is.** Keep EQ003 `kind: open`, EQ004/EQ005 `kind: informational`,
matching Divergent's `fail`/`error` as the only row designed to be "loud." Unknown/Added/Removed
stay informational entries: visible in the SARIF file and its message text, and to any tool
that reads `kind` (not just `level`), but not guaranteed to render with a warning/note badge in
a specific consumer's UI. Section 6's `level` column is then read as "the severity this result
would carry if its consumer honours per-kind severity," not a guarantee.

**Option B — make EQ003 loud.** Change Unknown to `kind: fail`, `level: warning` (EQ004/EQ005
stay informational; they are not equivalence problems). This makes Unknown behave like Divergent
for consumers that only look at `level`+`kind == fail` — at the cost of an Unknown result (e.g.
"opaque, ran out of time") looking, to a naive consumer, like the same category of problem as a
proven divergence, which VERIFICATION-MODEL.md's own verdict semantics treats as a different
thing (an admission of not knowing, not a finding). This is a table change (section 6, row for
Unknown), so it supersedes the existing row rather than filling a gap in it.

## Why (leaning, not decided)
- Option A costs nothing further and matches the verdict semantics already written down:
  Unknown is deliberately not "a problem found," it is "no answer obtained." Conflating it
  with Divergent for the sake of consumer visibility could produce noisier, less trustworthy
  reports (an MVP goal per ARCHITECTURE.md's "what is deliberately NOT in the MVP" framing:
  minimise noise before proving the loud path (Divergent) actually works end to end against
  `samples/`).
- Option B guarantees visibility in tools that only key off `level`+`kind: fail`, which is
  worth confirming against real GitHub Code Scanning / SonarQube output once a milestone
  actually exercises that integration (M3, per QUALITY-GATES.md's CI matrix) — deciding now,
  without that evidence, risks guessing wrong in either direction.

## Rejected
- Leaving the result's own `level` unset rather than explicit `none`: still satisfies "absent
  or none" (SARIF s3.27.9) for kind != fail, but does not change the underlying question (a
  consumer's fallback-to-rule-default behaviour is unverified either way), so it does not
  resolve anything by itself.

## Consequences
- Until this ADR is accepted, VERIFICATION-MODEL.md section 6 carries a caveat (this PR) rather
  than a settled claim about consumer-visible severity for EQ003-EQ005.
- Whichever option is accepted, the ticket that first wires real `samples/` output into GitHub
  Code Scanning or SonarQube (M3) should record what was actually observed, which may reopen
  this ADR regardless of which option is chosen now.
