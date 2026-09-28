using System.Collections.Immutable;

using Equiv.Core.Ir;

using Microsoft.Z3;

using HeapMap = Equiv.Verify.Z3.TraceEncoder.HeapMap;

namespace Equiv.Verify.Z3.Contracts;

/// <summary>What a <see cref="ContractConjunct"/> relates.</summary>
internal enum ConjunctKind
{
    /// <summary>Both sides throw, or neither does.</summary>
    Threw,

    /// <summary>When both throw, they throw the same exception type. A caller cannot tell the types apart, so it is never dropped.</summary>
    ExceptionType,

    /// <summary>Both make the same calls. A caller does not see its callee's calls, so it is never dropped.</summary>
    Calls,

    /// <summary>Both leave heap map <see cref="ContractConjunct.Map"/> the same.</summary>
    Heap,

    /// <summary>Unless either throws, <see cref="ContractConjunct.Predicate"/> has the same value on both sides.</summary>
    Predicate,
}
