#!/usr/bin/env bash
# Plans mutation.yml's Stryker shards (P2-133). For every project it lists the src/<project>
# .cs files this run mutates and splits them into at most <max-shards> groups, each of which
# one `stryker-shard` job mutates on its own runner. Wall-clock is then the slowest shard
# instead of the sum.
#
#   mutation-plan.sh <event> <max-shards> <src>=<test>...
#
# Which files: on `schedule`, every tracked .cs file of the project (the full sweep). On any
# other event, the files that differ from the base Stryker's --since is given: the merge
# commit's first parent on `pull_request`, the merge base with origin/main on a dispatched run
# (the reasons are in mutation.yml). A deleted file has nothing to mutate and is left out.
#
# How they are split: largest file first, each into the shard that is lightest so far, by
# bytes. Bytes stand in for mutants times covering tests, which nothing knows before the run.
# With no more files than shards, that is one file per shard. The split depends only on the
# file list, so a re-run plans the same shards.
#
# A project with no file gets no shard. On `pull_request`, neither does a project whose pass
# is already recorded for this code fingerprint ($HASH; see code-fingerprint.sh): the final
# `stryker` job reports that pass. The lookup asks the cache API for the entry that job saved
# under this PR's merge ref, which is the scope actions/cache restores from.
#
# Writes to $GITHUB_OUTPUT:
#   base      the commit --since diffs against (empty on `schedule`)
#   matrix    {"include":[{"src","test","shard","of","files"}]}; files are relative to
#             src/<src>, separated by spaces
#   any       true when the matrix has a shard (an empty matrix cannot be expanded)
#   projects  {"<src>":{"reused":bool,"shards":n}}, read by the final job
set -euo pipefail

event=$1
max=$2
shift 2

case "$event" in
  pull_request)
    git rev-parse --verify --quiet HEAD^2 > /dev/null || { echo "::error::HEAD is not the PR merge commit"; exit 1; }
    base=$(git rev-parse HEAD^1)
    ;;
  schedule)
    base=
    ;;
  *)
    base=$(git merge-base origin/main HEAD)
    ;;
esac

pass_recorded() {
  local count
  count=$(gh api -X GET "repos/$GITHUB_REPOSITORY/actions/caches" \
    -f key="ci-pass-stryker-$1-$HASH" -f ref="$GITHUB_REF" --jq '.total_count') || {
    echo "::warning::Could not look up an earlier pass for $1; planning it as not yet passed."
    return 1
  }
  [ "$count" -gt 0 ]
}

include='[]'
projects='{}'
for pair in "$@"; do
  src=${pair%%=*}
  test=${pair#*=}
  reused=false
  shards='[]'

  if [ "$event" = pull_request ] && pass_recorded "$src"; then
    reused=true
    echo "::notice::Stryker ($src) already passed on code fingerprint $HASH; this push changed only prose, so it is not re-run."
  else
    if [ -n "$base" ]; then
      files=$(git diff --name-only --diff-filter=d "$base" HEAD -- "src/$src/*.cs")
    else
      files=$(git ls-files -- "src/$src/*.cs")
    fi
    if grep -q '[[:space:]]' <<< "${files//$'\n'/}"; then
      echo "::error::A .cs path under src/$src contains whitespace, which the shard matrix cannot carry."
      exit 1
    fi
    if [ -n "$files" ]; then
      shards=$(
        while IFS= read -r file; do
          printf '%s\t%s\n' "$(wc -c < "$file")" "${file#"src/$src/"}"
        done <<< "$files" |
          LC_ALL=C sort -t $'\t' -k1,1nr -k2,2 |
          awk -F '\t' -v max="$max" '
            { size[NR] = $1; path[NR] = $2 }
            END {
              n = NR < max ? NR : max
              for (i = 1; i <= NR; i++) {
                best = 1
                for (b = 2; b <= n; b++) if (load[b] + 0 < load[best] + 0) best = b
                load[best] += size[i]
                print best "\t" path[i]
              }
            }' |
          jq -Rn --arg src "$src" --arg test "$test" '
            [inputs | split("\t") | { shard: (.[0] | tonumber), file: .[1] }]
            | group_by(.shard) as $groups
            | $groups
            | map({ src: $src, test: $test, shard: .[0].shard, of: ($groups | length),
                    files: (map(.file) | join(" ")) })'
      )
    fi
  fi

  count=$(jq 'length' <<< "$shards")
  echo "$src: reused=$reused, shards=$count"
  jq -r '.[] | "  \(.shard)/\(.of): \(.files)"' <<< "$shards"
  include=$(jq -c --argjson more "$shards" '. + $more' <<< "$include")
  projects=$(jq -c --arg src "$src" --argjson reused "$reused" --argjson shards "$count" \
    '.[$src] = { reused: $reused, shards: $shards }' <<< "$projects")
done

{
  echo "base=$base"
  echo "matrix=$(jq -c '{ include: . }' <<< "$include")"
  echo "any=$(jq 'length > 0' <<< "$include")"
  echo "projects=$projects"
} >> "$GITHUB_OUTPUT"
