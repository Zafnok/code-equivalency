# extern-import

An `extern` method is a procedure (ADR 0054, ticket P2-145). Both sides target .NET 10, so the pair
crosses no runtime (ADR 0040).

- `Ticks` is a `[DllImport]` method, the same on both sides. It has no body to lower, so both sides
  are one `no-body` opaque. On one runtime it has the fingerprint of its signature, of what it
  imports and of its attributes, so the pair is congruent.
- `F` imports from `a.dll` on the legacy side and from `b.dll` on the modern side. The fingerprints
  differ, and `equiv` does not model a native function, so the pair is Unknown. It is never
  Divergent: there is no input to show.
- `M` calls `F` and is the same on both sides, so it is congruent. Its result names `F` among the
  callees it assumed and among those not proved (ADR 0019).

Before ADR 0054 `F` and `Ticks` were not procedures: the run reported `M` alone, Equivalent, with
nothing assumed.

## Expected verdicts

| Procedure | Verdict | `proofMethod` | `unknownReason` |
|---|---|---|---|
| `Native.Ticks()` | Equivalent | `congruence` | |
| `Native.F()` | Unknown | | `opaque` (`no-body`) |
| `Native.M()` | Equivalent | `congruence` | |

Exit code: 0 (Unknown does not fail the run without `--fail-on unknown`).
