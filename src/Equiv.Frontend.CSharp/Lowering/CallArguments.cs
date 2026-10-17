using System.Collections.Immutable;
using System.Linq;

using Equiv.Core.RuntimeChanges;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Equiv.Frontend.CSharp.Lowering;

/// <summary>
/// A call's operands as a <c>runtime-changes.json</c> row's precondition reads them (ticket P2-073): the receiver of an
/// instance call, then each argument under its parameter's name, with its value when it is a compile-time constant, or
/// a read of a framework field the table's <c>fieldValues</c> lists, whose documented value it then has (ticket P2-189).
/// </summary>
internal static class CallArguments
{
    /// <summary>
    /// The operands of the call <paramref name="site"/> makes to <paramref name="method"/>; none when the site is no
    /// invocation or object creation, or calls another method, as a call resolved to a forwarder's target does.
    /// </summary>
    public static ImmutableArray<CallArgument> Of(IOperation? site, IMethodSymbol method) => Of(site, method, static _ => false);

    /// <summary>As the two-argument overload, with each operand <paramref name="isValidPath"/> holds of marked a <see cref="CallArgument.ValidPath"/> (ticket P2-074).</summary>
    public static ImmutableArray<CallArgument> Of(IOperation? site, IMethodSymbol method, Func<IOperation, bool> isValidPath) =>
        Operands(site, method, new PathTests(isValidPath, static _ => false));

    /// <summary>
    /// As the three-argument overload, with each operand that is a valid path, or that <paramref name="isValidPathOrEmpty"/>
    /// holds of, marked a <see cref="CallArgument.ValidPathOrEmpty"/> too (ticket P2-232).
    /// </summary>
    public static ImmutableArray<CallArgument> Of(IOperation? site, IMethodSymbol method, Func<IOperation, bool> isValidPath, Func<IOperation, bool> isValidPathOrEmpty) =>
        Operands(site, method, new PathTests(isValidPath, isValidPathOrEmpty));

    private static ImmutableArray<CallArgument> Operands(IOperation? site, IMethodSymbol method, PathTests tests) => site switch
    {
        IInvocationOperation call when Calls(call.TargetMethod, method) =>
            [.. Receiver(call.Instance, tests), .. call.Arguments.Select(a => Operand(a.Parameter!.Name, a.Parameter.Type, a.Value, tests))],
        IObjectCreationOperation creation when Calls(creation.Constructor, method) =>
            [.. creation.Arguments.Select(a => Operand(a.Parameter!.Name, a.Parameter.Type, a.Value, tests))],
        _ => [],
    };

    /// <summary>
    /// Whether the call <paramref name="site"/> makes to <paramref name="method"/> passes an ordinal
    /// <c>StringComparison</c> as a constant (<see cref="OrdinalComparison"/>; ticket P2-075). A site that calls another
    /// method, as a call resolved to a forwarder's target does, says nothing of it.
    /// </summary>
    public static bool IsOrdinalComparison(IOperation? site, IMethodSymbol method) =>
        site is IInvocationOperation call && Calls(call.TargetMethod, method) && OrdinalComparison.IsConstantArgument(call);

    private static bool Calls(IMethodSymbol? called, IMethodSymbol method) => SymbolEqualityComparer.Default.Equals(called, method);

    private static IEnumerable<CallArgument> Receiver(IOperation? instance, PathTests tests) =>
        instance is null ? [] : [Operand(CallArgument.Receiver, instance.Type!, instance, tests)];

    private static CallArgument Operand(string parameter, ITypeSymbol type, IOperation value, PathTests tests)
    {
        bool valid = tests.IsValidPath(value);
        return Constant(new CallArgument(parameter, type.SpecialType == SpecialType.System_String, value.ConstantValue.HasValue, value.ConstantValue.Value)
        {
            ValidPath = valid,
            ValidPathOrEmpty = valid || tests.IsValidPathOrEmpty(value),
            IsCharOrIndex = type.SpecialType is SpecialType.System_Char or SpecialType.System_Int32,
        }, value);
    }

    /// <summary><paramref name="argument"/>, as a constant with its documented value when <paramref name="value"/> reads a field the table lists.</summary>
    private static CallArgument Constant(CallArgument argument, IOperation value) =>
        value is IFieldReferenceOperation { Field: var field }
            && RuntimeChangeTable.Load().TryGetFieldValue($"{field.ContainingType.ToDisplayString()}::{field.Name}", out string? documented)
            ? argument with { IsConstant = true, Value = documented }
            : argument;

    /// <summary>What marks an operand a <see cref="CallArgument.ValidPath"/>, and what a <see cref="CallArgument.ValidPathOrEmpty"/>.</summary>
    private sealed record PathTests(Func<IOperation, bool> IsValidPath, Func<IOperation, bool> IsValidPathOrEmpty);
}
