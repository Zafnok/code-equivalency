using Equiv.Core;
using Equiv.Core.Execution;
using Equiv.Frontend.CSharp.Execution;


using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Execution;

/// <summary>
/// The driver factory over <see cref="DriverLibraries"/>: the test host's runtime assemblies plus <c>Odd</c>, in a legacy
/// and a modern version, so every case runs on any OS without the .NET Framework 4.8 targeting pack. The legacy side still
/// compiles as C# 7.3. The real runtimes run only in <c>RuntimeDiffTests</c> (Equiv.Tests.Integration).
/// </summary>
public sealed class DriverFactoryTests
{
    private static DriverFactory Factory => DriverLibraries.Factory;

    private static ExecutionSignature Single(string member) => Assert.Single(Factory.Resolve(member));

    [Fact]
    public void APrefixListsEveryOverloadInIdentityOrder()
    {
        Assert.Equal(
            ["System.String::ToUpper()", "System.String::ToUpper(System.Globalization.CultureInfo)"],
            Factory.Resolve("System.String::ToUpper(").Select(static s => s.Member.Value),
            StringComparer.Ordinal);
        Assert.Equal(["System.String::ToUpperInvariant()"], Factory.Resolve("System.String::ToUpperI").Select(static s => s.Member.Value), StringComparer.Ordinal);
    }

    [Fact]
    public void AnInstanceMemberTakesItsReceiverFirst()
    {
        ExecutionSignature indexOf = Single("System.String::IndexOf(char,System.StringComparison)");

        Assert.Equal(
            [("string", ExecutionTypeKind.Text), ("char", ExecutionTypeKind.Character), ("System.StringComparison", ExecutionTypeKind.Enum)],
            indexOf.Parameters.Select(static p => (p.TypeName, p.Kind)));
        Assert.Equal(["0", "1", "2", "3", "4", "5"], indexOf.Parameters[2].EnumValues, StringComparer.Ordinal);
        Assert.Empty(indexOf.NotConstructible);
        Assert.Equal([ExecutionTypeKind.Character, ExecutionTypeKind.Signed32], Single("System.String::.ctor(char,int)").Parameters.Select(static p => p.Kind));
    }

    [Fact]
    public void EveryBuildableKindIsClassified()
    {
        ExecutionSignature text = Single(DriverLibraries.AllKinds);

        Assert.Equal(
            [
                ExecutionTypeKind.Boolean, ExecutionTypeKind.Character, ExecutionTypeKind.SignedByte, ExecutionTypeKind.UnsignedByte,
                ExecutionTypeKind.Signed16, ExecutionTypeKind.Unsigned16, ExecutionTypeKind.Signed32, ExecutionTypeKind.Unsigned32,
                ExecutionTypeKind.Signed64, ExecutionTypeKind.Unsigned64, ExecutionTypeKind.Binary32, ExecutionTypeKind.Binary64,
                ExecutionTypeKind.DecimalNumber, ExecutionTypeKind.Text, ExecutionTypeKind.NullOnly, ExecutionTypeKind.Enum, ExecutionTypeKind.Enum,
            ],
            text.Parameters.Select(static p => p.Kind));
        Assert.Empty(text.NotConstructible);
    }

    [Fact]
    public void AnEnumHasTheDefinedValuesOfBothRuntimes() =>
        Assert.Equal(["1", "2", "3"], Assert.Single(Single("Odd.Members::Echo(Odd.Small)").Parameters).EnumValues, StringComparer.Ordinal);

    [Theory]
    [InlineData("System.DateTime::AddDays(double)", "System.DateTime")]
    [InlineData("Odd.Members::Ref(ref int)", "ref int")]
    [InlineData("Odd.Members::In(int)", "in int")]
    [InlineData("Odd.Members::Out(out int)", "out int")]
    [InlineData("Odd.Members::Vararg(int)", "__arglist")]
    [InlineData("Odd.Members::Pointer()", "returns a pointer")]
    [InlineData("Odd.Members::FunctionPointer()", "returns a pointer")]
    [InlineData("Odd.Members::Generic`1(T)", "generic")]
    [InlineData("Odd.Generic`1::Y()", "generic")]
    [InlineData("Odd.Base::.ctor()", "constructor of an abstract type")]
    [InlineData("Odd.Holder::op_Addition(Odd.Holder,Odd.Holder)", "not a method, a property getter or a constructor")]
    [InlineData("Odd.Holder::set_Settable(int)", "not a method, a property getter or a constructor")]
    public void AMemberGeneratedSourceCannotCallIsNotConstructible(string member, string reason) =>
        Assert.Equal(reason, Single(member).NotConstructible[0]);

    [Theory]
    [InlineData("Odd.Members::OnlyModern()")]
    [InlineData("Odd.Members::OnlyLegacy()")]
    [InlineData("Odd.Members::Internal()")]
    [InlineData("Odd.Hidden.Inner::X()")]
    [InlineData("Odd.Nope::X()")]
    [InlineData("Odd.Members")]
    public void OnlyPublicMembersOnBothRuntimesResolve(string member) => Assert.Empty(Factory.Resolve(member));

