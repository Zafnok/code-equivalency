namespace Equiv.Cli.Tests;

/// <summary>A real, empty file on disk for the duration of a test (<see cref="CompareCommand.Run"/> checks <see cref="File.Exists(string)"/>).</summary>
internal sealed class TempFile : IDisposable
{
    public TempFile() => Path = System.IO.Path.GetTempFileName();

    public string Path { get; }

    public void Dispose() => File.Delete(Path);
}
