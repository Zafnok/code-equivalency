# ADR 0019: Verdicts are modular, and an Equivalent names the callee pairs it assumed

Status: accepted (2026-09-21)

## Context
VERIFICATION-MODEL section 5 encodes every call as an uninterpreted function that both sides
share. When the callee is the user's own code, and itself a matched pair, that sharing assumes the
callee pair is equivalent: the mutual-summary assumption of SymDiff. The spec never says so, and the
SARIF gives no sign of it. If `Total()` calls `Tax()` on both sides and `Tax` changed, `Tax` is
Divergent and `Total` is reported plainly Equivalent, even though `Total`'s real behaviour changed.
Someone reading `Total`'s result alone would conclude it is unaffected by the migration.

## Decision
Every verdict is per procedure: it is conditional on the matched callee pairs its bodies call being
equivalent. The CLI makes that condition visible without changing any verdict or exit code.
- `properties.assumedCallees` lists, for every result with a matched pair of bodies, the call
  identities in either body that are themselves matched pairs in the same run, sorted, without
  duplicates.
- `properties.unprovenAssumptions` is the subset of those whose own verdict in this run is not
  Equivalent.
- When `unprovenAssumptions` is non-empty on an Equivalent result, the SARIF message gains one
  sentence naming them, for example: "Assumes callees equivalent; not proved for: X, Y."

## Why
- The exit code is already correct. A Divergent or Unknown callee is its own result, and that
  result drives the exit code and `--fail-on unknown`. What is missing is what the caller's result
  tells a reader, so the fix belongs in reporting.
- Downgrading a caller to Unknown whenever any callee is not Equivalent would spread one real
  divergence up the whole call graph and bury the one result that locates it.
- The two lists cost one pass over results the CLI already holds.

## Rejected
- **Inlining matched user callees instead of treating them as uninterpreted functions.** This
  explodes on recursion and deep call chains, and it discards the modularity that makes regression
  verification scale.
- **Downgrading the caller to Unknown.** Rejected for the reason given under Why.
- **Documenting the assumption in VERIFICATION-MODEL only.** A reader of the SARIF never sees the
  spec.

## Consequences
- VERIFICATION-MODEL section 1 states the modular reading, and section 6 lists the two properties.
- New ticket M3-008 implements this after M3-003, so that its snapshots are approved once.
- M3-003's snapshots do not yet carry the properties. M3-008 re-approves them.
