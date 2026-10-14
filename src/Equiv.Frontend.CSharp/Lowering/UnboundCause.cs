using Equiv.Core;

namespace Equiv.Frontend.CSharp.Lowering;

/// <summary>
/// One reason a method's bound code is erroneous (ADR 0029 decision 2): where it is, and the id of the compiler
/// diagnostic there, such as <c>CS0103</c>. <see cref="Diagnostic"/> is null for a cause no diagnostic in the method's own
/// declaration reports: an invalid operation or an error type whose diagnostic is elsewhere (ticket P2-172).
/// </summary>
internal readonly record struct UnboundCause(SourceSpan Span, string? Diagnostic);
