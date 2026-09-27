using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Equiv.Verify.Z3.Ladder;

/// <summary>
/// The production <see cref="IInvariantProposer"/> (ticket P1-002): one call to the Claude Messages API over plain
/// <see cref="HttpClient"/>, with the model <c>--invariant-model</c> names and the key in <c>ANTHROPIC_API_KEY</c>. The
/// prompt is the embedded <c>Prompts/invariant.txt</c> filled with the request's IR text, relations and rejected
/// candidates; nothing but IR text leaves the machine. The answer's text is the candidate, and <c>none</c> or no text
/// gives up. An HTTP failure throws, so the pair gets an error notification rather than a quiet Unknown.
/// </summary>
internal sealed class AnthropicInvariantProposer(HttpClient http, string model, string? apiKey) : IInvariantProposer
{
    public const string ApiKeyVariable = "ANTHROPIC_API_KEY";

    public static readonly Uri Endpoint = new("https://api.anthropic.com/v1/messages");

    private const string ResourceName = "Equiv.Verify.Z3.Ladder.Prompts.invariant.txt";

    private static readonly HttpClient Shared = new() { Timeout = TimeSpan.FromMinutes(10) };

    private static readonly Lazy<string> Template = new(static () =>
    {
        using Stream stream = typeof(AnthropicInvariantProposer).Assembly.GetManifestResourceStream(ResourceName)!;
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    });

    /// <summary>A proposer asking <paramref name="model"/> with the key in the environment, over one shared client.</summary>
    public static AnthropicInvariantProposer FromEnvironment(string model) => new(Shared, model, Environment.GetEnvironmentVariable(ApiKeyVariable));

    /// <summary>The prompt for <paramref name="request"/>: the template with its four slots filled.</summary>
    public static string Prompt(InvariantRequest request) => Template.Value
        .Replace("{{old}}", request.OldIr, StringComparison.Ordinal)
        .Replace("{{new}}", request.NewIr, StringComparison.Ordinal)
        .Replace("{{relations}}", string.Join('\n', request.Relations.Select(static r => $"({r.Name} {string.Join(' ', r.Parameters.Select(static p => $"({p.Name} {p.Sort})"))})")), StringComparison.Ordinal)
        .Replace("{{rejected}}", request.Rejected.IsEmpty ? "(none)" : string.Join("\n\n", request.Rejected.Select(static r => $"{r.Candidate}\nrejected: {r.Reason}")), StringComparison.Ordinal);

    public async Task<string?> ProposeAsync(InvariantRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string key = apiKey ?? throw new InvalidOperationException($"--invariant-model needs the {ApiKeyVariable} environment variable.");
        JsonObject body = new()
        {
            ["model"] = model,
            ["max_tokens"] = 16000,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = Prompt(request) }),
        };
        using HttpRequestMessage message = new(HttpMethod.Post, Endpoint) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
        message.Headers.Add("x-api-key", key);
        message.Headers.Add("anthropic-version", "2023-06-01");
        using HttpResponseMessage response = await http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using JsonDocument reply = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        string text = string.Concat(reply.RootElement.GetProperty("content").EnumerateArray()
            .Where(static b => b.GetProperty("type").ValueEquals("text"))
            .Select(static b => b.GetProperty("text").GetString())).Trim();
        return text.Length == 0 || string.Equals(text, "none", StringComparison.Ordinal) ? null : text;
    }
}
