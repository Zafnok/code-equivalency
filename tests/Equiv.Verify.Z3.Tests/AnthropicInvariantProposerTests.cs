using System.Net;
using System.Text;
using System.Text.Json;

using Equiv.Verify.Z3.Ladder;

using Xunit;

namespace Equiv.Verify.Z3.Tests;

/// <summary>
/// Ticket P1-002 criterion 3: the production proposer's one Messages API call, against an HTTP handler that answers
/// from a script, so nothing reaches the network.
/// </summary>
public sealed class AnthropicInvariantProposerTests
{
    private static readonly InvariantRequest Request = new(
        "proc old-ir",
        "proc new-ir",
        [new("inv.B1.B1", [new("in.n", "Int"), new("old.i", "Int")])],
        [new("(define-fun inv.B1.B1 ((in.n Int) (old.i Int)) Bool true)", "the exit obligation fails: ...")]);

    [Fact]
    public async Task AnthropicProposer_BuildsRequestAndParsesResponse()
    {
        using Handler handler = new(HttpStatusCode.OK, """{"content":[{"type":"thinking","thinking":""},{"type":"text","text":"\n(define-fun inv.B1.B1 "},{"type":"text","text":"((in.n Int) (old.i Int)) Bool true)\n"}],"stop_reason":"end_turn"}""");
        using HttpClient http = new(handler);

        string? candidate = await new AnthropicInvariantProposer(http, "claude-test", "sk-test").ProposeAsync(Request, TestContext.Current.CancellationToken);

        Assert.Equal("(define-fun inv.B1.B1 ((in.n Int) (old.i Int)) Bool true)", candidate);
        HttpRequestMessage sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal(new Uri("https://api.anthropic.com/v1/messages"), sent.RequestUri);
        Assert.Equal("sk-test", Assert.Single(sent.Headers.GetValues("x-api-key")));
        Assert.Equal("2023-06-01", Assert.Single(sent.Headers.GetValues("anthropic-version")));
        using JsonDocument body = JsonDocument.Parse(handler.Bodies[0]);
        Assert.Equal("claude-test", body.RootElement.GetProperty("model").GetString());
        Assert.Equal(16000, body.RootElement.GetProperty("max_tokens").GetInt32());
        JsonElement message = Assert.Single(body.RootElement.GetProperty("messages").EnumerateArray());
        Assert.Equal("user", message.GetProperty("role").GetString());
        Assert.Equal(AnthropicInvariantProposer.Prompt(Request), message.GetProperty("content").GetString());
    }

    [Theory]
    [InlineData("""{"content":[{"type":"text","text":" none\n"}]}""")]
    [InlineData("""{"content":[]}""")]
    public async Task AnthropicProposer_GivesUpOnNoneOrNoText(string reply)
    {
        using Handler handler = new(HttpStatusCode.OK, reply);
        using HttpClient http = new(handler);

        Assert.Null(await new AnthropicInvariantProposer(http, "claude-test", "sk-test").ProposeAsync(Request, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnthropicProposer_ThrowsOnAnHttpFailure()
    {
        using Handler handler = new(HttpStatusCode.Unauthorized, """{"type":"error"}""");
        using HttpClient http = new(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => new AnthropicInvariantProposer(http, "claude-test", "sk-test").ProposeAsync(Request, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnthropicProposer_NeedsAnApiKey()
    {
        using Handler handler = new(HttpStatusCode.OK, "{}");
        using HttpClient http = new(handler);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => new AnthropicInvariantProposer(http, "claude-test", apiKey: null).ProposeAsync(Request, TestContext.Current.CancellationToken));

        Assert.Equal("--invariant-model needs the ANTHROPIC_API_KEY environment variable.", exception.Message);
        Assert.Empty(handler.Requests);
    }

    /// <summary>The prompt is the embedded template: it holds both IR texts, every relation with its sorts, and each rejection with its reason.</summary>
    [Fact]
    public void ThePromptHoldsTheIrTheRelationsAndTheRejections()
    {
        string prompt = AnthropicInvariantProposer.Prompt(Request);

        Assert.Contains("Old procedure:\nproc old-ir\n", prompt.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains("New procedure:\nproc new-ir\n", prompt.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains("(inv.B1.B1 (in.n Int) (old.i Int))", prompt, StringComparison.Ordinal);
        Assert.Contains("(define-fun inv.B1.B1 ((in.n Int) (old.i Int)) Bool true)\nrejected: the exit obligation fails: ...", prompt, StringComparison.Ordinal);
        Assert.Contains("SMT-LIB only", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("{{", prompt, StringComparison.Ordinal);
        Assert.Contains("(none)", AnthropicInvariantProposer.Prompt(Request with { Rejected = [] }), StringComparison.Ordinal);
    }

    [Fact]
    public void FromEnvironmentBuildsAProposerWithoutCallingAnything()
    {
        Assert.IsType<AnthropicInvariantProposer>(AnthropicInvariantProposer.FromEnvironment("claude-test"));
    }

    /// <summary>Answers every request with one status and body, keeping each request and its body.</summary>
    private sealed class Handler(HttpStatusCode status, string reply) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            return new HttpResponseMessage(status) { Content = new StringContent(reply, Encoding.UTF8, "application/json") };
        }
    }
}
