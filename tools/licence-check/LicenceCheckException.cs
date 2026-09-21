namespace LicenceCheck;

/// <summary>Anything that stops the gate from running at all: a malformed policy, a package whose nuspec cannot be found.</summary>
internal sealed class LicenceCheckException(string message) : Exception(message);
