using System.Net;
using System.Text;
using System.Text.Json;
using Blazejewicz.A2Ui.Avalonia.Shell.Models;
using Blazejewicz.A2Ui.Avalonia.Shell.Services;
using Blazejewicz.A2Ui.Avalonia.Shell.ViewModels;
using Blazejewicz.A2Ui.Core;
using Blazejewicz.A2Ui.Core.Messages;
using Blazejewicz.A2Ui.Core.Surfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Blazejewicz.A2Ui.Avalonia.Tests;

public sealed class ShellParityTests
{
    [Fact]
    public void UserActionSerializer_V09AndV091_EmitsTypedClientToServerMessage()
    {
        var args = new UserActionEventArgs(
            SurfaceId: "booking",
            EventName: "book_restaurant",
            Payload: new { partySize = 4 },
            ComponentId: "btn_submit"
        );

        var v091Envelope = Assert.IsType<ClientToServerMessage>(UserActionSerializer.Serialize(args, A2UiProtocol.Version091));
        Assert.Equal("v0.9.1", v091Envelope.Version);
        Assert.NotNull(v091Envelope.Action);
        Assert.Equal("book_restaurant", v091Envelope.Action.Name);
        Assert.Equal("booking", v091Envelope.Action.SurfaceId);
        Assert.Equal("btn_submit", v091Envelope.Action.SourceComponentId);
        Assert.Equal(4, v091Envelope.Action.Context.GetProperty("partySize").GetInt32());

        var v09Envelope = Assert.IsType<ClientToServerMessage>(UserActionSerializer.Serialize(args, A2UiProtocol.Version09));
        Assert.Equal("v0.9", v09Envelope.Version);

        var v10Envelope = Assert.IsType<RendererToAgentMessage>(UserActionSerializer.Serialize(args, A2UiProtocol.Version10));
        Assert.Equal("1.0", v10Envelope.Version);
        Assert.Equal("book_restaurant", v10Envelope.Action?.Name);
        Assert.NotNull(v10Envelope.Action?.Context);
        Assert.Equal(4, v10Envelope.Action.Context["partySize"].GetInt32());
    }

    [Fact]
    public void UserActionSerializer_NullPayload_EmitsNonNullEmptyContextAndValidatesAcrossAllVersions()
    {
        var args = new UserActionEventArgs(
            SurfaceId: "product-card",
            EventName: "addToCart",
            Payload: null,
            ComponentId: "add_cart_btn"
        );

        var v10Envelope = Assert.IsType<RendererToAgentMessage>(
            UserActionSerializer.Serialize(args, A2UiProtocol.Version10)
        );
        v10Envelope.Validate();
        Assert.NotNull(v10Envelope.Action);
        Assert.NotNull(v10Envelope.Action.Context);
        Assert.Empty(v10Envelope.Action.Context);

        var v091Envelope = Assert.IsType<ClientToServerMessage>(
            UserActionSerializer.Serialize(args, A2UiProtocol.Version091)
        );
        v091Envelope.Validate();
        Assert.Equal(JsonValueKind.Object, v091Envelope.Action!.Context.ValueKind);
        Assert.Empty(v091Envelope.Action.Context.EnumerateObject());

        var v09Envelope = Assert.IsType<ClientToServerMessage>(
            UserActionSerializer.Serialize(args, A2UiProtocol.Version09)
        );
        v09Envelope.Validate();
        Assert.Equal(JsonValueKind.Object, v09Envelope.Action!.Context.ValueKind);
        Assert.Empty(v09Envelope.Action.Context.EnumerateObject());
    }

    [Fact]
    public async Task A2AAgentClient_AttachesCapabilitiesAndSendDataModel_AndPersistsContextIdAsync()
    {
        var manager = new SurfaceManager();
        manager.Process(
            """
            {
              "version": "1.0",
              "createSurface": {
                "surfaceId": "checkout",
                "catalogId": "https://a2ui.org/catalogs/basic/v1/catalog.json",
                "sendDataModel": true,
                "dataModel": { "cartTotal": 99 },
                "components": [{ "id": "root", "component": "Text", "text": "Total" }]
              }
            }
            """
        );

        var capturedRequests = new List<string>();
        using var handler = new StubHttpMessageHandler(async req =>
        {
            string body = await req.Content!.ReadAsStringAsync().ConfigureAwait(false);
            capturedRequests.Add(body);

            const string RpcReply = """
                {
                  "jsonrpc": "2.0",
                  "id": "1",
                  "result": {
                    "id": "task-1",
                    "contextId": "ctx-persisted-42",
                    "status": { "state": "completed" }
                  }
                }
                """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(RpcReply, Encoding.UTF8, "application/json"),
            };
        });

        using var http = new HttpClient(handler);
        var client = new A2AAgentClient(
            http,
            new AgentConfig("http://localhost:10002", "Test", "Ask..."),
            NullLogger<A2AAgentClient>.Instance,
            manager
        );

