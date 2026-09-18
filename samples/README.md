# samples/

Paired fixtures. Each sample is a folder with `legacy/` (a .NET Framework 4.8 solution,
old-style csproj) and `modern/` (a .NET 10 solution), plus `expected.sarif.json` (the
snapshot the integration tests assert against) and a `README.md` stating which verdicts
the sample is designed to produce.

Samples are deliberately tiny: a handful of methods each, one concept per sample.
They are the executable specification of the tool. The initial set is defined in
ticket M1-001 and grows with every frontend feature.
