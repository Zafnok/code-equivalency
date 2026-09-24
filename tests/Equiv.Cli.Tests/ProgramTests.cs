using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Matching;
using Equiv.Core.Verdicts;

using Xunit;

namespace Equiv.Cli.Tests;

/// <summary>
/// <see cref="Program.Main"/> end to end. Redirects the console, so it shares the "Console"
/// collection with <see cref="CompareCommandTests"/> and never runs in parallel with it.
/// </summary>
[Collection("Console")]
public sealed class ProgramTests
{
    [Fact]
    public void Main_WithNoArgsExits3()
    {
        int exitCode = ExitCodes.Success;

        string errorOutput = CaptureStdErr(() => exitCode = Program.Main([]));

        Assert.Equal(ExitCodes.UsageError, exitCode);
        Assert.False(string.IsNullOrWhiteSpace(errorOutput));
    }

    [Fact]
    public void Main_WithValidArgsButUnsupportedExtensionExits3()
    {
        using TempFile legacy = new();
        using TempFile modern = new();

        // TempFile.GetTempFileName() has a .tmp extension, which CSharpFrontend.Supports rejects
        // (it only supports .sln/.slnx), so a well-formed parse still ends in a router rejection.
        // This also exercises the parseResult.Invoke() branch that Main_WithNoArgsExits3's
        // parse-error branch does not reach.
        int exitCode = Program.Main(["compare", "--legacy", legacy.Path, "--modern", modern.Path]);

        Assert.Equal(ExitCodes.UsageError, exitCode);
    }

    /// <summary>
    /// Ticket M3-013 acceptance criterion 6 (ADR 0023): any exception that escapes the command, here the
    /// missing-body <see cref="InvalidOperationException"/> a frontend bug produces, is exit 5 with the message on
    /// stderr, not System.CommandLine's own exit 1 for an unhandled exception.
    /// </summary>
    [Fact]
    public void Main_UnhandledException_Exits5()
    {
        using TempFile legacy = new();
        using TempFile modern = new();
        ProcedureIdentity identity = new("T::Pair()");
        IrProcedure body = IrText.Parse($"proc \"{identity.Value}\" () entry B0 B0: ret");
        ProcedurePair pair = new(identity, identity, body, NewBody: null);
        FakeFrontend frontend = new("csharp", _ => true, new MatchResult([pair], [], [], []));
        int exitCode = ExitCodes.Success;

        string errorOutput = CaptureStdErr(() => exitCode = Program.Run(
            ["compare", "--legacy", legacy.Path, "--modern", modern.Path],
            [frontend],
            new FakeBackend(new Dictionary<string, Verdict>(StringComparer.Ordinal))));

        Assert.Equal(ExitCodes.InternalError, exitCode);
        Assert.Contains(identity.Value, errorOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_HelpShowsDescriptionAndExits0()
    {
        int exitCode = ExitCodes.UsageError;
        TextWriter original = Console.Out;
        using StringWriter writer = new();
        Console.SetOut(writer);
        try
        {
            exitCode = Program.Main(["--help"]);
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Contains("Compares two versions of a codebase for behavioural equivalence.", writer.ToString(), StringComparison.Ordinal);
    }

    private static string CaptureStdErr(Action action)
    {
        TextWriter original = Console.Error;
        using StringWriter writer = new();
        Console.SetError(writer);
        try
        {
            action();
        }
        finally
        {
            Console.SetError(original);
        }

        return writer.ToString();
    }
}
