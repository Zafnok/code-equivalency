#!/usr/bin/env bash
# Prints one SHA-256 over every tracked file at HEAD that can change a gates or Stryker
# result: path, mode and blob id from `git ls-tree`, so it is the same on every OS and
# costs no file reads. ci.yml and mutation.yml key a "this code already passed" cache
# entry on it, so a push that changes only prose (a ticket, an ADR, a skill) reuses the
# earlier pass instead of re-running the build, tests and mutation testing.
#
# The excluded paths are prose that no build, test or tool reads. A file that something
# does read stays in, even under docs/: IOPERATION-COVERAGE.md is read by
# Equiv.Tests.Integration. If a test starts reading another file under an excluded path,
# add it to the keep list below, or a change to it will be skipped instead of tested.
set -euo pipefail

git ls-tree -r --full-tree HEAD |
  awk -F '\t' '
    $2 == "docs/tickets/IOPERATION-COVERAGE.md" { print; next }
    $2 ~ /^(docs|\.claude)\// { next }
    $2 ~ /^(CLAUDE|CONTRIBUTING|README)\.md$/ { next }
    { print }
  ' |
  sha256sum |
  cut -d ' ' -f 1
