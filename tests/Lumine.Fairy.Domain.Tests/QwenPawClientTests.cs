using System.Net;
using System.Text;
using Lumine.Fairy.Infrastructure;
using Lumine.Fairy.Domain;

namespace Lumine.Fairy.Domain.Tests;

public sealed class QwenPawClientTests
{
    [Fact]
    public async Task ChatRetriesConflictAndReturnsTheSubsequentCompletedResponse()
    {
        using var handler = new SequenceHandler(
            JsonResponse(HttpStatusCode.OK, "{\"token\":\"token\",\"username\":\"tester\"}"),
            JsonResponse(HttpStatusCode.Conflict, "{\"detail\":\"A task is already running for this chat.\"}"),
            SseResponse("{\"decision\":\"no_action\",\"todos\":[],\"intents\":[],\"questions\":[],\"sourceRefs\":[]}"));
        using var http = new HttpClient(handler);
        using var client = new QwenPawClient(new FakeProtectedTokenStore(), http);

        await client.LoginAsync("http://qwenpaw.test", "tester", "password");
        var result = await client.ChatAsync("default", "分析这条消息", "round-1");

        Assert.Equal(3, handler.RequestCount);
        Assert.NotNull(result.Analysis);
        Assert.Equal("no_action", result.Analysis!.Decision);
    }

    [Fact]
    public async Task ChatIncludesTheServerDetailForNonSuccessResponses()
    {
        using var handler = new SequenceHandler(
            JsonResponse(HttpStatusCode.OK, "{\"token\":\"token\",\"username\":\"tester\"}"),
            JsonResponse(HttpStatusCode.BadRequest, "{\"detail\":\"invalid chat payload\"}"));
        using var http = new HttpClient(handler);
        using var client = new QwenPawClient(new FakeProtectedTokenStore(), http);

        await client.LoginAsync("http://qwenpaw.test", "tester", "password");
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.ChatAsync("default", "分析这条消息", "round-1"));

        Assert.Contains("invalid chat payload", exception.Message);
        Assert.Equal(2, handler.RequestCount);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task ChatJoinsMultilineDataInCompletedFrame(string newline)
    {
        var body = string.Join(newline,
            ": keepalive",
            "event: response",
            "data: {\"object\":\"response\",\"status\":\"completed\",",
            "data: \"session_id\":\"round-1\",\"output\":[",
            "data: {\"type\":\"message\",\"role\":\"assistant\",\"content\":[",
            "data: {\"type\":\"output_text\",\"text\":\"{\\\"decision\\\":\\\"no_action\\\",\\\"todos\\\":[],\\\"intents\\\":[],\\\"questions\\\":[],\\\"sourceRefs\\\":[]}\"}",
            "data: ]}]}",
            "", "");
        using var handler = new SequenceHandler(
            JsonResponse(HttpStatusCode.OK, "{\"token\":\"token\",\"username\":\"tester\"}"),
            RawSseResponse(body));
        using var http = new HttpClient(handler);
        using var client = new QwenPawClient(new FakeProtectedTokenStore(), http);

        await client.LoginAsync("http://qwenpaw.test", "tester", "password");
        var result = await client.ChatAsync("default", "分析这条消息", "round-1");

        Assert.NotNull(result.Analysis);
        Assert.Equal("no_action", result.Analysis!.Decision);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task ChatRejectsPartialContentAtEofWithoutCompletedResponse()
    {
        const string body = "data: {\"object\":\"message\",\"type\":\"message\",\"role\":\"assistant\",\"id\":\"msg-1\"}\n\n"
            + "data: {\"object\":\"content\",\"msg_id\":\"msg-1\",\"type\":\"output_text\",\"text\":\"partial answer\"}\n\n";
        using var handler = new SequenceHandler(
            JsonResponse(HttpStatusCode.OK, "{\"token\":\"token\",\"username\":\"tester\"}"),
            RawSseResponse(body));
        using var http = new HttpClient(handler);
        using var client = new QwenPawClient(new FakeProtectedTokenStore(), http);

        await client.LoginAsync("http://qwenpaw.test", "tester", "password");
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.ChatAsync("default", "分析这条消息", "round-1"));

        Assert.Contains("completed", exception.Message);
    }

