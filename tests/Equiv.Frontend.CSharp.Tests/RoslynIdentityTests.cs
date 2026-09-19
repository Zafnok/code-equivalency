using System.Linq;

using Equiv.Core.Configuration;

using Microsoft.CodeAnalysis;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests;

public sealed class RoslynIdentityTests
{
    public static TheoryData<string, string, string> Cases => new()
    {
        { "namespace N { public class C { public void M() {} } }", "M", "N.C::M()" },
        { "namespace N { public class C { public int M(int a, string b) => a; } }", "M", "N.C::M(int,string)" },
        { "namespace N { public class C { public void M(ref int a) { a = 1; } } }", "M", "N.C::M(ref int)" },
        { "namespace N { public class C { public void M(out int a) { a = 0; } } }", "M", "N.C::M(out int)" },
        { "namespace N { public class C { public void M<T>(T a) {} } }", "M", "N.C::M`1(T)" },
        { "namespace N { public class C<T> { public void M() {} } }", "M", "N.C`1::M()" },
        { "namespace N { public class C { public C() {} } }", ".ctor", "N.C::.ctor()" },
        { "namespace N { public class C { public int P { get; set; } } }", "get_P", "N.C::get_P()" },
        { "namespace N { public class C { public int P { get; set; } } }", "set_P", "N.C::set_P(int)" },
        { "namespace N { public class C { public static C operator +(C a, C b) => a; } }", "op_Addition", "N.C::op_Addition(N.C,N.C)" },
        { "public class C { public void M() {} }", "M", "C::M()" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void FormatsGenericsRefOutAndAccessors(string source, string memberName, string expectedIdentity)
    {
        Compilation compilation = RoslynTestCompilations.Compile(source);
        IMethodSymbol symbol = ProcedureEnumerator.Enumerate(compilation).Single(p => string.Equals(p.Symbol.Name, memberName, StringComparison.Ordinal)).Symbol;

        Assert.Equal(expectedIdentity, RoslynIdentity.Of(symbol, RenameMap.Empty).Value);
    }

    [Fact]
    public void AppliesNamespaceRename()
    {
        Compilation compilation = RoslynTestCompilations.Compile("namespace Old.Ns { public class C { public void M() {} } }");
        IMethodSymbol symbol = ProcedureEnumerator.Enumerate(compilation).Single(p => p.Symbol.Name is "M").Symbol;
        RenameMap renames = RenameMap.Empty with
        {
            Namespaces = RenameMap.Empty.Namespaces.Add("Old.Ns", "New.Ns"),
        };

        Assert.Equal("New.Ns.C::M()", RoslynIdentity.Of(symbol, renames).Value);
    }

    [Fact]
    public void NullArgumentsThrow()
    {
        Compilation compilation = RoslynTestCompilations.Compile("public class C { public void M() {} } ");
        IMethodSymbol symbol = ProcedureEnumerator.Enumerate(compilation).Single(p => p.Symbol.Name is "M").Symbol;

        Assert.Throws<ArgumentNullException>(() => RoslynIdentity.Of(null!, RenameMap.Empty));
        Assert.Throws<ArgumentNullException>(() => RoslynIdentity.Of(symbol, null!));
    }
}
