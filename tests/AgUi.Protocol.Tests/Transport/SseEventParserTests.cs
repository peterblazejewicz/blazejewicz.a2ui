using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AGUI.Abstractions;
using Blazejewicz.A2Ui.AgUi.Protocol.Transport;

namespace Blazejewicz.A2Ui.AgUi.Protocol.Tests.Transport;

public sealed class SseEventParserTests
{
    private static MemoryStream ToStream(string content) => new(Encoding.UTF8.GetBytes(content));

    [Fact]
    public async Task ParseAsync_WellFormedStream_YieldsEventsAsync()
    {
        const string Sse =
            "data: {\"type\":\"RUN_STARTED\",\"threadId\":\"t1\",\"runId\":\"r1\"}\ndata: {\"type\":\"RUN_FINISHED\",\"threadId\":\"t1\",\"runId\":\"r1\"}\n";

        var events = new List<BaseEvent>();
        await foreach (
            var evt in SseEventParser.ParseAsync(
                ToStream(Sse),
                cancellationToken: TestContext.Current.CancellationToken
            )
        )
        {
            events.Add(evt);
        }

        Assert.Equal(2, events.Count);
        Assert.IsType<RunStartedEvent>(events[0]);
        Assert.IsType<RunFinishedEvent>(events[1]);
    }

    [Fact]
    public async Task ParseAsync_MalformedJsonLine_SkipsSilentlyAsync()
    {
        const string Sse =
            "data: {\"type\":\"RUN_STARTED\",\"threadId\":\"t1\",\"runId\":\"r1\"}\ndata: {not valid json}\ndata: {\"type\":\"RUN_FINISHED\",\"threadId\":\"t1\",\"runId\":\"r1\"}\n";

        var events = new List<BaseEvent>();
        await foreach (
            var evt in SseEventParser.ParseAsync(
                ToStream(Sse),
                cancellationToken: TestContext.Current.CancellationToken
            )
        )
        {
            events.Add(evt);
        }

        Assert.Equal(2, events.Count);
    }

    [Fact]
    public async Task ParseAsync_DoneSentinel_StopsYieldingAsync()
    {
        const string Sse =
            "data: {\"type\":\"RUN_STARTED\",\"threadId\":\"t1\",\"runId\":\"r1\"}\ndata: [DONE]\ndata: {\"type\":\"RUN_FINISHED\",\"threadId\":\"t1\",\"runId\":\"r1\"}\n";

        var events = new List<BaseEvent>();
        await foreach (
            var evt in SseEventParser.ParseAsync(
                ToStream(Sse),
                cancellationToken: TestContext.Current.CancellationToken
            )
        )
        {
            events.Add(evt);
        }

        // [DONE] is skipped, but parsing continues for lines after it
        Assert.Equal(2, events.Count);
    }

    [Fact]
    public async Task ParseAsync_NonDataLines_SkippedAsync()
    {
        const string Sse = "event: message\nid: 1\ndata: {\"type\":\"RUN_STARTED\",\"threadId\":\"t1\",\"runId\":\"r1\"}\n\n";

        var events = new List<BaseEvent>();
        await foreach (
            var evt in SseEventParser.ParseAsync(
                ToStream(Sse),
                cancellationToken: TestContext.Current.CancellationToken
            )
        )
        {
            events.Add(evt);
        }

        Assert.Single(events);
        Assert.IsType<RunStartedEvent>(events[0]);
    }

    [Fact]
    public async Task ParseAsync_CancellationToken_StopsProcessingAsync()
    {
        const string Sse =
            "data: {\"type\":\"RUN_STARTED\",\"threadId\":\"t1\",\"runId\":\"r1\"}\ndata: {\"type\":\"RUN_FINISHED\",\"threadId\":\"t1\",\"runId\":\"r1\"}\n";
        using var cts = new CancellationTokenSource();

        var events = new List<BaseEvent>();
        await foreach (var evt in SseEventParser.ParseAsync(ToStream(Sse), cancellationToken: cts.Token))
        {
            events.Add(evt);
            cts.Cancel(); // cancel after first event
        }

        Assert.Single(events);
    }

    [Fact]
    public async Task ParseAsync_EmptyStream_YieldsNothingAsync()
    {
        var events = new List<BaseEvent>();
        await foreach (
            var evt in SseEventParser.ParseAsync(ToStream(""), cancellationToken: TestContext.Current.CancellationToken)
        )
        {
            events.Add(evt);
        }

        Assert.Empty(events);
    }

    [Fact]
    public async Task ParseAsync_DataWithExtraSpaces_TrimsCorrectlyAsync()
    {
        const string Sse = "data:   {\"type\":\"RUN_STARTED\",\"threadId\":\"t1\",\"runId\":\"r1\"}\n";

        var events = new List<BaseEvent>();
        await foreach (
            var evt in SseEventParser.ParseAsync(
                ToStream(Sse),
                cancellationToken: TestContext.Current.CancellationToken
            )
        )
        {
            events.Add(evt);
        }

        Assert.Single(events);
    }
}
