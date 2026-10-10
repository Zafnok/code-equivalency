using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Equiv.Frontend.CSharp.Lowering;

/// <summary>
/// Whether a call hands its callee <c>StringComparison.Ordinal</c> or <c>StringComparison.OrdinalIgnoreCase</c> as a
/// constant (ticket P2-075). Ordinal comparisons do not use the runtime's culture data, so a runtime-changes row about
/// culture-sensitive comparison (<see cref="Equiv.Core.RuntimeChanges.RuntimeChange.OrdinalUnaffected"/>) does not
/// apply to such a call.
/// </summary>
internal static class OrdinalComparison
{
    private const string ComparisonType = "System.StringComparison";

    public static bool IsConstantArgument(IInvocationOperation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        return invocation.Arguments.Any(static argument =>
            string.Equals(argument.Parameter!.Type.ToDisplayString(), ComparisonType, StringComparison.Ordinal)
            && argument.Value.ConstantValue is { HasValue: true, Value: (int)StringComparison.Ordinal or (int)StringComparison.OrdinalIgnoreCase });
    }
}
