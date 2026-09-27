using Equiv.Verify.Z3.Ladder;

namespace Equiv.Verify.Z3.Tests;

/// <summary>A scripted <see cref="IInvariantProposer"/> (ticket P1-002): it answers in order, then gives up, and keeps every request.</summary>
internal sealed class FakeInvariantProposer(params string?[] answers) : IInvariantProposer
{
    private readonly Queue<string?> answers = new(answers);

    public List<InvariantRequest> Requests { get; } = [];

    public Task<string?> ProposeAsync(InvariantRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(answers.TryDequeue(out string? answer) ? answer : null);
    }
}
