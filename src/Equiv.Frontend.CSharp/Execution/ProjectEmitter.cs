using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Emit;

namespace Equiv.Frontend.CSharp.Execution;

/// <summary>
/// Emits a loaded project's <see cref="Compilation"/> for replay (ADR 0035 decision 2; ticket M4-009), with what it needs
/// at run time next to it: each project it references, emitted the same way, and each referenced file outside a reference
/// pack. A reference assembly (<c>ReferenceAssemblyAttribute</c>, as the .NET Framework targeting pack and the .NET
/// reference packs are) cannot run, and the runtime supplies the real one; the facades beside it in its folder only
/// forward types to it, so any folder that holds a reference assembly is a pack and nothing in it is copied.
/// </summary>
internal static class ProjectEmitter
{
    /// <summary>Emits <paramref name="compilation"/> into <paramref name="directory"/>; returns the first error, or null on success.</summary>
    public static string? Emit(Compilation compilation, string directory)
    {
        Directory.CreateDirectory(directory);
        return Emit(compilation, directory, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    private static string? Emit(Compilation compilation, string directory, HashSet<string> done)
    {
        string file = FileName(compilation);
        if (!done.Add(file))
        {
            return null;
        }

        List<(IAssemblySymbol Assembly, MetadataReference? Reference)> references =
            [.. compilation.SourceModule.ReferencedAssemblySymbols.Select(a => (a, compilation.GetMetadataReference(a)))];
        HashSet<string> packs = new(
            references.Where(static r => ReferenceAssemblies.IsReferenceAssembly(r.Assembly)).Select(static r => r.Reference).OfType<PortableExecutableReference>()
                .Select(static r => r.FilePath).OfType<string>().Select(Folder),
            StringComparer.OrdinalIgnoreCase);
        foreach ((IAssemblySymbol _, MetadataReference? reference) in references)
        {
            string? error = reference switch
            {
                CompilationReference project => Emit(project.Compilation, directory, done),
                PortableExecutableReference { FilePath: { } path } when !packs.Contains(Folder(path)) => Copy(path, directory, done),
                _ => null,
            };
            if (error is not null)
            {
                return error;
            }
        }

        EmitResult result = compilation.Emit(Path.Combine(directory, file));
        return result.Success
            ? null
            : $"{compilation.AssemblyName}: {string.Join("; ", result.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error))}";
    }

    /// <summary>
    /// Every project is emitted as <c>&lt;assembly&gt;.dll</c>, an executable one too: both runtimes probe the application
    /// folder for that name first.
    /// </summary>
    private static string FileName(Compilation compilation) => compilation.AssemblyName + ".dll";

    private static string Folder(string path) => Path.GetDirectoryName(path)!;

    private static string? Copy(string path, string directory, HashSet<string> done)
    {
        string name = Path.GetFileName(path);
        if (done.Add(name))
        {
            File.Copy(path, Path.Combine(directory, name), overwrite: true);
        }

        return null;
    }
}
