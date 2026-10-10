# samples/

Paired fixtures. Each sample is a folder with `legacy/` (a .NET Framework 4.8 solution,
old-style csproj) and `modern/` (a .NET 10 solution), plus, from M3-003, `expected.sarif.json` (the
snapshot the integration tests assert against) and a `README.md` stating which verdicts
the sample is designed to produce. Ten samples pair other runtimes (ADR 0040, P2-055, P2-113, P1-029, P2-119, P2-143, P2-145, P2-114), with an
SDK-style project on both sides: `same-runtime-cleanup`, `cleanup-modern-syntax`,
`cleanup-extract-method`, `async-disposal`, `webapi-inherited-route` and `extern-import` (.NET 10 on both), `version-bump` and `params-span-overloads` (.NET 8 against .NET 10) and `runtime-row-framework-only-change`
(.NET 8 against .NET 9) and `runtime-row-sync-position` (.NET 5 against .NET 6). The two
`cleanup-*` samples (P2-049) record what `equiv` says today about behaviour-preserving cleanups, so
some of their expected verdicts are known gaps, each with an owning ticket in the sample's README.

Samples are deliberately tiny: a handful of methods each, one concept per sample.
They are the executable specification of the tool. The initial set is defined in
ticket M1-001 and grows with every frontend feature.
