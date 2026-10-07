using System.Collections.Immutable;

using Equiv.Core.Ir;

namespace Equiv.Core.Matching;

/// <summary>
/// A matched pair's bodies lowered from IL, kept beside the IOperation ones in thorough mode so the IL pass can verify the
/// pair again from them (ADR 0049 decision 2; ADR 0039; ticket P1-032). <see cref="ForwardersResolved"/> are the
/// forwarders these bodies' calls were resolved through (ADR 0047), which a result decided from them reports.
/// </summary>
public sealed record IlBodies(IrProcedure Old, IrProcedure New, ImmutableArray<ResolvedForwarder> ForwardersResolved);
