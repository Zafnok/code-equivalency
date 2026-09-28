using System.Globalization;

using Equiv.Core.Progress;

namespace Equiv.TestSupport;

/// <summary>An <see cref="IRunLog"/> that keeps every call, in order, as one short string (ticket M4-013).</summary>
public sealed class RecordingRunLog(bool isDebug = false) : IRunLog
{
    private readonly List<string> _events = [];

    public IReadOnlyList<string> Events => _events;

    public bool IsDebug { get; } = isDebug;

    public void Phase(string name, int total, long totalWeight, PhaseBound? bound = null) =>
        _events.Add(string.Create(CultureInfo.InvariantCulture, $"phase {name} {total} {totalWeight}"));

    public void Item(string identity, long weight) => _events.Add(string.Create(CultureInfo.InvariantCulture, $"item {identity} {weight}"));

    public void ItemDone(string outcome) => _events.Add($"done {outcome}");

    public void Detail(string text) => _events.Add($"detail {text}");

    public void PhaseDone() => _events.Add("phase-done");
}
