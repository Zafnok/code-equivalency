# P2-272: `Math.Sin`, `Math.Cos` and `Math.Tan` on .NET Framework 4.8 and .NET 5 to 10 (2026-10-20)

One x64 console program, outside equiv, built for each runtime from one source. It evaluates every member below on a
fixed argument set and writes each result's bits. The results are then compared bit for bit between every pair of
runtimes.

## Outcome in one line

**On ordinary arguments, `Math.Sin`, `Math.Cos` and `Math.Tan` differ between .NET Framework 4.8 and every .NET from 5
to 10 (3 to 4 in 100 angles in [-2pi, 2pi], nearly all past 2^19). .NET 5, 6, 8, 9 and 10 give the same bits for every
argument and every member, `MathF` included. `Math.Sin` and `Math.Cos` already had rows that fire on every call across
the Framework boundary, so their rows change only their reason. `Math.Tan` had none and gains one. No pair's result
moves.**

## Runtimes

| Short name | Runtime | How it ran | Core library and CRT the process loaded |
|---|---|---|---|
| net48 | .NET Framework 4.8.10008.0 x64 | `.exe`, `PlatformTarget` x64 | `Framework64\v4.0.30319\clr.dll` 4.8.10008.0 |
| net5.0 | .NET 5.0.17 x64 | shared framework, `rollForward: LatestPatch` | `coreclr.dll` 5.0.1722.21314 |
| net6.0 | .NET 6.0.36 x64 | shared framework, `rollForward: LatestPatch` | `coreclr.dll` 6.0.3624.51421 |
| net8.0 | .NET 8.0.31 x64 | self-contained, runtime pack from NuGet | `coreclr.dll` 8.0.3126.42015 |
| net9.0 | .NET 9.0.20 x64 | self-contained, runtime pack from NuGet | `coreclr.dll` 9.0.2026.41315 |
| net10.0 | .NET 10.0.12 x64 | shared framework, `rollForward: LatestPatch` | `coreclr.dll` 10.0.1226.42308 |

Every process loaded the same system `C:\Windows\System32\ucrtbase.dll` 10.0.26100.9711 (Windows 11 build 26300). The
.NET 5 and 6 shared frameworks ship an app-local `ucrtbase.dll`, but Windows 10 and later always load the system one,
and the module list confirms that. .NET 8 and 9 are not installed on this host, so they ran self-contained from their
runtime packs, which ship no CRT.

## Arguments

The arguments come from SplitMix64 with seed `0x272_2026_1020`. It uses only integer arithmetic, so every runtime draws
the same doubles.

| Set | Arguments |
|---|---|
| `angle2pi` | 20,000 uniform in [-2pi, 2pi] |
| `pm1000` | 20,000 uniform in [-1000, 1000] |
| `log2m30to63` | 20,000 with a magnitude log-uniform in [2^-30, 2^63) and a random sign |
| `special` | 272: k·pi/4 and k for k in -64..64, ±0, ±epsilon, 1e-300, 1e-8, NaN, ±infinity, ±2^63, 1e19, 1e300, `double.MaxValue` |

The members:
- `Math.Sin`, `Math.Cos` and `Math.Tan` of each double `x`.
- `(float)Math.F((float)x)`: the float form, as a body that holds angles in `float` writes it on every runtime.
- `MathF.F((float)x)`. .NET Framework 4.8 has no `MathF`, so against net48 it is compared with that runtime's
  `(float)Math.F((float)x)`, the nearest call there.

## .NET Framework 4.8 against each .NET

The shares are the same against net5.0, net6.0, net8.0, net9.0 and net10.0, argument for argument, because those five
runtimes agree bit for bit (next section). A "max ulps" column measures the largest distance in units in the last place.

| Member | `angle2pi` | `pm1000` | `log2m30to63` | `special` |
|---|---|---|---|---|
| `Math.Sin(double)` | 635 / 20,000 = 3.18% (max 1 ulp) | 897 = 4.49% (max 47) | 10,166 = 50.83% | 49 / 272 = 18.01% |
| `Math.Cos(double)` | 648 = 3.24% (max 2) | 888 = 4.44% (max 169) | 10,167 = 50.84% | 45 / 272 = 16.54% |
| `Math.Tan(double)` | 876 = 4.38% (max 2) | 1,392 = 6.96% (max 303) | 10,598 = 52.99% | 79 / 272 = 29.04% |
| `(float)Math.Sin(float)` | 0 | 0 | 3,935 = 19.68% | 3 / 272 = 1.10% |
| `(float)Math.Cos(float)` | 0 | 0 | 3,906 = 19.53% | 3 / 272 = 1.10% |
| `(float)Math.Tan(float)` | 0 | 0 | 4,346 = 21.73% | 3 / 272 = 1.10% |
| `MathF.Sin(float)` against net48's `(float)Math.Sin(float)` | 28 = 0.14% (max 1) | 20 = 0.10% (max 1) | 3,945 = 19.73% | 3 / 272 = 1.10% |
| `MathF.Cos(float)` against net48's `(float)Math.Cos(float)` | 18 = 0.09% (max 1) | 24 = 0.12% (max 1) | 3,913 = 19.57% | 5 / 272 = 1.84% |
| `MathF.Tan(float)` against net48's `(float)Math.Tan(float)` | 5 = 0.03% (max 1) | 2 = 0.01% (max 1) | 4,347 = 21.74% | 3 / 272 = 1.10% |

