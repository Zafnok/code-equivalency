---
name: equiv-release
description: Versioning and release policy for this repo. Use when cutting a release, bumping a version, deciding semver (patch, minor or major), asking "is this breaking", adding a `Release:` footer, tagging, or when a release or its tag failed.
---

# Releases and versions

Semver, `vMAJOR.MINOR.PATCH` tags. Nobody picks version numbers by hand: the rolling release
computes them.

## How a release happens

- Every commit on `main` that is green in CI, CodeQL and SonarQube Cloud is tagged with the
  next version and released: single-file binaries, `ghcr.io/<owner>/equiv:<version>` and
  `:latest`, a GitHub release with generated notes. Only `main`'s tip is released; a failed
  commit is skipped and the next green one takes the version.
- The bump is patch unless a commit since the last tag carries a footer line on its own:
  `Release: minor` or `Release: major` (highest wins). Squash merges keep each commit's body,
  so the footer goes in the commit that makes the change.
- Manual: `gh workflow run rolling-release.yml -f bump=minor` (or `major`) releases `main`'s
  current tip with that bump if it is green, even when it already has its patch tag (MinVer
  reads the higher of the two). Dispatch once; a second dispatch bumps again.

## Which bump

- **Patch**: everything that does not change a public surface: fixes, newly lowered
  constructs, internal refactors, docs, tickets. Automatic; no footer.
- **Minor**: backwards-compatible additions worth announcing: a new CLI flag or subcommand, a
  new rule id, a batch of newly supported constructs, a finished milestone.
- **Major**: a backwards-incompatible change to a public surface: a CLI flag or subcommand
  removed or renamed, exit-code meanings, SARIF shape or rule ids, verdict meanings, config or
  baseline file formats, `action.yml` inputs/outputs, the container entrypoint or paths.

Pre-launch (while `0.x`): `1.0.0` is the launch. Until then a breaking change bumps MINOR
(semver's 0.x rule): write `Release: minor`, not `major`. The workflow downgrades a stray
`major` footer to minor in 0.x; only `gh workflow run rolling-release.yml -f bump=major`
cuts 1.0.0. After 1.0.0, breaking changes bump major and should be avoided: deprecate first,
remove in a later major.

## What an agent does

- In `equiv-task-loop`, when a PR changes a public surface listed above, add the `Release:`
  footer to the PR's final commit (next to `Ticket:`) and say so in the PR body. Anything
  else needs no footer.
- Never push `v*` tags by hand. To repair a failed release, re-run the failed jobs of its
  `Rolling release` run (`gh run rerun <id> --failed`; the tag job's output is reused). Only
  if that cannot work, a `v*` tag pushed by hand (your own credentials, not `GITHUB_TOKEN`)
  triggers `release.yml` directly; that is the one allowed hand-pushed tag.
- Prerelease tags (`-rc.N`) are not used by the rolling flow; the version computation
  ignores them.

## Where things are

- `.github/workflows/rolling-release.yml`: the green check, version computation and tag push
  (as github-actions[bot]); then calls `release.yml` with the tag, because a tag pushed with
  `GITHUB_TOKEN` triggers no workflows.
- `.github/workflows/release.yml`: builds and publishes one tag; also runs on a hand-pushed
  `v*` tag.
- MinVer reads the version from the tag (`Directory.Build.props`, `MinVerTagPrefix=v`).
- BUSL applies per version: each release's own `LICENSE` copy gets a Change Date four years
  after its release date (ADR 0017).
