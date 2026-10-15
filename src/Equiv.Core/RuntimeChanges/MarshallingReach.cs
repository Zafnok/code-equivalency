namespace Equiv.Core.RuntimeChanges;

/// <summary>
/// Which <c>[DllImport]</c> declarations a <see cref="MarshallingChange"/> touches, read off the declaration alone
/// (ADR 0058 decision 3). The frontend decides whether a declaration is one.
/// </summary>
public enum MarshallingReach
{
    /// <summary>None: the change is not to the runtime's marshalling of a <c>[DllImport]</c>.</summary>
    None,

    /// <summary>Every <c>[DllImport]</c>: the change is to how a native library is found or to the native code it runs.</summary>
    EveryImport,

    /// <summary>A <c>[DllImport]</c> whose <c>ExactSpelling</c> is false, whose entry point the runtime may probe for with a suffix.</summary>
    InexactEntryPoint,

    /// <summary>A <c>[DllImport]</c> whose return value or a parameter is marshalled as a COM or WinRT object.</summary>
    ComObject,

    /// <summary>A <c>[DllImport]</c> whose search paths, its own or its assembly's, are given and leave out <c>AssemblyDirectory</c>.</summary>
    SearchPathsWithoutAssemblyDirectory,

    /// <summary>A <c>[DllImport]</c> whose search paths, its own or its assembly's, are <c>AssemblyDirectory</c> alone.</summary>
    AssemblyDirectoryOnly,
}
