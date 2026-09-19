# ADR 0011: EQ003-EQ005 stay non-`fail` SARIF results; visibility of Unknown is the exit code's job

Status: accepted (2026-09-18)

## Context
VERIFICATION-MODEL.md section 6 assigns `warning`/`note` "SARIF level" to Unknown (EQ003) and
Added/Removed (EQ004/EQ005). SARIF 2.1.0 s3.27.9 requires a result's own `level` to be absent
or `none` whenever `kind` is not `fail` — so `SarifReportWriter.ToResult` writes `level: none`
for these three rows (M1-004), which is spec-correct. Separately, Sarif.Sdk omits a rule's
`defaultConfiguration.level` from the JSON when it equals SARIF's own baseline default
(`warning`); EQ003's intended level *is* that baseline default, so its `defaultConfiguration`
is not written at all (see `SarifReportWriterTests.Unknown.verified.txt`). For a consumer that
reads only a result's own `level`, all three rows read as `none`. Whether GitHub Code Scanning
or SonarQube fall back to the rule default for a `kind != fail` result is unverified in this repo.

## Decision
EQ003 stays `kind: open`, EQ004/EQ005 stay `kind: informational`, each with result `level: none`
and the rule's `defaultConfiguration.level` set to `warning`/`note` as severity metadata only.
Divergent (EQ002, later EQ006) is the only `kind: fail` row. Consumer-visible severity for
Unknown is not a goal of the SARIF shape: the gate for Unknown is the CLI exit code
(`--fail-on unknown`, ARCHITECTURE.md), and the message text carries the reason.
VERIFICATION-MODEL.md section 6's `level` column is corrected to say what is actually written.

## Why
- It matches the verdict semantics: Unknown is "no answer obtained", not "a problem found".
  `kind: fail` would tell every consumer that an unproved procedure is the same category of
  finding as a proven divergence, which erodes trust in EQ002 — the one result that must be
  believed.
- The user-facing control over Unknown already exists and does not depend on any consumer's
  rendering: `--fail-on unknown` turns Unknown results into a failing exit code.
- It is the spec-correct SARIF shape today and needs no code change.
- It is cheap to reverse: `ResultFingerprint` hashes the verdict kind, not the SARIF `kind`,
  and the rule id stays EQ003, so switching EQ003 to `kind: fail` later causes no baseline
  churn (`unchanged` stays `unchanged`).

## Rejected
- Option B, EQ003 as `kind: fail` + `level: warning`: guarantees a badge in `level`-only
  consumers, but mislabels "not proved" as a finding in every consumer, and is being chosen on
  an unverified guess about consumer behaviour.
- Leaving the result's `level` unset instead of explicit `none`: SARIF treats an absent `level`
  on a `kind != fail` result as `none` anyway, so it changes nothing.

## Consequences
- VERIFICATION-MODEL.md section 6: the `level` column for EQ003-EQ005 reads `none` with the
  rule default in parentheses, and the caveat paragraph is replaced by a pointer to this ADR.
- No code change in M1-004.
- Reopen trigger: M3-003 (end-to-end samples) or whichever ticket first uploads real `samples/`
  output to GitHub Code Scanning or SonarQube records how EQ003 renders. If Unknown results are
  invisible there *and* users cannot be expected to run with `--fail-on unknown`, supersede this
  ADR with Option B.
