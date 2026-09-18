namespace Equiv.Core.Configuration;

/// <summary>Validation rule ids for <see cref="EquivConfigLoader"/>; one rule per id.</summary>
public static class EquivConfigDiagnosticIds
{
    /// <summary>The document's root is not a JSON object.</summary>
    public const string InvalidRoot = "CFG001";

    /// <summary>"bound" is present but not a positive integer.</summary>
    public const string InvalidBound = "CFG002";

    /// <summary>"timeoutMs" is present but not a positive integer.</summary>
    public const string InvalidTimeout = "CFG003";

    /// <summary>A rename map is not an object of non-empty string to non-empty string.</summary>
    public const string InvalidRenameEntry = "CFG004";

    /// <summary>A top-level property is not one this schema defines.</summary>
    public const string UnknownProperty = "CFG005";

    /// <summary>A rename map repeats a key; the JSON parser keeps only the last occurrence.</summary>
    public const string DuplicateRenameEntry = "CFG006";
}
