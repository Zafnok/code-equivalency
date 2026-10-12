#!/usr/bin/env bash
# Plans mutation.yml's Stryker shards (P2-133, P2-160). For every project it lists the
# src/<project> .cs files this run mutates and splits them into at most <max-shards> groups,
# each of which one `stryker-shard` job mutates on its own runner. Wall-clock is then the
# slowest shard instead of the sum.
#
#   mutation-plan.sh <event> <max-shards> <src>=<test>[:<bytes-per-shard>]...
#
# Which files: on `schedule`, every tracked .cs file of the project (the full sweep). On any
# other event, the files that differ from the base Stryker's --since is given: the merge
# commit's first parent on `pull_request`, the merge base with origin/main on a dispatched run
# (the reasons are in mutation.yml). A deleted file has nothing to mutate and is left out.
#
# How many shards: the files' bytes over <bytes-per-shard> (48000 when the project names
# none), rounded up, and at most <max-shards>. Bytes stand in for mutants times covering
# tests, which nothing knows before the run, and a project names its own figure because a
# byte of one project costs many times a byte of another: 30 kB of Equiv.Verify.Z3 is about
# 130 mutants and 40 to 110 minutes, 184 kB of Equiv.Frontend.CSharp about 720 and 58.
#
# How they are split: a file larger than a shard's share (the bytes over the shard count) is
# cut into equal pieces no larger than it, so one changed file no longer means one shard
# however long it takes. Then largest piece first, each into the shard that is lightest so
# far. The split depends only on the file list, so a re-run plans the same shards.
#
# What a shard is given: one --mutate pattern per file it holds any of, and for a file it
# holds only pieces of, one exclusion `!<file>{from..to}` (Stryker's character spans) per run
# of pieces that are other shards'. Stryker drops a mutant only when an exclusion contains
# its whole span, and an exclusion stops one character short of the shard's own pieces. So a
# mutant that starts in a piece is tested by the shard holding that piece, whatever the cut
# runs through, and one that crosses a cut is tested by both sides; mutation-score.sh counts
# it once. Nothing rests on where a cut falls: the offsets are bytes where Stryker counts
# characters, which only moves the cuts.
#
# A project with no file gets no shard. On `pull_request`, neither does a project whose pass
# is already recorded for this code fingerprint ($HASH; see code-fingerprint.sh): the final
# `stryker` job reports that pass. The lookup asks the cache API for the entry that job saved
# under this PR's merge ref, which is the scope actions/cache restores from.
#
# Writes to $GITHUB_OUTPUT:
#   base      the commit --since diffs against (empty on `schedule`)
#   matrix    {"include":[{"src","test","shard","of","mutate"}]}; mutate is the shard's
#             patterns, relative to src/<src>, separated by spaces
#   any       true when the matrix has a shard (an empty matrix cannot be expanded)
#   projects  {"<src>":{"reused":bool,"shards":n}}, read by the final job
set -euo pipefail

event=$1
max=$2
shift 2

case "$event" in
  pull_request)
    git rev-parse --verify --quiet HEAD^2 > /dev/null || { echo "::error::HEAD is not the PR merge commit" >&2; exit 1; }
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
  local project=$1
  local count
  count=$(gh api -X GET "repos/$GITHUB_REPOSITORY/actions/caches" \
    -f key="ci-pass-stryker-$project-$HASH" -f ref="$GITHUB_REF" --jq '.total_count') || {
    echo "::warning::Could not look up an earlier pass for $project; planning it as not yet passed."
    return 1
  }
  [[ "$count" -gt 0 ]]
}

# Reads "<bytes>\t<path>" lines and writes one "<shard>\t<pattern>" line per --mutate pattern.
split_into_shards() {
  local target=$1
  LC_ALL=C sort -t $'\t' -k1,1nr -k2,2 |
    awk -F '\t' -v max="$max" -v target="$target" '
      { size[NR] = $1; path[NR] = $2; total += $1 }
      END {
        n = int((total + target - 1) / target)
        if (n > max) n = max
        if (n < 1) n = 1
        share = int((total + n - 1) / n)
        if (share < 1) share = 1
        for (i = 1; i <= NR; i++) {
          pieces = int((size[i] + share - 1) / share)
          if (pieces < 1) pieces = 1
          for (p = 1; p <= pieces; p++)
            printf "%d\t%s\t%d\t%d\t%d\t%d\n", size[i] / pieces, path[i], p, pieces, size[i], n
        }
      }' |
    LC_ALL=C sort -t $'\t' -k1,1nr -k2,2 -k3,3n |
    awk -F '\t' '
      {
        file = $2; pieces[file] = $4; size[file] = $5; n = $6
        best = 1
        for (b = 2; b <= n; b++) if (load[b] + 0 < load[best] + 0) best = b
        load[best] += $1
        if (best > last) last = best
        if (!((best, file) in held)) { held[best, file] = 1; files[best, ++count[best]] = file }
        own[best, file, $3] = 1
      }
      END {
        for (s = 1; s <= last; s++) for (j = 1; j <= count[s]; j++) {
          file = files[s, j]; k = pieces[file]
          print s "\t" file
          for (p = 1; p <= k; p++) {
            if ((s, file, p) in own) continue
            q = p
            while (q < k && !((s, file, q + 1) in own)) q++
            from = p == 1 ? 0 : int((p - 1) * size[file] / k) + 1
            to = q == k ? "2147483647" : int(q * size[file] / k) - 1
            if (q == k || to > from) print s "\t!" file "{" from ".." to "}"
            p = q
          }
        }
      }'
}

include='[]'
projects='{}'
for pair in "$@"; do
  src=${pair%%=*}
  test=${pair#*=}
  target=48000
  if [[ "$test" = *:* ]]; then
    target=${test##*:}
    test=${test%:*}
  fi
  reused=false
  shards='[]'

  if [[ "$event" = pull_request ]] && pass_recorded "$src"; then
    reused=true
    echo "::notice::Stryker ($src) already passed on code fingerprint $HASH; this push changed only prose, so it is not re-run."
  else
    if [[ -n "$base" ]]; then
      files=$(git diff --name-only --diff-filter=d "$base" HEAD -- "src/$src/*.cs")
    else
      files=$(git ls-files -- "src/$src/*.cs")
    fi
    if grep -q '[[:space:]]' <<< "${files//$'\n'/}"; then
      echo "::error::A .cs path under src/$src contains whitespace, which the shard matrix cannot carry." >&2
      exit 1
    fi
    if [[ -n "$files" ]]; then
      shards=$(
        while IFS= read -r file; do
          printf '%s\t%s\n' "$(wc -c < "$file")" "${file#"src/$src/"}"
        done <<< "$files" |
          split_into_shards "$target" |
          jq -Rn --arg src "$src" --arg test "$test" '
            [inputs | split("\t") | { shard: (.[0] | tonumber), pattern: .[1] }]
            | group_by(.shard) as $groups
            | $groups
            | map({ src: $src, test: $test, shard: .[0].shard, of: ($groups | length),
                    mutate: (map(.pattern) | join(" ")) })'
      )
    fi
  fi

  count=$(jq 'length' <<< "$shards")
  echo "$src: reused=$reused, shards=$count"
  jq -r '.[] | "  \(.shard)/\(.of): \(.mutate)"' <<< "$shards"
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
