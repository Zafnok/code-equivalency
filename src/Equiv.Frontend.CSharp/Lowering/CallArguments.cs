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
    public static ImmutableArray<CallArgument> Of(IOperation? site, IMethodSymbol method) => site switch
    {
        IInvocationOperation call when Calls(call.TargetMethod, method) => [.. Receiver(call.Instance), .. call.Arguments.Select(Of)],
        IObjectCreationOperation creation when Calls(creation.Constructor, method) => [.. creation.Arguments.Select(Of)],
        _ => [],
    };

    private static bool Calls(IMethodSymbol? called, IMethodSymbol method) => SymbolEqualityComparer.Default.Equals(called, method);

    private static IEnumerable<CallArgument> Receiver(IOperation? instance) =>
        instance is null ? [] : [Of(CallArgument.Receiver, instance.Type!, instance)];

    private static CallArgument Of(IArgumentOperation argument) => Of(argument.Parameter!.Name, argument.Parameter.Type, argument.Value);

    private static CallArgument Of(string parameter, ITypeSymbol type, IOperation value) =>
        new(parameter, type.SpecialType == SpecialType.System_String, value.ConstantValue.HasValue, value.ConstantValue.Value);
}