    [Theory]
    [InlineData("Odd.Outer.Inner::X()")]
    [InlineData("Global.Nested::Z()")]
    public void ANestedTypeResolves(string member) => Assert.Empty(Single(member).NotConstructible);

    [Theory]
    [InlineData("Odd.Members::B()", "Returned(W.B(r))")]
    [InlineData("Odd.Members::S8()", "Returned(W.I(r))")]
    [InlineData("Odd.Members::S16()", "Returned(W.I(r))")]
    [InlineData("Odd.Members::S32()", "Returned(W.I(r))")]
    [InlineData("Odd.Members::S64()", "Returned(W.I(r))")]
    [InlineData("Odd.Members::U8()", "Returned(W.U(r))")]
    [InlineData("Odd.Members::U16()", "Returned(W.U(r))")]
    [InlineData("Odd.Members::U32()", "Returned(W.U(r))")]
    [InlineData("Odd.Members::U64()", "Returned(W.U(r))")]
    [InlineData("Odd.Members::C()", "Returned(W.U(r))")]
    [InlineData("Odd.Members::F()", "Returned(W.F(r))")]
    [InlineData("Odd.Members::D()", "Returned(W.D(r))")]
    [InlineData("Odd.Members::M()", "Returned(W.M(r))")]
    [InlineData("Odd.Members::When()", "NotComparable(\"System.DateTime\")")]
    public void EachPrimitiveReturnHasItsCanonicalWriter(string member, string answer) =>
        Assert.Contains($"return {answer};", DriverLibraries.Source(member), StringComparison.Ordinal);

