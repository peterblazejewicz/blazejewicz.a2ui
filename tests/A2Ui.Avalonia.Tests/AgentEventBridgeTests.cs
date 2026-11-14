using AGUI.Abstractions;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Blazejewicz.A2Ui.Core.Bindings;
using Blazejewicz.A2Ui.Core.Surfaces;
using Xunit;

namespace Blazejewicz.A2Ui.Avalonia.Tests;

public sealed class AgentEventBridgeTests
{
    /// <summary>
    /// Deterministically flushes the background processing channel and pumps the
    /// Avalonia UI dispatcher so posted lambdas execute on the test thread.
    /// </summary>
    private static async Task DrainAndPumpAsync(AgentEventBridge bridge)
    {
        await bridge.FlushAsync().ConfigureAwait(true);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void OnUserAction_ForwardsToUserActionReceived()
    {
        var sm = new SurfaceManager();
        using var bridge = new AgentEventBridge(sm);

        UserActionEventArgs? received = null;
        bridge.UserActionReceived += (_, e) => received = e;

        var args = new UserActionEventArgs("surface-1", "click", "payload", "btn-1");
        bridge.OnUserAction(this, args);

        Assert.NotNull(received);
        Assert.Equal("surface-1", received.SurfaceId);
        Assert.Equal("click", received.EventName);
        Assert.Equal("btn-1", received.ComponentId);
    }

    [AvaloniaFact]
    public void OnUserAction_SubscriberThrows_DoesNotPropagate()
    {
        var sm = new SurfaceManager();
        using var bridge = new AgentEventBridge(sm);

        bridge.UserActionReceived += (_, _) => throw new InvalidOperationException("boom");

        var args = new UserActionEventArgs("surface-1", "click", null);

        // Should not throw despite subscriber throwing
        bridge.OnUserAction(this, args);
    }

    [AvaloniaFact]
    public async Task ProcessLoop_NonA2UiToolCall_IgnoredAsync()
    {
        var sm = new SurfaceManager();
        using var bridge = new AgentEventBridge(sm);
        bool surfaceCreated = false;
        sm.SurfaceCreated += (_, _) => surfaceCreated = true;

        bridge.Start();

        // Tool call with a non-A2UI name ("search") should be ignored
        await bridge
            .WriteEventAsync(new ToolCallStartEvent { ToolCallId = "tc-1", ToolCallName = "search" })
            .ConfigureAwait(true);
        await bridge
            .WriteEventAsync(new ToolCallArgsEvent { ToolCallId = "tc-1", Delta = """{"query":"hello"}""" })
            .ConfigureAwait(true);
        await bridge.WriteEventAsync(new ToolCallEndEvent { ToolCallId = "tc-1" }).ConfigureAwait(true);

        await DrainAndPumpAsync(bridge).ConfigureAwait(true);
        bridge.Stop();

        Assert.False(surfaceCreated);
    }

    [AvaloniaFact]
    public async Task ProcessLoop_RunStartedAndFinished_FiresEventsAsync()
    {
        var sm = new SurfaceManager();
        using var bridge = new AgentEventBridge(sm);

        bool started = false;
        bool finished = false;
        bridge.RunStarted += (_, _) => started = true;
        bridge.RunFinished += (_, _) => finished = true;

        bridge.Start();

        await bridge.WriteEventAsync(new RunStartedEvent { ThreadId = "t-1", RunId = "r-1" }).ConfigureAwait(true);
        await bridge.WriteEventAsync(new RunFinishedEvent { ThreadId = "t-1", RunId = "r-1" }).ConfigureAwait(true);

        await DrainAndPumpAsync(bridge).ConfigureAwait(true);
        bridge.Stop();

        Assert.True(started);
        Assert.True(finished);
    }

    [AvaloniaFact]
    public async Task ProcessLoop_RunError_FiresErrorEventAsync()
    {
        var sm = new SurfaceManager();
        using var bridge = new AgentEventBridge(sm);

        string? errorMessage = null;
        bridge.RunError += (_, msg) => errorMessage = msg;

        bridge.Start();

        await bridge.WriteEventAsync(new RunErrorEvent { Message = "something went wrong" }).ConfigureAwait(true);

        await DrainAndPumpAsync(bridge).ConfigureAwait(true);
        bridge.Stop();

        Assert.Equal("something went wrong", errorMessage);
    }

    [AvaloniaFact]
    public async Task ProcessLoop_TextMessageContent_FiresAgentTextDeltaAsync()
    {
        var sm = new SurfaceManager();
        using var bridge = new AgentEventBridge(sm);

        string? delta = null;
        bridge.AgentTextDelta += (_, d) => delta = d;

        bridge.Start();

        await bridge
            .WriteEventAsync(new TextMessageContentEvent { MessageId = "msg-1", Delta = "Hello, world!" })
            .ConfigureAwait(true);

        await DrainAndPumpAsync(bridge).ConfigureAwait(true);
        bridge.Stop();

        Assert.Equal("Hello, world!", delta);
    }

    // ── End-to-end tool call → surface processing tests ──

    [AvaloniaFact]
    public async Task ProcessLoop_ValidRenderUiToolCall_CreatesSurfaceAsync()
    {
        var sm = new SurfaceManager();
        using var bridge = new AgentEventBridge(sm);

        bool surfaceCreated = false;
        sm.SurfaceCreated += (_, _) => surfaceCreated = true;

        bridge.Start();

        // Simulate a complete render_ui tool call with a createSurface JSONL payload
        await bridge
            .WriteEventAsync(new ToolCallStartEvent { ToolCallId = "tc-e2e", ToolCallName = "render_ui" })
            .ConfigureAwait(true);
        await bridge
            .WriteEventAsync(
                new ToolCallArgsEvent
                {
                    ToolCallId = "tc-e2e",
                    Delta = """{"version":"v0.9","createSurface":{"surfaceId":"e2e-test","catalogId":"basic"}}""",
                }
            )
            .ConfigureAwait(true);
        await bridge.WriteEventAsync(new ToolCallEndEvent { ToolCallId = "tc-e2e" }).ConfigureAwait(true);

        await DrainAndPumpAsync(bridge).ConfigureAwait(true);
        bridge.Stop();

        // the bridge should process valid render_ui tool calls
        Assert.True(surfaceCreated);
        Assert.NotNull(sm.GetSurface("e2e-test"));
        Assert.Equal("basic", sm.GetSurface("e2e-test")!.CatalogId);
    }

    [AvaloniaFact]
    public async Task ProcessLoop_ValidRenderUiToolCall_ProcessesUpdateComponentsAsync()
    {
        var sm = new SurfaceManager();
        using var bridge = new AgentEventBridge(sm);

        bridge.Start();

        // First tool call: createSurface
        await bridge
            .WriteEventAsync(new ToolCallStartEvent { ToolCallId = "tc-create", ToolCallName = "render_ui" })
            .ConfigureAwait(true);
        await bridge
            .WriteEventAsync(
                new ToolCallArgsEvent
                {
                    ToolCallId = "tc-create",
                    Delta = """{"version":"v0.9","createSurface":{"surfaceId":"e2e-uc","catalogId":"basic"}}""",
                }
            )
            .ConfigureAwait(true);
        await bridge.WriteEventAsync(new ToolCallEndEvent { ToolCallId = "tc-create" }).ConfigureAwait(true);

        await DrainAndPumpAsync(bridge).ConfigureAwait(true);

        // Second tool call: updateComponents (multi-line JSONL)
        const string UpdateJsonl =
            """{"version":"v0.9","updateComponents":{"surfaceId":"e2e-uc","components":[{"id":"root","component":"Column"},{"id":"t1","component":"Text"}]}}""";

        await bridge
            .WriteEventAsync(new ToolCallStartEvent { ToolCallId = "tc-update", ToolCallName = "render_ui" })
            .ConfigureAwait(true);
        await bridge
            .WriteEventAsync(new ToolCallArgsEvent { ToolCallId = "tc-update", Delta = UpdateJsonl })
            .ConfigureAwait(true);
        await bridge.WriteEventAsync(new ToolCallEndEvent { ToolCallId = "tc-update" }).ConfigureAwait(true);

        await DrainAndPumpAsync(bridge).ConfigureAwait(true);
        bridge.Stop();

        var surface = sm.GetSurface("e2e-uc");
        Assert.NotNull(surface);
        Assert.Equal(2, surface.Components.Count);
        Assert.Contains("root", surface.Components);
        Assert.Contains("t1", surface.Components);
    }

    [AvaloniaFact]
    public async Task ProcessLoop_InvalidToolCallPayload_SkipsAndContinuesAsync()
    {
        var sm = new SurfaceManager();
        using var bridge = new AgentEventBridge(sm);

        bridge.Start();

        // First tool call: invalid JSON payload
        await bridge
            .WriteEventAsync(new ToolCallStartEvent { ToolCallId = "tc-bad", ToolCallName = "render_ui" })
            .ConfigureAwait(true);
        await bridge
            .WriteEventAsync(new ToolCallArgsEvent { ToolCallId = "tc-bad", Delta = "not valid json {{{" })
            .ConfigureAwait(true);
        await bridge.WriteEventAsync(new ToolCallEndEvent { ToolCallId = "tc-bad" }).ConfigureAwait(true);

        await DrainAndPumpAsync(bridge).ConfigureAwait(true);

        // Second tool call: valid payload — bridge should have recovered
        await bridge
            .WriteEventAsync(new ToolCallStartEvent { ToolCallId = "tc-good", ToolCallName = "render_ui" })
            .ConfigureAwait(true);
        await bridge
            .WriteEventAsync(
                new ToolCallArgsEvent
                {
                    ToolCallId = "tc-good",
                    Delta = """{"version":"v0.9","createSurface":{"surfaceId":"recovered","catalogId":"basic"}}""",
                }
            )
            .ConfigureAwait(true);
        await bridge.WriteEventAsync(new ToolCallEndEvent { ToolCallId = "tc-good" }).ConfigureAwait(true);

        await DrainAndPumpAsync(bridge).ConfigureAwait(true);
        bridge.Stop();

        // bridge should recover from invalid payloads
        Assert.NotNull(sm.GetSurface("recovered"));
    }

    [AvaloniaFact]
    public async Task ProcessLoop_ValidV10RenderUiToolCall_CreatesSurfaceAndComponentsAsync()
    {
        var sm = new SurfaceManager();
        using var bridge = new AgentEventBridge(sm);

        bridge.Start();

        const string V10Jsonl =
            """{"version":"1.0","createSurface":{"surfaceId":"v10-bridge","catalogId":"https://a2ui.org/specification/v1_0/catalogs/basic/catalog.json","dataModel":{"greeting":"Hello v1.0"},"components":[{"id":"root","component":"Text","text":{"@path":"/greeting"}}]}}""";

        await bridge
            .WriteEventAsync(new ToolCallStartEvent { ToolCallId = "tc-v10", ToolCallName = "render_ui" })
            .ConfigureAwait(true);
        await bridge
            .WriteEventAsync(new ToolCallArgsEvent { ToolCallId = "tc-v10", Delta = V10Jsonl })
            .ConfigureAwait(true);
        await bridge.WriteEventAsync(new ToolCallEndEvent { ToolCallId = "tc-v10" }).ConfigureAwait(true);

        await DrainAndPumpAsync(bridge).ConfigureAwait(true);
        bridge.Stop();

        var surface = sm.GetSurface("v10-bridge");
        Assert.NotNull(surface);
        Assert.Equal("1.0", surface.ProtocolVersion);
        Assert.Single(surface.Components);
        Assert.Equal("Hello v1.0", surface.DataModel.Resolve(DynamicValue.FromPath("/greeting")));
    }

    [AvaloniaFact]
    public async Task FlushAsync_WhenNotStartedOrAfterDispose_ThrowsInvalidOperationExceptionAsync()
    {
        var sm = new SurfaceManager();
        var bridge = new AgentEventBridge(sm);

        await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.FlushAsync()).ConfigureAwait(true);

        bridge.Start();
        bridge.Dispose();

        await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.FlushAsync()).ConfigureAwait(true);
    }
}
