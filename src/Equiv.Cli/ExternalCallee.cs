namespace Equiv.Cli;

/// <summary>One external callee's identity and how many call sites in matched pairs' lowered bodies reach it (ADR 0035; ticket M3-033).</summary>
internal sealed record ExternalCallee(string Member, int CallSites);
