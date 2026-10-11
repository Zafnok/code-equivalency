using System.Collections.Immutable;
using System.Linq;

using Equiv.Core.RuntimeChanges;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Equiv.Frontend.CSharp.Lowering;

/// <summary>
/// A call's operands as a <c>runtime-changes.json</c> row's precondition reads them (ticket P2-073): the receiver of an
/// instance call, then each argument under its parameter's name, with its value when it is a compile-time constant.
/// </summary>
internal static class CallArguments
{
    /// <summary>
    /// The operands of the call <paramref name="site"/> makes to <paramref name="method"/>; none when the site is no
    /// invocation or object creation, or calls another method, as a call resolved to a forwarder's target does.
    /// </summary>
    public static ImmutableArray<CallArgument> Of(IOperation? site, IMethodSymbol method) => Of(site, method, static _ => false);

    /// <summary>As the two-argument overload, with each operand <paramref name="isValidPath"/> holds of marked a <see cref="CallArgument.ValidPath"/> (ticket P2-074).</summary>
    public static ImmutableArray<CallArgument> Of(IOperation? site, IMethodSymbol method, Func<IOperation, bool> isValidPath) => site switch
    {
        IInvocationOperation call when Calls(call.TargetMethod, method) =>
            [.. Receiver(call.Instance, isValidPath), .. call.Arguments.Select(a => Of(a.Parameter!.Name, a.Parameter.Type, a.Value, isValidPath))],
        IObjectCreationOperation creation when Calls(creation.Constructor, method) =>
            [.. creation.Arguments.Select(a => Of(a.Parameter!.Name, a.Parameter.Type, a.Value, isValidPath))],
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

    private static IEnumerable<CallArgument> Receiver(IOperation? instance, Func<IOperation, bool> isValidPath) =>
        instance is null ? [] : [Of(CallArgument.Receiver, instance.Type!, instance, isValidPath)];

    private static CallArgument Of(string parameter, ITypeSymbol type, IOperation value, Func<IOperation, bool> isValidPath) =>
        new(parameter, type.SpecialType == SpecialType.System_String, value.ConstantValue.HasValue, value.ConstantValue.Value) { ValidPath = isValidPath(value) };
}
