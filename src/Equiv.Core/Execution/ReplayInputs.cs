namespace Equiv.Core.Execution;

/// <summary>
/// How each side's driver built its receiver and each argument (ADR 0062 decision 5; ticket P2-245), in call order: the
/// receiver first when it is an input, then the method's parameters. A replay result carries it as
/// <c>properties.replayInputs</c> and a <c>probe</c> result as <c>inputs</c>, so that an audit can tell a difference on a
/// state the model chose from one a constructor reached.
/// </summary>
public sealed record ReplayInputs(IReadOnlyList<ReplayInput> Legacy, IReadOnlyList<ReplayInput> Modern);
