using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Linq;

using Equiv.Core.Configuration;
using Equiv.Core.RuntimeChanges;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Equiv.Frontend.CSharp.Lowering;

/// <summary>
/// The facts about a method that a <see cref="RuntimeChange.Requires"/> row needs (tickets P2-114 and P2-190). A row whose fact
/// does not hold of the method being lowered or fingerprinted is added to that method's list of suppressed rows, so none of its
/// calls is flagged runtime-changed; the lowering and the fingerprint both begin from <see cref="Suppress"/>'s list, and so agree.
/// Every fact but <see cref="RowPrecondition.AsyncStreamOperation"/> is about the method's calls to the row's own member: it holds
/// when one of them can reach the row's change, read from the call and the method around it.
/// </summary>
internal static class RowPreconditions
{
    private static readonly FrozenSet<string> AsyncStreamOperations =
        new[] { "ReadAsync", "WriteAsync", "BeginRead", "BeginWrite", "CopyToAsync", "FlushAsync" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>The facts that are about the calls to a row's own member (ticket P2-190).</summary>
    private static readonly ImmutableArray<RowPrecondition> CallFacts =
    [
        RowPrecondition.ShimmedVersionRead,
        RowPrecondition.SignedZeroResult,
        RowPrecondition.StaticFieldSet,
        RowPrecondition.Utf8Decoding,
        RowPrecondition.DefaultShellExecute,
    ];

    /// <summary><c>BindingFlags.Instance</c>.</summary>
    private const int Instance = 4;

    /// <summary><c>BindingFlags.Static</c>.</summary>
    private const int Static = 8;

    /// <summary>The newest Windows version .NET Framework's compatibility shim reports, 6.2 (Windows 8), as major and minor.</summary>
    private static readonly (int Major, int Minor) Shimmed = (6, 2);

    private const string GetField = "System.Type::GetField(string,System.Reflection.BindingFlags)";
    private const string GetFields = "System.Type::GetFields(System.Reflection.BindingFlags)";
    private const string StartWithInfo = "System.Diagnostics.Process::Start(System.Diagnostics.ProcessStartInfo)";

    /// <summary>
    /// <paramref name="suppressed"/>, and the members of the rows whose <see cref="RowPrecondition"/> does not hold of a method
    /// whose operations are <paramref name="roots"/> (its body and, for a constructor, its initializers), bound in
    /// <paramref name="compilation"/>. A fact about a value that came from a caller is not known here, so a method with no call
    /// that can reach a row's change does not fire the row.
    /// </summary>
    public static ImmutableArray<string> Suppress(ImmutableArray<string> suppressed, IEnumerable<IOperation?> roots, Compilation compilation)
    {
        RuntimeChangeTable table = RuntimeChangeTable.Load();
        ImmutableArray<IOperation> operations = [.. roots.OfType<IOperation>().SelectMany(static root => root.DescendantsAndSelf())];
        IEnumerable<string> asyncStream = operations.OfType<IInvocationOperation>().Any(static call => IsAsyncStreamOperation(call.TargetMethod))
            ? []
            : table.MembersRequiring(RowPrecondition.AsyncStreamOperation);
        ImmutableArray<(RowPrecondition Fact, string Member)> rows = [.. CallFacts.SelectMany(fact => table.MembersRequiring(fact).Select(member => (fact, member)))];
        FrozenSet<string> names = rows.Select(static row => SimpleName(row.Member)).ToFrozenSet(StringComparer.Ordinal);
        ImmutableArray<(IOperation Site, string Callee)> calls = [.. operations.SelectMany(site => Callees(site, compilation, names).Select(callee => (site, callee)))];
        IEnumerable<string> unreached = rows
            .Where(row => !calls.Any(call => call.Callee.StartsWith(row.Member, StringComparison.Ordinal) && Reaches(row.Fact, call.Site)))
            .Select(static row => row.Member);
        return [.. suppressed.Union(asyncStream.Concat(unreached), StringComparer.Ordinal)];
    }

    /// <summary>The member's name in a row's <c>Type::Name(</c> prefix: <c>Ceiling</c>, <c>get_OSVersion</c>.</summary>
    private static string SimpleName(string member) => member.Split("::")[1].Split('(')[0];

    /// <summary>
    /// The identities of the methods <paramref name="site"/> calls, or names as a delegate's target, whose name is one of
    /// <paramref name="names"/>: an invocation's method and, when it is a forwarder, the method it forwards to; a property's
    /// accessors; a method reference's method; a constructor.
    /// </summary>
    private static IEnumerable<string> Callees(IOperation site, Compilation compilation, FrozenSet<string> names)
    {
        IEnumerable<IMethodSymbol?> methods = site switch
        {
            IInvocationOperation call => [call.TargetMethod, Forwarders.Resolve(call.TargetMethod, compilation)?.Target],
            IPropertyReferenceOperation property => [property.Property.GetMethod, property.Property.SetMethod],
            IMethodReferenceOperation reference => [reference.Method],
            IObjectCreationOperation creation => [creation.Constructor],
            _ => [],
        };
        return methods.OfType<IMethodSymbol>().Where(method => names.Contains(method.Name)).Select(static method => CallIdentityFactory.Name(method, RenameMap.Empty));
    }

    /// <summary>Whether the call <paramref name="site"/> makes to a row's member can reach the change of a row that requires <paramref name="fact"/>.</summary>
    private static bool Reaches(RowPrecondition fact, IOperation site) => fact switch
    {
        RowPrecondition.ShimmedVersionRead => !IsUnshimmedRead(site),
        RowPrecondition.SignedZeroResult => !(site.Parent is IConversionOperation conversion && IsIntegral(conversion.Type!)),
        // The members of both are instance methods, which no forwarder calls: an invocation of one has a receiver.
        RowPrecondition.StaticFieldSet => !(site is IInvocationOperation field && LocalValues.HoldsOnly(field.Instance!, IsInstanceField)),
        RowPrecondition.Utf8Decoding => !(site is IInvocationOperation decoding && LocalValues.HoldsOnly(decoding.Instance!, IsAscii)),
        // RowPrecondition.DefaultShellExecute, the last of CallFacts.
        _ => !(site is IInvocationOperation call && IsName(call.TargetMethod, StartWithInfo) && SetsShellExecute(call.Arguments[0].Value, call)),
    };

    /// <summary>
    /// A read of an <c>OperatingSystem</c> whose only use is its <c>Platform</c>, or its <c>Version</c> compared with a
    /// version the shim never crosses: the shim reports the real version up to 6.2.9200 and 6.2.9200 above it. A child of a
    /// property reference that is no argument is its receiver.
    /// </summary>
    private static bool IsUnshimmedRead(IOperation site) =>
        site.Parent is IPropertyReferenceOperation outer
        && (string.Equals(outer.Property.Name, "Platform", StringComparison.Ordinal)
            || (string.Equals(outer.Property.Name, "Version", StringComparison.Ordinal) && IsComparedBelowShim(outer)));

    /// <summary>
    /// <paramref name="version"/> compared with an old version: <c>version.CompareTo(new Version(6, 1))</c>,
    /// <c>version &gt;= new Version(6, 1)</c>. A <c>Version</c> that is a call's child and no argument is its receiver, and
    /// <c>CompareTo</c> takes one argument.
    /// </summary>
    private static bool IsComparedBelowShim(IOperation version) => version.Parent switch
    {
        IInvocationOperation compare => string.Equals(compare.TargetMethod.Name, "CompareTo", StringComparison.Ordinal) && IsOldVersion(compare.Arguments[0].Value),
        IBinaryOperation binary => IsOldVersion(binary.LeftOperand == version ? binary.RightOperand : binary.LeftOperand),
        _ => false,
    };

    /// <summary><c>new Version(major, minor)</c> of constants no newer than <see cref="Shimmed"/>: a version with no build, which is older than 6.2.9200.</summary>
    private static bool IsOldVersion(IOperation value) =>
        value is IObjectCreationOperation creation
        && string.Equals(creation.Type!.ToDisplayString(), "System.Version", StringComparison.Ordinal)
        && creation.Arguments.OrderBy(static a => a.Parameter!.Ordinal).Select(static a => a.Value.ConstantValue.Value).ToArray() is [int major, int minor]
        && (major, minor).CompareTo(Shimmed) <= 0;

    private static IOperation Unconverted(IOperation value) => value is IConversionOperation conversion ? Unconverted(conversion.Operand) : value;

    private static bool IsIntegral(ITypeSymbol type) => type.SpecialType is
        SpecialType.System_SByte or SpecialType.System_Byte or SpecialType.System_Int16 or SpecialType.System_UInt16
        or SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Char;

    /// <summary>
    /// A field the method shows is an instance field: the result of <c>GetField</c>, or the variable of a <c>foreach</c> over
    /// <c>GetFields</c>, with constant flags that include <c>Instance</c> and not <c>Static</c>.
    /// </summary>
    private static bool IsInstanceField(IOperation value) => value switch
    {
        IInvocationOperation call => IsInstanceLookup(call, GetField),
        ILocalReferenceOperation local => LocalValues.Root(value).DescendantsAndSelf().OfType<IForEachLoopOperation>().Any(loop =>
            loop.Locals.Contains(local.Local, SymbolEqualityComparer.Default)
            && Unconverted(loop.Collection) is IInvocationOperation collection
            && IsInstanceLookup(collection, GetFields)),
        _ => false,
    };

    private static bool IsInstanceLookup(IInvocationOperation call, string lookup) =>
        IsName(call.TargetMethod, lookup)
        && call.Arguments.Single(a => a.Parameter!.Ordinal == call.TargetMethod.Parameters.Length - 1).Value.ConstantValue.Value is int flags
        && (flags & Instance) != 0
        && (flags & Static) == 0;

    private static bool IsAscii(IOperation value) =>
        value is IPropertyReferenceOperation reference && string.Equals(reference.Property.ToDisplayString(), "System.Text.Encoding.ASCII", StringComparison.Ordinal);

    /// <summary>
    /// Whether <paramref name="info"/>, passed to <c>Process.Start</c> by <paramref name="call"/>, has <c>UseShellExecute</c> set
    /// and <c>WindowStyle</c> never set: a new one whose initializer sets the one and not the other, or a local variable
    /// declared with a new one that nothing assigns again, used only for its other properties and as <c>Process.Start</c>'s
    /// argument, whose initializer sets <c>UseShellExecute</c> or an earlier statement of a block <paramref name="call"/> runs in does.
    /// </summary>
    private static bool SetsShellExecute(IOperation info, IInvocationOperation call) => info switch
    {
        IObjectCreationOperation creation => !Sets(creation.Initializer, "WindowStyle") && Sets(creation.Initializer, "UseShellExecute"),
        ILocalReferenceOperation local =>
            LocalValues.Initializer(local.Local, LocalValues.Root(info)) is IObjectCreationOperation creation
            && !Sets(creation.Initializer, "WindowStyle")
            && LocalValues.Writes(local.Local, LocalValues.Root(info)).IsEmpty
            && LocalValues.References(local.Local, LocalValues.Root(info)).All(IsStartInfoUse)
            && (Sets(creation.Initializer, "UseShellExecute") || IsSetEarlier(local.Local, call)),
        _ => false,
    };

    /// <summary>Whether a new <c>ProcessStartInfo</c>'s <paramref name="initializer"/>, if it has one, sets <paramref name="property"/>.</summary>
    private static bool Sets(IObjectOrCollectionInitializerOperation? initializer, string property) =>
        initializer is not null && Properties(initializer.Initializers).Any(set => string.Equals(set, property, StringComparison.Ordinal));

    /// <summary>The names of the properties <paramref name="operations"/> assign; a <c>ProcessStartInfo</c> has no public field.</summary>
    private static IEnumerable<string> Properties(IEnumerable<IOperation> operations) =>
        operations.OfType<IAssignmentOperation>().Select(static a => a.Target).OfType<IPropertyReferenceOperation>().Select(static p => p.Property.Name);

    /// <summary>
    /// A reference to the variable that reads or writes one of its properties but <c>WindowStyle</c> (a child of a property
    /// reference that is no argument is its receiver), or passes it to <c>Process.Start</c>.
    /// </summary>
    private static bool IsStartInfoUse(IOperation reference) => reference.Parent switch
    {
        IPropertyReferenceOperation property => !string.Equals(property.Property.Name, "WindowStyle", StringComparison.Ordinal),
        IArgumentOperation argument => argument.Parent is IInvocationOperation start && IsName(start.TargetMethod, StartWithInfo),
        _ => false,
    };

    /// <summary>
    /// Whether, in a block <paramref name="call"/> runs in, a statement before the one that holds it sets <paramref name="local"/>'s
    /// <c>UseShellExecute</c>. A block with a label could jump past that statement, and a lambda or local function could run before
    /// it, so the search stops at either.
    /// </summary>
    private static bool IsSetEarlier(ILocalSymbol local, IOperation call)
    {
        for (IOperation inner = call; inner.Parent is { } outer && outer is not (IAnonymousFunctionOperation or ILocalFunctionOperation); inner = outer)
        {
            if (outer is IBlockOperation block
                && !block.Operations.OfType<ILabeledOperation>().Any()
                && block.Operations.TakeWhile(statement => statement != inner).Any(statement => IsShellExecuteAssignment(statement, local)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The statement <c>local.UseShellExecute = value;</c>.</summary>
    private static bool IsShellExecuteAssignment(IOperation statement, ILocalSymbol local) =>
        statement is IExpressionStatementOperation { Operation: ISimpleAssignmentOperation assignment }
        && assignment.Target is IPropertyReferenceOperation target
        && string.Equals(target.Property.Name, "UseShellExecute", StringComparison.Ordinal)
        && target.Instance is ILocalReferenceOperation receiver
        && SymbolEqualityComparer.Default.Equals(receiver.Local, local);

    private static bool IsName(IMethodSymbol method, string identity) =>
        string.Equals(CallIdentityFactory.Name(method, RenameMap.Empty), identity, StringComparison.Ordinal);

    private static bool IsAsyncStreamOperation(IMethodSymbol method) =>
        AsyncStreamOperations.Contains(method.Name) && DerivesFromStream(method.ContainingType);

    private static bool DerivesFromStream(INamedTypeSymbol? type)
    {
        for (; type is not null; type = type.BaseType)
        {
            if (string.Equals(type.Name, "Stream", StringComparison.Ordinal) && string.Equals(type.ContainingNamespace.ToDisplayString(), "System.IO", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
