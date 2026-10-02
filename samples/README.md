# samples/

Paired fixtures. Each sample is a folder with `legacy/` (a .NET Framework 4.8 solution,
old-style csproj) and `modern/` (a .NET 10 solution), plus, from M3-003, `expected.sarif.json` (the
snapshot the integration tests assert against) and a `README.md` stating which verdicts
the sample is designed to produce. Four samples pair other runtimes (ADR 0040, P2-055), with an
SDK-style project on both sides: `same-runtime-cleanup`, `cleanup-modern-syntax` and
`cleanup-extract-method` (.NET 10 on both) and `version-bump` (.NET 8 against .NET 10). The two
`cleanup-*` samples (P2-049) record what `equiv` says today about behaviour-preserving cleanups, so
some of their expected verdicts are known gaps, each with an owning ticket in the sample's README.

Samples are deliberately tiny: a handful of methods each, one concept per sample.
They are the executable specification of the tool. The initial set is defined in
ticket M1-001 and grows with every frontend feature.