    [Fact]
    public async Task ChatReportsModelTimeoutFromFailedResponseBeforeFirstContent()
    {
        const string body = "data: {\"object\":\"response\",\"status\":\"failed\",\"error\":{\"code\":\"MODEL_TIMEOUT\",\"message\":\"model request timed out\"}}\n\n";
        using var handler = new SequenceHandler(
            JsonResponse(HttpStatusCode.OK, "{\"token\":\"token\",\"username\":\"tester\"}"),
            RawSseResponse(body));
        using var http = new HttpClient(handler);
        using var client = new QwenPawClient(new FakeProtectedTokenStore(), http);

        await client.LoginAsync("http://qwenpaw.test", "tester", "password");
        var exception = await Assert.ThrowsAsync<TimeoutException>(
            () => client.ChatAsync("default", "分析这条消息", "round-1"));

        Assert.Contains("模型", exception.Message);
        Assert.Contains("超时", exception.Message);
        Assert.DoesNotContain("首个有效内容", exception.Message);
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("error")]
    [InlineData("cancelled")]
    [InlineData("canceled")]
    [InlineData("incomplete")]
    public async Task ChatRejectsFailedResponseBeforeFirstContent(string status)
    {
        var body = $"data: {{\"object\":\"response\",\"status\":\"{status}\"}}\n\n";
        using var handler = new SequenceHandler(
            JsonResponse(HttpStatusCode.OK, "{\"token\":\"token\",\"username\":\"tester\"}"),
            RawSseResponse(body));
        using var http = new HttpClient(handler);
        using var client = new QwenPawClient(new FakeProtectedTokenStore(), http);

        await client.LoginAsync("http://qwenpaw.test", "tester", "password");
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.ChatAsync("default", "分析这条消息", "round-1"));

        Assert.DoesNotContain("首个有效内容", exception.Message);
    }

    [Theory]
    [InlineData("event: error\ndata: upstream unavailable\n\n")]
    [InlineData("data: {\"object\":\"error\",\"message\":\"upstream unavailable\"}\n\n")]
    [InlineData("data: {\"type\":\"error\",\"message\":\"upstream unavailable\"}\n\n")]
    [InlineData("data: {\"error\":{\"code\":\"MODEL_ERROR\",\"message\":\"upstream unavailable\"}}\n\n")]
    public async Task ChatReportsErrorTerminalBeforeFirstContentTimeout(string body)
    {
        using var handler = new SequenceHandler(
            JsonResponse(HttpStatusCode.OK, "{\"token\":\"token\",\"username\":\"tester\"}"),
            RawSseResponse(body));
        using var http = new HttpClient(handler);
        using var client = new QwenPawClient(new FakeProtectedTokenStore(), http);

        await client.LoginAsync("http://qwenpaw.test", "tester", "password");
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.ChatAsync("default", "分析这条消息", "round-1"));

        Assert.DoesNotContain("首个有效内容", exception.Message);
    }

    [Theory]
    [InlineData(": heartbeat\n\n")]
    [InlineData("event: heartbeat\ndata: {\"object\":\"heartbeat\"}\n\n")]
    [InlineData("data: {\"object\":\"message\",\"type\":\"heartbeat\",\"status\":null,\"data\":null}\n\n")]
    [InlineData("data: {\"object\":\"response\",\"status\":\"in_progress\"}\n\n")]
    public async Task ChatTreatsHeartbeatOnlyEofAsMissingFirstContent(string body)
    {
        using var handler = new SequenceHandler(
            JsonResponse(HttpStatusCode.OK, "{\"token\":\"token\",\"username\":\"tester\"}"),
            RawSseResponse(body + body));
        using var http = new HttpClient(handler);
        using var client = new QwenPawClient(new FakeProtectedTokenStore(), http);

        await client.LoginAsync("http://qwenpaw.test", "tester", "password");
        var exception = await Assert.ThrowsAsync<TimeoutException>(
            () => client.ChatAsync("default", "分析这条消息", "round-1"));

        Assert.Contains("首个有效内容", exception.Message);
    }

    private static HttpResponseMessage RawSseResponse(string body)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/event-stream"),
        };

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string body)
        => new(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    private static HttpResponseMessage SseResponse(string assistantText)
    {
        var body = $"data: {{\"object\":\"response\",\"status\":\"completed\",\"session_id\":\"round-1\",\"output\":[{{\"type\":\"message\",\"role\":\"assistant\",\"content\":[{{\"type\":\"output_text\",\"text\":{System.Text.Json.JsonSerializer.Serialize(assistantText)}}}]}}]}}\n\n";
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/event-stream"),
        };
    }

    private sealed class SequenceHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private sealed class FakeProtectedTokenStore : IProtectedTokenStore
    {
        public Task SaveAsync(string token, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<string?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

        public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
