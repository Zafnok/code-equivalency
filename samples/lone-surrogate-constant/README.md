# lone-surrogate-constant

A string constant that is not well-formed UTF-16 (ticket P2-116). `CharSets.Find(string)` is the same
file on both sides and searches for the two code units of `"\uD800\uDBFF"`: two high surrogates, so
neither has a low surrogate to pair with. The regex source generator writes character sets this way
when a set covers a surrogate range.

The bound fingerprint (ADR 0024) writes a string constant as JSON text, and JSON text cannot be made
from a lone surrogate. Before P2-116 that threw, the pair failed to lower with the notification
"Cannot transcode invalid UTF-16 string to UTF-8 JSON text", and the run exited 5. Now each lone
surrogate is written as its own `\uXXXX` escape, so the two sides have the same fingerprint and the
pair is Equivalent by congruence, with no notification.

## Expected verdicts

| Procedure | Verdict |
|---|---|
| `CharSets.Find(string)` | Equivalent (by congruence) |

Exit code: 0 (no Divergent or Unknown result).
