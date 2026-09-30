using Equiv.Core;
using Equiv.Core.Execution;
using Equiv.Frontend.CSharp.Execution;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Execution;

/// <summary>
/// A <see cref="DriverFactory"/> over the test host's runtime assemblies plus a small library, <c>Odd</c>, built once into
/// the test output folder in a legacy and a modern version: the legacy one alone has <c>Members.OnlyLegacy()</c>, the
/// modern one alone <c>Small.C</c> and <c>Members.OnlyModern()</c>. A stand-in <c>System.Windows.Forms</c> (ticket
/// P2-051) is a legacy reference and the modern side's Windows Desktop pack, so a driver that uses it runs on
/// <c>Microsoft.WindowsDesktop.App</c>; <c>System.Drawing</c>'s value types are the test host's own.
/// </summary>
internal static class DriverLibraries
{
    public const string AllKinds = "Odd.Members::AllKinds(bool,char,sbyte,byte,short,ushort,int,uint,long,ulong,float,double,decimal,string,object,Odd.Small,Odd.Signed)";

    private const string Odd = """
        namespace Odd
        {
            public enum Small : byte { A = 1, B = 2, /*MODERN*/ }
            public enum Signed : long { Minus = -1, Zero = 0 }
            public enum Wide16 : ushort { A }
            public enum Wide32 : uint { A }
            public enum Wide64 : ulong { A }

            public static class Members
            {
                public static string AllKinds(bool b, char c, sbyte s8, byte u8, short s16, ushort u16, int s32, uint u32, long s64, ulong u64, float f, double d, decimal m, string s, object o, Small small, Signed signed) => s;
                public static bool B() => true;
                public static sbyte S8() => -1;
                public static byte U8() => 1;
                public static short S16() => -1;
                public static ushort U16() => 1;
                public static int S32() => -1;
                public static uint U32() => 1;
                public static long S64() => -1;
                public static ulong U64() => 1;
                public static char C() => 'c';
                public static float F() => 1f;
                public static double D() => 1d;
                public static decimal M() => 1m;
                public static Small Echo(Small s) => s;
                public static Signed Flip(Signed s) => s;
                public static System.Collections.Generic.List<int> Ints() => new System.Collections.Generic.List<int> { 1 };
                public static int[][] Jagged() => new[] { new[] { 1 } };
                public static int[,] Grid() => new int[1, 1];
                public static System.Collections.Generic.List<object> Objects() => null;
                public static int? Maybe() => null;
                public static System.DateTime When() => default;
                public static void Nothing() { }
                public static int Ref(ref int x) => x;
                public static int In(in int x) => x;
                public static int Out(out int x) { x = 0; return 0; }
                public static int Vararg(int a, __arglist) => a;
                public static unsafe int* Pointer() => null;
                public static unsafe delegate*<void> FunctionPointer() => null;
                public static T Generic<T>(T x) => x;
                [System.Obsolete("gone", true)] public static int Gone() => 0;
                internal static int Internal() => 0;
                public static int Widths(Wide16 a, Wide32 b, Wide64 c) => 0;
                /*ONLYMODERN*/
            }

            public abstract class Base { public Base() { } }

            public class Holder
            {
                public int this[int i] => i;
                public int Size => 2;
                public static int Count => 1;
                public int Settable { set { } }
                public static Holder operator +(Holder a, Holder b) => a;
            }

            public class Generic<T> { public static int Y() => 1; }
            public class Outer { public class Inner { public static int X() => 1; } }
            internal class Hidden { public class Inner { public static int X() => 1; } }
        }

        public class Global { public class Nested { public static int Z() => 1; } }
        """;

    private const string Forms = """
        namespace System.Windows.Forms
        {
            public interface IWin32Window { System.IntPtr Handle { get; } }
            public struct Message { public int Msg; }
            public class Control : IWin32Window
            {
                public System.IntPtr Handle => System.IntPtr.Zero;
                public string Text => string.Empty;
            }

            public static class TextRenderer
            {
                public static System.Drawing.Size MeasureText(string text) => new System.Drawing.Size(text.Length, 1);
                public static int Draw(IWin32Window owner) => 0;
                public static int Dispatch(Message message) => message.Msg;
            }

            public static class MessageBox { public static int Show(string text) => 0; }
        }
        """;

    private static readonly Lazy<DriverFactory> Shared = new(Build);

    public static DriverFactory Factory => Shared.Value;

    /// <summary>The modern driver's source for <paramref name="member"/>, which <see cref="DriverFactory.Create"/> writes beside the driver.</summary>
    public static string Source(string member)
    {
        string output = Directory.CreateTempSubdirectory("driver-source-").FullName;
        try
        {
            string driver = Factory.Create(new ExecutionRequest(new CallIdentity(member), [], []), output).Modern;
            return File.ReadAllText(Path.ChangeExtension(driver, ".cs"));
        }
        finally
        {
            Directory.Delete(output, recursive: true);
        }
    }

    private static DriverFactory Build()
    {
        IReadOnlyList<string> runtime = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        string legacy = Library("legacy", Odd.Replace("/*ONLYMODERN*/", "public static int OnlyLegacy() => 0;", StringComparison.Ordinal), runtime);
        string modern = Library(
            "modern",
            Odd.Replace("/*MODERN*/", "C = 3", StringComparison.Ordinal).Replace("/*ONLYMODERN*/", "public static int OnlyModern() => 0;", StringComparison.Ordinal),
            runtime);
        string forms = Library("forms", Forms, runtime, "System.Windows.Forms");
        return new DriverFactory(() => new DriverReferences([.. runtime, legacy, forms], [.. runtime, modern], [forms]));
    }

    private static string Library(string side, string source, IReadOnlyList<string> references, string name = "Odd")
    {
        string path = Path.Combine(AppContext.BaseDirectory, "odd", side, name + ".dll");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        CSharpCompilation compilation = CSharpCompilation.Create(
            name,
            [CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken)],
            [.. references.Select(static r => MetadataReference.CreateFromFile(r))],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        EmitResult result = compilation.Emit(path, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Success, string.Join('\n', result.Diagnostics));
        return path;
    }
}
