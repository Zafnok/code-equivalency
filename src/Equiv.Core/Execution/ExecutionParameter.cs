namespace Equiv.Core.Execution;

/// <summary>
/// A parameter as the input generators see it: its type's name, its kind, and for an enum every defined value as its
/// underlying integer in decimal.
/// </summary>
public sealed record ExecutionParameter(string TypeName, ExecutionTypeKind Kind, IReadOnlyList<string> EnumValues);
