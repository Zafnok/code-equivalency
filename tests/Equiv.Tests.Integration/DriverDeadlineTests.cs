using System.Diagnostics;
using System.Globalization;

using Equiv.Core.Execution;
using Equiv.Execute;
using Equiv.Frontend.CSharp.Execution;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// Ticket P2-039: every wait on a real driver process has a deadline. Each stub is a .NET 10 console assembly that writes
/// its process id next to itself and then hangs, one without ever reading stdin and one reading every line but never
/// answering. The case line is far bigger than a pipe's buffer, so a write that is not under the deadline blocks for good.
/// </summary>
public sealed class DriverDeadlineTests : IDisposable
{
    private static readonly TimeSpan CaseTimeout = RuntimeDiff.CaseTimeout;

    /// <summary>Generous against a slow runner, and far short of forever.</summary>
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);

    private static readonly ExecutionInput Big = new([$"\"{new string('a', 1 << 20)}\""]);

    private readonly string directory = Directory.CreateTempSubdirectory("driver-deadline-").FullName;

    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Fact]
    public void ADriverThatNeverReadsStdin_TimesOutAndIsKilled() =>
        AssertTimesOut("NeverReads", "System.Threading.Thread.Sleep(System.Threading.Timeout.Infinite);");

    [Fact]
    public void ADriverThatNeverWritesStdout_TimesOutAndIsKilled() =>
        AssertTimesOut("NeverWrites", "while (System.Console.In.ReadLine() != null) { }\n        System.Threading.Thread.Sleep(System.Threading.Timeout.Infinite);");

    private void AssertTimesOut(string name, string body)
    {
        string stub = Emit(name, body);
        Stopwatch clock = Stopwatch.StartNew();
        ExecutionOutcome outcome;
        int pid;
        using (DriverStream stream = new(new ChildProcessHost(ChildProcessHost.DefaultMemoryLimitBytes), stub, CaseTimeout))
        {
            outcome = stream.Run(Big, "invariant");
            pid = int.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(stub)!, "pid")), CultureInfo.InvariantCulture);
        }

        Assert.True(clock.Elapsed < Bound, $"{name} took {clock.Elapsed}");
        Assert.Equal(OutcomeKind.NotComparable, outcome.Kind);
        Assert.Equal(OutcomeLine.TimedOut(CaseTimeout), outcome.Canonical);
        Assert.False(Alive(pid), string.Create(CultureInfo.InvariantCulture, $"{name} ({pid}) is still running"));
    }

    private static bool Alive(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            return !process.WaitForExit(TimeSpan.FromSeconds(10));
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Compiles a stub whose <c>Main</c> records its process id and then runs <paramref name="body"/>.</summary>
    private string Emit(string name, string body)
    {
        string folder = Directory.CreateDirectory(Path.Combine(directory, name)).FullName;
        string source = $$"""
            internal static class Program
            {
                private static void Main()
                {
                    System.IO.File.WriteAllText(System.IO.Path.Combine(System.AppContext.BaseDirectory, "pid"), System.Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    {{body}}
                }
            }
            """;
        CSharpCompilation compilation = CSharpCompilation.Create(
            name,
            [CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken)],
            [.. DriverReferences.Installed().Modern.Select(static r => MetadataReference.CreateFromFile(r))],
            new CSharpCompilationOptions(OutputKind.ConsoleApplication));
        string path = Path.Combine(folder, name + ".dll");
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
