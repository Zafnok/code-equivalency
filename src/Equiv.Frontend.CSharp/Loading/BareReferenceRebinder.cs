using Microsoft.CodeAnalysis;

namespace Equiv.Frontend.CSharp.Loading;

/// <summary>
/// Points SDK-style compilations at the bare loader's compilations of the non-SDK projects they reference (M3-029).
/// MSBuildWorkspace opens such a reference through its netcore build host, which cannot evaluate a non-SDK project the
/// way Windows does, so its copy is replaced (<see cref="Compilation.ReplaceReference"/>), by assembly name, whether it
/// arrived as a compilation reference or as a reference to the built file. A compilation that only reaches a bare
/// project through another compilation is rebuilt against the rebound one.
/// </summary>
internal sealed class BareReferenceRebinder(IReadOnlyDictionary<string, Compilation> bareByAssemblyName)
{
    private readonly Dictionary<Compilation, Compilation> _rebound = new(ReferenceEqualityComparer.Instance);

    /// <summary><paramref name="compilation"/> bound against the bare compilations; the same instance when nothing changed.</summary>
    public Compilation Rebind(Compilation compilation)
    {
        if (_rebound.TryGetValue(compilation, out Compilation? done))
        {
            return done;
        }

        Compilation result = compilation;
        foreach (MetadataReference reference in compilation.References)
        {
            if (Replacement(reference) is { } replacement)
            {
                result = result.ReplaceReference(reference, replacement);
            }
        }

        _rebound[compilation] = result;
        return result;
    }

    private CompilationReference? Replacement(MetadataReference reference)
    {
        Compilation? target = reference switch
        {
            CompilationReference { Compilation.AssemblyName: { } name } project when bareByAssemblyName.TryGetValue(name, out Compilation? bare) => bare == project.Compilation ? null : bare,
            CompilationReference project => Rebind(project.Compilation) is { } inner && inner != project.Compilation ? inner : null,
            PortableExecutableReference { FilePath: { } path } when bareByAssemblyName.TryGetValue(Path.GetFileNameWithoutExtension(path), out Compilation? bare) => bare,
            _ => null,
        };
        return target?.ToMetadataReference(reference.Properties.Aliases, reference.Properties.EmbedInteropTypes);
    }
}
