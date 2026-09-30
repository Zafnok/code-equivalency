using System.Collections.Immutable;
using System.Linq;

using Equiv.Core;

namespace Equiv.Frontend.CSharp.Loading;

/// <summary>
/// One loaded project's runtime (ADR 0040 decision 1; ticket P2-053). <see cref="Runtimes"/> is empty only for an
/// <see cref="RuntimeDetection.Unhosted"/> project, whose <see cref="Runtime"/> is then its own target framework.
/// </summary>
internal sealed record ProjectRuntime(string Project, ImmutableArray<TargetRuntime> Runtimes, string Declared, string Source)
{
    /// <summary>The runtime as SARIF reports it: the short names of <see cref="Runtimes"/> in runtime order, else <see cref="Declared"/>.</summary>
    public string Runtime => Runtimes.IsEmpty ? Declared : string.Join(", ", Runtimes.Select(static r => r.ToString()));
}
