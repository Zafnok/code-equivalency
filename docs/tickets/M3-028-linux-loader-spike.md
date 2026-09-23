# M3-028 Spike: load both sides of every sample on Linux
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M3-024

## Goal
ADR 0031 requires Linux parity but leaves the loading mechanism open. This spike tries its three
candidates on `ubuntu-latest` against every sample and the legacy side of one corpus pair, then
records which one to build. It produces a decision and an implementation ticket, not product code.

## Spec references
ADR 0031, ADR 0004, M2-001 (loader contract, pitfalls), ADR 0028 (corpus, via `equiv-corpus-run`).

## Acceptance criteria (all must hold; nothing beyond them)
1. A throwaway branch (not merged) holds one probe per candidate. Each probe, on `ubuntu-latest`,
   loads the legacy and modern side of every `samples/` pair and of the corpus pair
   `eshop-upgrade-assistant`, and prints per project: loaded / skipped, source-file count,
   reference count, and error diagnostics count.
2. The same probe output from the current Windows loader is the reference. Notes hold a table:
   candidate × solution → match / mismatch (with the first difference).
3. Notes record, per candidate: what must be in the container image (SDK or not, extra
   packages), image size estimate, and whether `packages.config` restore works without Mono or
   nuget.exe.
4. A `## Clarifications` bullet on ADR 0031 names the chosen candidate and why. If none matches
   Windows on some projects, Notes list those projects with the MSBuild feature that blocked
   them (COM reference, custom target generating Compile items, missing import, ...) and the
   bullet says whether that residue is taken as Unknown/skipped (ADR 0029) or needs ADR 0031's
   Windows-worker fallback.
5. A new ticket `M3-029-linux-loader.md` implements the chosen candidate. Its acceptance criteria
   include a CI parity job that runs `equiv compare` on every sample on Windows and Ubuntu and
   diffs the SARIF results (ignoring paths). M3-004 gets `Depends on: M3-029`, and its criteria 1
   and 2 are rewritten so the Linux binary and the container analyse samples instead of exiting 3.

## Files
`docs/adr/0031-linux-parity-is-a-release-requirement.md` (Clarifications only),
`docs/tickets/M3-028-linux-loader-spike.md` (Notes), `docs/tickets/M3-029-linux-loader.md` (new),
`docs/tickets/M3-004-packaging.md`, `docs/ROADMAP.md`.

## Tests
None merged; the probes are evidence, not product.

## Size guard
Any change under `src/` or `tests/` on the merged branch means you built instead of measured.

## Out of scope
Implementing the loader, the container, arm64, non-C# projects.

## Notes
