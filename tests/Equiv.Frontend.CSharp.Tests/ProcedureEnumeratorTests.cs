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

    /// <summary>
    /// What is no procedure: a local function and a lambda, which are no type members, a compiler-generated member, a
    /// destructor, and an abstract member, which names no implementation. An <c>extern</c> member is one (ADR 0054).
    /// </summary>
    [Fact]
    public void ExcludesLocalFunctionsLambdasImplicitAbstract()
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

                    ~C() { }
                }

                public record R(int X);
            }
            """);

        ImmutableArray<EnumeratedProcedure> procedures = ProcedureEnumerator.Enumerate(compilation);

        Assert.Contains(procedures, static p => p.Symbol.Name is "HasLocalAndLambda");
        Assert.DoesNotContain(procedures, static p => p.Symbol.Name is "Local");
        Assert.DoesNotContain(procedures, static p => p.Symbol.MethodKind == MethodKind.LambdaMethod);
        Assert.DoesNotContain(procedures, static p => p.Symbol.Name is "AbstractMethod");
        Assert.DoesNotContain(procedures, static p => p.Symbol.MethodKind == MethodKind.Destructor);
        Assert.DoesNotContain(procedures, static p => p.Symbol.IsImplicitlyDeclared);
    }

    /// <summary>
    /// ADR 0054 decision 1 (ticket P2-145): an <c>extern</c> member is a procedure, whatever names its implementation or
    /// nothing does: a <c>[DllImport]</c> method, an <c>InternalCall</c> method, one with no attribute, and an
    /// <c>extern</c> constructor, accessor and operator.
    /// </summary>
    [Fact]
    public void IncludesExternMembers()
    {
        Compilation compilation = RoslynTestCompilations.Compile("""
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;

            namespace N
            {
                public class C
                {
                    [DllImport("a.dll")] public static extern int Imported();
                    [MethodImpl(MethodImplOptions.InternalCall)] public extern int Internal();
                    public extern void Bare();
                    [MethodImpl(MethodImplOptions.InternalCall)] public extern C(int x);
                    public static extern int P { [DllImport("a.dll")] get; }
                    [MethodImpl(MethodImplOptions.InternalCall)] public static extern C operator +(C a, C b);
                }
            }
            """);

        ImmutableArray<EnumeratedProcedure> procedures = ProcedureEnumerator.Enumerate(compilation);

        Assert.Equal(
            [".ctor", "Bare", "Imported", "Internal", "get_P", "op_Addition"],
            procedures.Select(static p => p.Symbol.Name).Order(StringComparer.Ordinal),
            StringComparer.Ordinal);
        Assert.All(procedures, static p => Assert.True(p.Symbol.IsExtern));
    }

    /// <summary>
    /// Ticket P2-145: Roslyn reports a partial method whose implementing part is <c>extern</c> as <c>extern</c>, and it is
    /// one procedure, located at its defining part.
    /// </summary>
    [Fact]
    public void IncludesAPartialMethodWhoseImplementingPartIsExtern()
    {
        Compilation compilation = RoslynTestCompilations.Compile("""
            using System.Runtime.InteropServices;

            namespace N
            {
                public static partial class C
                {
                    public static partial int F();
                    [DllImport("a.dll")] public static extern partial int F();
                }
            }
            """);

        EnumeratedProcedure procedure = Assert.Single(ProcedureEnumerator.Enumerate(compilation));

        Assert.Equal("F", procedure.Symbol.Name);
        Assert.True(procedure.Symbol.IsExtern);
        Assert.True(procedure.Symbol.IsPartialDefinition);
        Assert.Equal(6, procedure.Location.GetLineSpan().StartLinePosition.Line);
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