    [Fact]
    public void EveryUnsignedEnumIsDecodedUnsigned()
    {
        string source = DriverLibraries.Source("Odd.Members::Widths(Odd.Wide16,Odd.Wide32,Odd.Wide64)");

        Assert.Contains("global::Odd.Wide16 p0 = (global::Odd.Wide16)R.U(a[1]);", source, StringComparison.Ordinal);
        Assert.Contains("global::Odd.Wide32 p1 = (global::Odd.Wide32)R.U(a[2]);", source, StringComparison.Ordinal);
        Assert.Contains("global::Odd.Wide64 p2 = (global::Odd.Wide64)R.U(a[3]);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSameMemberCompilesToTheSameBytes()
    {
        string first = Directory.CreateTempSubdirectory("driver-factory-").FullName;
        string second = Directory.CreateTempSubdirectory("driver-factory-").FullName;
        try
        {
            ExecutionRequest request = new(new CallIdentity("System.String::IndexOf(string)"), [], []);
            ExecutionDrivers a = Factory.Create(request, first);
            ExecutionDrivers b = Factory.Create(request, second);

            Assert.Equal(File.ReadAllBytes(a.Legacy), File.ReadAllBytes(b.Legacy));
            Assert.Equal(File.ReadAllBytes(a.Modern), File.ReadAllBytes(b.Modern));
        }
        finally
        {
            Directory.Delete(first, recursive: true);
            Directory.Delete(second, recursive: true);
        }
    }

    [Theory]
    [InlineData(DriverLibraries.AllKinds)]
    [InlineData("Odd.Members::B()")]
    [InlineData("Odd.Members::S8()")]
    [InlineData("Odd.Members::U8()")]
    [InlineData("Odd.Members::S16()")]
    [InlineData("Odd.Members::U16()")]
    [InlineData("Odd.Members::S32()")]
    [InlineData("Odd.Members::U32()")]
    [InlineData("Odd.Members::S64()")]
    [InlineData("Odd.Members::U64()")]
    [InlineData("Odd.Members::C()")]
    [InlineData("Odd.Members::F()")]
    [InlineData("Odd.Members::D()")]
    [InlineData("Odd.Members::M()")]
    [InlineData("Odd.Members::Echo(Odd.Small)")]
    [InlineData("Odd.Members::Flip(Odd.Signed)")]
    [InlineData("Odd.Members::Ints()")]
    [InlineData("Odd.Members::Jagged()")]
    [InlineData("Odd.Members::Grid()")]
    [InlineData("Odd.Members::Objects()")]
    [InlineData("Odd.Members::Maybe()")]
    [InlineData("Odd.Members::When()")]
    [InlineData("Odd.Members::Nothing()")]
    [InlineData("Odd.Holder::get_Item(int)")]
    [InlineData("Odd.Holder::get_Size()")]
    [InlineData("Odd.Holder::get_Count()")]
    [InlineData("System.String::.ctor(char,int)")]
    [InlineData("System.String::IndexOf(string)")]
    [InlineData("System.String::Split(char[])")]
    public void BothDriversCompile(string member)
    {
        string directory = Directory.CreateTempSubdirectory("driver-factory-").FullName;
        try
        {
            ExecutionDrivers drivers = Factory.Create(new ExecutionRequest(new CallIdentity(member), [], ["invariant"]), directory);

            Assert.Equal(Path.Combine(directory, "legacy", "EquivDriver.exe"), drivers.Legacy);
            Assert.Equal(Path.Combine(directory, "modern", "EquivDriver.dll"), drivers.Modern);
            Assert.True(File.Exists(drivers.Legacy));
            Assert.Contains("<supportedRuntime version=\"v4.0\" sku=\".NETFramework,Version=v4.8\" />", File.ReadAllText(drivers.Legacy + ".config"), StringComparison.Ordinal);
            Assert.True(File.Exists(drivers.Modern));
            Assert.Contains("\"name\": \"Microsoft.NETCore.App\"", File.ReadAllText(Path.Combine(directory, "modern", "EquivDriver.runtimeconfig.json")), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("System.String::IndexOf(string)", "string p0 = R.S(a[1]);", "r = p0.IndexOf(p1);", "Returned(W.I(r))")]
    [InlineData("System.String::.ctor(char,int)", "char p0 = checked((char)R.U(a[1]));", "r = new string(p0, p1);", "Returned(W.Str(r))")]
    [InlineData("Odd.Holder::get_Item(int)", "global::Odd.Holder p0 = (global::Odd.Holder)a[1];", "r = p0[p1];", "Returned(W.I(r))")]
    [InlineData("Odd.Holder::get_Size()", "int r;", "r = p0.Size;", "Returned(W.I(r))")]
    [InlineData("Odd.Holder::get_Count()", "int r;", "r = global::Odd.Holder.Count;", "Returned(W.I(r))")]
    [InlineData("Odd.Members::Echo(Odd.Small)", "global::Odd.Small p0 = (global::Odd.Small)R.U(a[1]);", "r = global::Odd.Members.Echo(p0);", "Returned(W.U(((byte)r)))")]
    [InlineData("Odd.Members::Flip(Odd.Signed)", "global::Odd.Signed p0 = (global::Odd.Signed)R.I(a[1]);", "r = global::Odd.Members.Flip(p0);", "Returned(W.I(((long)r)))")]
    [InlineData("Odd.Members::Ints()", "global::System.Collections.Generic.List<int> r;", "r = global::Odd.Members.Ints();", "Returned(W.Seq(r, x0 => W.I(x0)))")]
    [InlineData("Odd.Members::Jagged()", "int[][] r;", "r = global::Odd.Members.Jagged();", "Returned(W.Seq(r, x0 => W.Seq(x0, x1 => W.I(x1))))")]
    [InlineData("Odd.Members::Grid()", "int[,] r;", "r = global::Odd.Members.Grid();", "NotComparable(\"int[*,*]\")")]
    [InlineData("Odd.Members::Objects()", "global::System.Collections.Generic.List<object> r;", "r = global::Odd.Members.Objects();", "NotComparable(\"System.Collections.Generic.List<object>\")")]
    [InlineData("Odd.Members::Maybe()", "int? r;", "r = global::Odd.Members.Maybe();", "NotComparable(\"int?\")")]
    [InlineData("Odd.Members::Nothing()", "Case(List<object> a)", "global::Odd.Members.Nothing();", "Returned(\"null\")")]
    public void TheDriverCallsTheMemberAndCanonicalisesItsStaticReturnType(string member, string declaration, string call, string answer)
    {
        string source = DriverLibraries.Source(member);

        Assert.Contains(declaration, source, StringComparison.Ordinal);
        Assert.Contains(call, source, StringComparison.Ordinal);
        Assert.Contains($"return {answer};", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Odd.Members::Gone()", "does not compile: ")]
    [InlineData("Odd.Members::OnlyModern()", "Odd.Members::OnlyModern() is not a public member on .NET Framework 4.8")]
    [InlineData("Odd.Members::OnlyLegacy()", "Odd.Members::OnlyLegacy() is not a public member on .NET 10")]
    [InlineData("System.DateTime::AddDays(double)", "no input can be built for System.DateTime")]
    public void ADriverThatCannotBeBuiltThrows(string member, string message)
    {
        string directory = Directory.CreateTempSubdirectory("driver-factory-").FullName;
        try
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => Factory.Create(new ExecutionRequest(new CallIdentity(member), [], []), directory));

            Assert.Contains(message, exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ARuntimeWithoutReferenceAssembliesCannotResolve()
    {
        DriverFactory factory = new(static () => new DriverReferences([], ["unused.dll"]));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => factory.Resolve("System.String::ToUpper("));

        Assert.Equal("no .NET Framework 4.8 reference assemblies are installed", exception.Message);
        string[] runtime = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        DriverFactory noModern = new(() => new DriverReferences(runtime, []));
        Assert.Equal("no .NET 10 reference assemblies are installed", Assert.Throws<InvalidOperationException>(() => noModern.Resolve("System.String::ToUpper(")).Message);
        Assert.Throws<ArgumentNullException>(() => factory.Resolve(null!));
        Assert.Throws<ArgumentNullException>(() => factory.Create(null!, "."));
    }

    [Fact]
    public void TheInstalledFactoryLocatesItsReferencesLazily() => Assert.NotNull(new DriverFactory());
}
