using System.Collections.Immutable;

namespace Equiv.Frontend.CSharp.Execution;

/// <summary>The files that run in place of a package's compile asset, or why there are none to deploy (ticket P2-240).</summary>
internal sealed record PackageFiles(ImmutableArray<string> Files, string? Error);
