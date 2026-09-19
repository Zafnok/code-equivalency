using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests;

public sealed class ProcedureEnumeratorTests
{
    [Fact]
    public void IncludesMethodsCtorsAccessorsOperators()
    {
        Compilation compilation = RoslynTestCompilations.Compile("""
            namespace N
            {
                public class C
                {
                    public C() { }
                    public int M(int a) => a;
                    public int P { get; set; }
                    public static C operator +(C a, C b) => a;
                }
            }
            """);

        ImmutableArray<EnumeratedProcedure> procedures = ProcedureEnumerator.Enumerate(compilation);

        Assert.Contains(procedures, static p => p.Symbol.MethodKind == MethodKind.Constructor);
        Assert.Contains(procedures, static p => p.Symbol.Name is "M");
        Assert.Contains(procedures, static p => p.Symbol.Name is "get_P");
        Assert.Contains(procedures, static p => p.Symbol.Name is "set_P");
        Assert.Contains(procedures, static p => p.Symbol.MethodKind == MethodKind.UserDefinedOperator);
    }

    [Fact]
    public void ExcludesLocalFunctionsLambdasImplicitAbstractExtern()
    {
        Compilation compilation = RoslynTestCompilations.Compile("""
            using System;

            namespace N
            {
                public abstract class A
                {
                    public abstract void AbstractMethod();
                }

                public class C
                {
                    public void HasLocalAndLambda()
                    {
                        void Local() { }
                        Action a = () => { };
                        Local();
                        a();
                    }

                    public extern void ExternMethod();
                }

                public record R(int X);
            }
            """);

        ImmutableArray<EnumeratedProcedure> procedures = ProcedureEnumerator.Enumerate(compilation);

        Assert.Contains(procedures, static p => p.Symbol.Name is "HasLocalAndLambda");
        Assert.DoesNotContain(procedures, static p => p.Symbol.Name is "Local");
        Assert.DoesNotContain(procedures, static p => p.Symbol.MethodKind == MethodKind.LambdaMethod);
        Assert.DoesNotContain(procedures, static p => p.Symbol.Name is "AbstractMethod");
        Assert.DoesNotContain(procedures, static p => p.Symbol.Name is "ExternMethod");
        Assert.DoesNotContain(procedures, static p => p.Symbol.IsImplicitlyDeclared);
    }

    [Fact]
    public void IncludesMembersOfNestedTypes()
    {
        Compilation compilation = RoslynTestCompilations.Compile("""
            namespace N
            {
                public class Outer
                {
                    public class Inner
                    {
                        public void M() { }
                    }
                }
            }
            """);

        ImmutableArray<EnumeratedProcedure> procedures = ProcedureEnumerator.Enumerate(compilation);

        Assert.Contains(procedures, static p => p.Symbol.Name is "M" && p.Symbol.ContainingType.Name is "Inner");
    }

    [Fact]
    public void NullCompilationThrows() =>
        Assert.Throws<ArgumentNullException>(static () => ProcedureEnumerator.Enumerate(null!));
}
