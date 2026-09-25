# Corpus runs

One directory per run of the `equiv-corpus-run` skill: `docs/runs/<yyyy-mm-dd>-<mode>-<slug>/`,
holding only `SUMMARY.md`. A run over several pairs also writes
`docs/runs/<yyyy-mm-dd>-<mode>-verdict.md`, which applies ADR 0028's criteria across them.

What may be committed here: counts, percentages, opaque and Unknown reason names, rule ids,
`proofMethod` and `scope` values, seed ids, procedure identities (namespace, type and member names),
upstream commit SHAs, tool and model names, and wall-clock times.

What may not: source text, snippets, file contents, counterexample values, SARIF files, or anything
else copied from a corpus repository. Those stay in `.corpus/`, which git ignores (ADR 0028). The
summary template is in `.claude/skills/equiv-corpus-run/SKILL.md`.

A project that loaded but whose enumeration found zero procedures counts against the project load
rate the same as one the loader itself skipped, never as a clean load with an empty result (ADR 0029; P2-018).
