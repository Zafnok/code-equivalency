using Equiv.Core.Execution;
using Equiv.Execute;
using Equiv.Frontend.CSharp.Execution;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// Ticket P2-040: a driver started by a host bound to <c>--execute</c>'s temporary folder runs in a fresh folder under it, so
/// a relative write by the code under test lands there and not in the caller's working directory. The stub is a .NET 10
/// console assembly that writes a file by a relative name and then answers every case.
/// </summary>
public sealed class DriverWorkingDirectoryTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("driver-cwd-").FullName;

    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Fact]
    public void ARelativeWrite_LandsUnderTheTemporaryFolder_NotInTheCallersDirectory()
    {
        string name = $"relative-{Guid.NewGuid():N}.txt";
        string stub = Emit(name);
        string root = Directory.CreateDirectory(Path.Combine(directory, "execute")).FullName;
        string caller = Environment.CurrentDirectory;
        string[] before = Entries(caller);

        ExecutionOutcome outcome;
        using (DriverStream stream = new(new ChildProcessHost(ChildProcessHost.DefaultMemoryLimitBytes).Within(root), stub, RuntimeDiff.CaseTimeout))
        {
            outcome = stream.Run(new ExecutionInput([]), "invariant");
        }

        Assert.Equal(OutcomeKind.Returned, outcome.Kind);
        Assert.Equal(before, Entries(caller));
        Assert.False(File.Exists(Path.Combine(caller, name)));
        string written = Assert.Single(Directory.GetFiles(root, name, SearchOption.AllDirectories));
        Assert.StartsWith("cwd-", Path.GetFileName(Path.GetDirectoryName(written)), StringComparison.Ordinal);
    }

    private static string[] Entries(string folder) => [.. Directory.EnumerateFileSystemEntries(folder).Order(StringComparer.Ordinal)];

    /// <summary>Compiles a stub whose <c>Main</c> writes <paramref name="relative"/> and then answers every case line.</summary>
    private string Emit(string relative)
    {
        string folder = Directory.CreateDirectory(Path.Combine(directory, "driver")).FullName;
        string source = $$"""
            internal static class Program
            {
                private static void Main()
                {
                    System.IO.File.WriteAllText("{{relative}}", "written");
                    while (System.Console.In.ReadLine() != null)
                    {
                        System.Console.Out.WriteLine("[\"Returned\",1]");
                        System.Console.Out.Flush();
                    }
                }
            }
            """;
        CSharpCompilation compilation = CSharpCompilation.Create(
            "RelativeWriter",
            [CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken)],
            [.. DriverReferences.Installed().Modern.Select(static r => MetadataReference.CreateFromFile(r))],
            new CSharpCompilationOptions(OutputKind.ConsoleApplication));
        string path = Path.Combine(folder, "RelativeWriter.dll");
        EmitResult result;
        using (FileStream image = File.Create(path))
        {
            result = compilation.Emit(image, cancellationToken: TestContext.Current.CancellationToken);
        }

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        File.WriteAllText(Path.ChangeExtension(path, ".runtimeconfig.json"), DriverFactory.RuntimeConfig);
        return path;
    }
}
