namespace Equiv.Core;

/// <summary>No answer: the solver gave up, ran out of its limits, or could not read the script. <see cref="Reason"/> says which.</summary>
public sealed record SmtUnknown(string Reason) : SmtAnswer;
