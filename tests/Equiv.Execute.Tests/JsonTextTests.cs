using Xunit;

namespace Equiv.Execute.Tests;

public sealed class JsonTextTests
{
    [Theory]
    [InlineData(null, "null")]
    [InlineData("", "\"\"")]
    [InlineData("a \"q\" \\ ~", "\"a \\\"q\\\" \\\\ ~\"")]
    [InlineData("\r\n\u0000\u007F\u0130", "\"\\u000D\\u000A\\u0000\\u007F\\u0130\"")]
    public void StringsAreAsciiJson(string? value, string json) => Assert.Equal(json, JsonText.String(value));

    // A lone surrogate cannot sit in an attribute argument: it is stored as UTF-8 and comes back as U+FFFD.
    [Fact]
    public void ALoneSurrogateIsEscaped() => Assert.Equal("\"\\uD800x\"", JsonText.String("\uD800x"));
}