Where the differences sit, by the argument's binary exponent, for the double members:
- **Below 2^7 (|x| < 128):** 1 to 7 in 100 results differ, almost all by one ulp. Near a zero of the function the gap is
  many ulps, because the Framework reduces by a 66-bit pi, the x87 `fsin` signature. For example, `Math.Sin(Math.PI)` is
  `0x3CA1A60000000000` (1.22460635e-16) on net48 and `0x3CA1A62633145C07` (1.22464680e-16) on every .NET. `Math.Tan`
  of `Math.PI / 2` is `0x434D02D546D3EAB2` against `0x434D02967C31CDB5`.
- **2^7 to 2^18:** the share grows from 3% to about 80%. Away from zeros the gap grows from a couple of ulps to tens or
  hundreds at 2^17, and up to about 1,250 at 2^18.
- **From about 2^19:** more than 9 in 10 results differ, and every one from 2^25, by more ulps the larger the argument.
  Beyond 2^63 the Framework returns the argument unchanged, which the existing rows' witnesses show.

Rounded to `float`, the float forms agree on every argument below 2^31, so the ordinary-angle sets show none. The first
differences are at 2^31 (`Cos`, one argument), 2^34 (`Tan`) and 2^38 (`Sin`). Most results differ from 2^46 and nearly
all from 2^48.

## Between .NET versions

Every pair among net5.0, net6.0, net8.0, net9.0 and net10.0 differs on **0** of the 60,272 arguments, for all nine
members, `MathF` included. The five output files are byte-identical once their two header lines are removed.

## What changes

- **Already covered.** `System.Math::Sin(double)` and `System.Math::Cos(double)` already had rows with `changedIn`
  `netcoreapp1.0` and no `precondition` or `requires`. They already fired on every call on a pair that crosses the
  Framework boundary, so an unedited body calling them on ordinary angles was never congruent there. The ticket's premise
  that such a body is congruent today does not hold for these two. Only the rows' reason widens, from "beyond 2^63"
  to what this file measures; the member, `changedIn` and witness stay the same.
- **New row.** `System.Math::Tan(double)` had no row and gains one: `measured`, `changedIn` `netcoreapp1.0`, witnessed
  by `0xBFE66E8086EE4788` (-0.70 rad), which gives `0xBFEB01E355B9AB1B` on net48 and `0xBFEB01E355B9AB1C` on .NET.
  `BodyFingerprinterTests.ATrigonometricCallIsRuntimeSensitiveAcrossTheFrameworkBoundaryOnly` shows an unedited
  `Math.Tan` call is runtime-sensitive on net48 to net5.0, and not on net5.0 to net9.0.
- **No row.** No row is needed for `MathF`, or for any of the three members between two .NET versions: the bits never
  differ there. A row with `changedIn` at a .NET version would make congruence fail on version upgrades for nothing.

## Pairs

No pair's result moves, so no soundness ticket is filed:
- **The new `Math.Tan` row** applies only on a pair that crosses the Framework boundary: `gitextensions-8522`,
  `duplicati-3124` and `openra-17989`. A shallow fetch of each one's legacy commit
  (`3f4ed219`, `beaf0356`, `fef7a018`) shows no `Math.Tan` or `MathF.Tan` in any `.cs` file.
- **The Sin and Cos rows** fired on those pairs before and still do. OpenRA's `float2::FromAngle(float)` stays EQ007, as
  P2-247 confirmed.
- **The .NET-to-.NET pairs** (`jellyfin-13023`, `gitextensions-9860` and the cleanup pairs) cross no change.

## Correction to P2-247

P2-247's OpenRA exposure sub-agent reported that about half of 20,000 random angles differ in the low bits after rounding
to `float` (`docs/runs/2026-10-20-precision-execute-verdict-2.md`). It compared Windows PowerShell 5.1 with pwsh. This
measurement finds no such difference: `(float)Math.Sin(float)` agrees on all 40,000 ordinary angles, and its differences
start only at 2^38.

In double precision the low-bit differences are real, at 3 in 100. The note on OpenRA's `ScreenShaker::GetScrollOffset()`
(U11) computes in double, so it stands. The P2-247 row for `FromAngle` was a very large angle, which this file agrees
with.
