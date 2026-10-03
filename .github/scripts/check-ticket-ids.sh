#!/usr/bin/env bash
# Fails when two ticket files share an id, or when an open ticket's `Depends on:` line names an
# id that is not exactly one ticket file. Two PRs branched from the same `main` each take the
# next free number; whichever merges second must renumber its ticket before it lands. The
# pull_request run checks out the merge ref, so a number `main` already holds shows up here.
set -euo pipefail

cd "$(git rev-parse --show-toplevel)/docs/tickets"
status=0

ids() { find . -maxdepth 2 -name '*.md' -printf '%f\n' | grep -oE '^[A-Z][0-9]+-[0-9]+' || true; }

for id in $(ids | sort | uniq -d); do
  echo "::error::ticket id $id is used by more than one file:" $(find . -maxdepth 2 -name "$id-*.md")
  status=1
done

for f in [A-Z]*-[0-9]*.md; do
  for dep in $(grep -m1 -E '^Depends on:' "$f" | grep -oE '\b[A-Z][0-9]+-[0-9]+\b' || true); do
    n=$(find . -maxdepth 2 -name "$dep-*.md" | wc -l)
    if [ "$n" -ne 1 ]; then
      echo "::error file=docs/tickets/$f::depends on $dep, which matches $n ticket files"
      status=1
    fi
  done
done

exit $status
