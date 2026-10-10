using System.Collections.Immutable;

using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Frontend.CSharp.Fingerprinting;
using Equiv.Frontend.CSharp.Lowering;

using Microsoft.CodeAnalysis;

using Xunit;

using static Equiv.Frontend.CSharp.Tests.Lowering.Lowered;

namespace Equiv.Frontend.CSharp.Tests.Lowering;

/// <summary>
/// Ticket P2-114: the <c>System.IO.FileStream::get_Position(</c> row fires in a method that issues an asynchronous operation
/// on a stream, and nowhere else. Each case is checked in the lowering and in the fingerprint, which must agree.
/// </summary>
public sealed class RowPreconditionsTests
{
    private const string Position = "System.IO.FileStream::get_Position(";

    [Theory]
    [InlineData("s.Read(b, 0, 16);")]
    [InlineData("s.Write(b, 0, 16);")]
    [InlineData("new Other().ReadAsync();")]
    [InlineData("new Stream().ReadAsync();")]
    [InlineData("")]
    public void WithoutAnAsyncStreamOperationTheRowDoesNotFire(string statement) =>
        Assert.False(Fires(Body(statement)), statement);

    [Theory]
    [InlineData("s.ReadAsync(b, 0, 16);")]
    [InlineData("s.WriteAsync(b, 0, 16);")]
    [InlineData("s.BeginRead(b, 0, 16, null, null);")]
    [InlineData("s.BeginWrite(b, 0, 16, null, null);")]
    [InlineData("s.CopyToAsync(System.IO.Stream.Null);")]
    [InlineData("s.FlushAsync();")]
    [InlineData("((System.IO.Stream)s).ReadAsync(b, 0, 16);")]
    [InlineData("new Derived().ReadAsync(b, 0, 16);")]
    [InlineData("Action a = () => s.ReadAsync(b, 0, 16);")]
    public void AnAsyncStreamOperationInTheMethodKeepsTheRow(string statement) =>
        Assert.True(Fires(Body(statement)), statement);

    /// <summary>The suppression a method gets for the row adds to the configuration's, and a configured one stays.</summary>
    [Fact]
    public void ASuppressionInTheConfigurationStillApplies()
    {
        const string Source = "class C { static long M(System.IO.FileStream s) { s.FlushAsync(); return s.Position; } }";

        Assert.True(Fires(Source));
        Assert.False(Fires(Source, [Position]));
    }

    [Theory]
    [InlineData("Open().FlushAsync()", true)]
    [InlineData("Open().Length", false)]
    public void AnAsyncOperationInAFieldInitializerCountsForTheConstructor(string initializer, bool fires) =>
        Assert.Equal(fires, Fires($$"""
            class C
            {
                static System.IO.FileStream Open() => null!;
                object o = {{initializer}};
                long p;
                C() { p = Open().Position; }
            }
            """, name: ".ctor"));

    private static string Body(string statement) => $$"""
        using System;
        class Other { public void ReadAsync() { } }
        class Stream { public void ReadAsync() { } }
        class Derived : System.IO.Stream
        {
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => 0;
            public override long Position { get => 0; set { } }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) => 0;
            public override long Seek(long offset, System.IO.SeekOrigin origin) => 0;
            public override void SetLength(long value) { }
            public override void Write(byte[] buffer, int offset, int count) { }
        }
        class C
        {
            static long M(System.IO.FileStream s)
            {
                var b = new byte[16];
                {{statement}}
                return s.Position;
            }
        }
        """;

    /// <summary>Whether the .NET 5 to .NET 6 pair flags the method's <c>Position</c> read, in the lowering and in the fingerprint.</summary>
    private static bool Fires(string source, ImmutableArray<string> suppressed = default, string name = "M")
    {
        SideRuntime runtime = Runtimes.Between("net5.0", "net6.0");
        Compilation compilation = RoslynTestCompilations.Compile(source);
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(static d => d.Severity == DiagnosticSeverity.Error));
        IMethodSymbol method = compilation.GetTypeByMetadataName("C")!.GetMembers(name).OfType<IMethodSymbol>().Single();
        IrProcedure procedure = IrLowerer.Lower(method, compilation, RenameMap.Empty, suppressed.IsDefault ? [] : suppressed, runtime);
        bool lowered = procedure.Blocks.SelectMany(static b => b.Instructions).Any(static i => i is IrCall { Callee.RuntimeChanged: true } or IrPure { RuntimeSensitive: true });
        EquivConfig config = EquivConfig.Default with { SuppressRuntimeChanges = suppressed.IsDefault ? [] : suppressed };
        bool fingerprinted = BodyFingerprinter.Compute(method, compilation, config, [], runtime)!.RuntimeSensitive;

        Assert.Equal(lowered, fingerprinted);
        return lowered;
    }
}
