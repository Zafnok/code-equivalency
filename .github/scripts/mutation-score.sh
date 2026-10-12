#!/usr/bin/env bash
# Adds up the JSON reports of one project's Stryker shards and applies the break threshold
# that a single unsharded run applied with --break-at (P2-133).
#
#   mutation-score.sh <src> <expected-shards> <break-at> <reports-dir>
#
# <reports-dir> holds one mutation-report.json per shard, anywhere below it (the downloaded
# artifacts). A report is a mutation-testing-elements document: files.<path>.mutants[].status.
# Every shard of a project lists every mutant of the project, and marks the ones outside its
# own patterns Ignored. A mutant that crosses a cut inside a file is tested by the shards on
# both sides of it (P2-160; see mutation-plan.sh), so the sum is over distinct mutants: one
# per file, location, mutator and replacement. Where two shards disagree about one, which
# only a flaky test can cause, the undetected status counts.
#
# The score is Stryker's own (ProjectComponent.GetMutationScore): detected / (detected +
# undetected), where detected is Killed + Timeout and undetected is Survived + NoCoverage.
# Ignored, CompileError and RuntimeError mutants are in neither. With nothing in either there
# is no score and the project passes, as an unsharded run with no mutants did. The run fails
# when the score is below <break-at> percent, compared in whole numbers, and when a shard's
# report is missing: a shard that failed or ran into its time limit has tested nothing.
set -euo pipefail

src=$1
expected=$2
break_at=$3
dir=$4

mapfile -t reports < <(find "$dir" -name mutation-report.json | sort)
if [[ "${#reports[@]}" -ne "$expected" ]]; then
  echo "::error::Stryker ($src): $expected shards were planned and ${#reports[@]} reported. A shard failed, was cancelled, or ran into its time limit; see the stryker-shard ($src, n/$expected) jobs." >&2
  exit 1
fi

# One report at a time, and only the mutants a shard tested: a report is tens of megabytes
# (it lists every test that covers every mutant), and a sweep has dozens.
tested=$(mktemp)
mutants=$(mktemp)
for report in "${reports[@]}"; do
  jq -c '.files | to_entries[] | .key as $file | .value.mutants[] | select(.status != "Ignored")
         | { file: $file, location, mutatorName, replacement, status }' "$report"
done > "$tested"
jq -cs 'def rank: . as $status | { Survived: 0, NoCoverage: 1, Killed: 2, Timeout: 3 } | .[$status] // 4;
        group_by([.file, .location, .mutatorName, .replacement]) | map(min_by(.status | rank))' "$tested" > "$mutants"

counts=$(jq -c 'map(.status) | group_by(.) | map({ (.[0]): length }) | add // {}' "$mutants")
count() {
  local status=$1
  jq --arg s "$status" '.[$s] // 0' <<< "$counts"
}
killed=$(count Killed)
timeout=$(count Timeout)
survived=$(count Survived)
no_coverage=$(count NoCoverage)
detected=$((killed + timeout))
valid=$((detected + survived + no_coverage))

summary=${GITHUB_STEP_SUMMARY:-/dev/stdout}
{
  echo "### Stryker ($src), $expected shards"
  echo
  echo "| Killed | Timeout | Survived | No coverage |"
  echo "|---|---|---|---|"
  echo "| $killed | $timeout | $survived | $no_coverage |"
  echo
} >> "$summary"

if [[ "$valid" -eq 0 ]]; then
  echo "No mutants were tested, so there is no score." >> "$summary"
  echo "::notice::Stryker ($src): the shards tested no mutants, so there is no score; it passes."
  exit 0
fi

score=$(jq -n --argjson d "$detected" --argjson v "$valid" '$d * 10000 / $v | floor / 100')
{
  echo "Mutation score: $score% ($detected of $valid), break at $break_at%."
  if [[ $((survived + no_coverage)) -gt 0 ]]; then
    echo
    echo "Not detected (the first 200):"
    echo
    echo '```'
    jq -r '[.[] | select(.status == "Survived" or .status == "NoCoverage")
            | "\(.file):\(.location.start.line) \(.status) \(.mutatorName)"]
           | sort | .[:200][]' "$mutants"
    echo '```'
  fi
} >> "$summary"

echo "Stryker ($src): killed $killed, timeout $timeout, survived $survived, no coverage $no_coverage; score $score%."
if [[ $((detected * 100)) -lt $((break_at * valid)) ]]; then
  echo "::error::Stryker ($src): mutation score $score% is below the break threshold of $break_at%." >&2
  exit 1
fi