        _ = await client.SendTextRawAsync("Hello", TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.Equal("ctx-persisted-42", client.ContextId);

        _ = await client.SendTextRawAsync("Follow up", TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.Equal(2, capturedRequests.Count);

        using var firstDoc = JsonDocument.Parse(capturedRequests[0]);
        JsonElement firstMsg = firstDoc.RootElement.GetProperty("params").GetProperty("message");
        JsonElement metadata = firstMsg.GetProperty("metadata");
        Assert.True(metadata.TryGetProperty("a2uiRendererCapabilities", out JsonElement rendererCaps));
        Assert.True(rendererCaps.TryGetProperty("1.0", out JsonElement v10RendererCaps));
        Assert.False(rendererCaps.TryGetProperty("v1.0", out _));
        string?[] v10CatalogIds = [.. v10RendererCaps.GetProperty("supportedCatalogIds").EnumerateArray().Select(e => e.GetString())];
        Assert.Contains(A2UiProtocol.BasicCatalogIdV10, v10CatalogIds);
        Assert.Contains("https://a2ui.org/specification/v1_0/catalogs/basic/catalog.json", v10CatalogIds);

        Assert.True(metadata.TryGetProperty("a2uiClientCapabilities", out JsonElement clientCaps));
        Assert.True(clientCaps.TryGetProperty("v0.9", out JsonElement v09ClientCaps));
        Assert.False(clientCaps.TryGetProperty("1.0", out _));
        Assert.False(clientCaps.TryGetProperty("v1.0", out _));
        string?[] v09CatalogIds = [.. v09ClientCaps.GetProperty("supportedCatalogIds").EnumerateArray().Select(e => e.GetString())];
        Assert.Contains("https://a2ui.org/specification/v0_9/standard_catalog.json", v09CatalogIds);
        Assert.Contains("https://a2ui.org/specification/v0_9/basic_catalog.json", v09CatalogIds);
        Assert.Equal(
            99,
            metadata
                .GetProperty("a2uiRendererDataModel")
                .GetProperty("surfaces")
                .GetProperty("checkout")
                .GetProperty("cartTotal")
                .GetInt32()
        );

        using var secondDoc = JsonDocument.Parse(capturedRequests[1]);
        JsonElement secondMsg = secondDoc.RootElement.GetProperty("params").GetProperty("message");
        Assert.Equal("ctx-persisted-42", secondMsg.GetProperty("contextId").GetString());
    }

    [Fact]
    public async Task ShellViewModel_BatchErrorIsolation_AndMultiSurfacePreservationAsync()
    {
        var manager = new SurfaceManager();
        var stubClient = new StubA2AClient(
            textMessages:
            [
                """{"version":"1.0","createSurface":{"surfaceId":"s1","catalogId":"https://a2ui.org/catalogs/basic/v1/catalog.json","components":[{"id":"root","component":"Text","text":"One"}]}}""",
                """{"version":"1.0","invalidEnvelope":true}""",
                """{"version":"1.0","createSurface":{"surfaceId":"s2","catalogId":"https://a2ui.org/catalogs/basic/v1/catalog.json","components":[{"id":"root","component":"Text","text":"Two"}]}}""",
            ],
            actionMessages:
            [
                """{"version":"1.0","updateDataModel":{"surfaceId":"s1","path":"/updated","value":true}}""",
            ]
        );

        using var vm = new ShellViewModel(stubClient, manager, NullLogger<ShellViewModel>.Instance)
        {
            PromptText = "Show surfaces",
        };

        await vm.SendCommand.ExecuteAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        // Both valid surfaces rendered despite malformed message in middle of batch
        Assert.Equal(2, vm.Surfaces.Count);
        Assert.NotNull(vm.ErrorText);

        // Action dispatch preserves existing surfaces and applies incremental update
        await vm.HandleUserActionAsync(
                new UserActionEventArgs("s1", "click", null, "root"),
                TestContext.Current.CancellationToken
            )
            .ConfigureAwait(true);

        Assert.Equal(2, vm.Surfaces.Count);
        Assert.Equal("true", manager.GetSurface("s1")?.DataModel.Resolve(Core.Bindings.DynamicValue.FromPath("/updated")));
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => responder(request);
    }

    private sealed class StubA2AClient(IReadOnlyList<string> textMessages, IReadOnlyList<string> actionMessages)
        : IA2AClient
    {
        public Task<string> GetAgentNameAsync(CancellationToken ct = default) => Task.FromResult("Stub Agent");

        public Task<IReadOnlyList<A2UiMessage>> SendTextAsync(string text, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<A2UiMessage>>([]);

        public Task<IReadOnlyList<string>> SendTextRawAsync(string text, CancellationToken ct = default) =>
            Task.FromResult(textMessages);

        public Task<IReadOnlyList<A2UiMessage>> SendActionAsync(object userAction, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<A2UiMessage>>([]);

        public Task<IReadOnlyList<string>> SendActionRawAsync(object userAction, CancellationToken ct = default) =>
            Task.FromResult(actionMessages);
    }
}
