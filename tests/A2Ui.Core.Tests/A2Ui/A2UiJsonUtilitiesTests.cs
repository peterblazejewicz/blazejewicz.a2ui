using System.Text.Json;
using Blazejewicz.A2Ui.Core.Bindings;
using Blazejewicz.A2Ui.Core.Components;
using Blazejewicz.A2Ui.Core.Messages;

namespace Blazejewicz.A2Ui.Core.Tests.A2Ui;

public sealed class A2UiJsonUtilitiesTests
{
    [Fact]
    public void DefaultOptions_IsReadOnlyAndUsesSourceGeneratedResolver()
    {
        Assert.True(A2UiJsonUtilities.DefaultOptions.IsReadOnly);
        Assert.Same(A2UiJsonSerializerContext.Default, A2UiJsonUtilities.DefaultTypeInfoResolver);
        Assert.NotNull(A2UiJsonSerializerContext.Default.GetTypeInfo(typeof(A2UiMessage)));
        Assert.NotNull(A2UiJsonSerializerContext.Default.GetTypeInfo(typeof(AgentToRendererMessage)));
        Assert.NotNull(A2UiJsonSerializerContext.Default.GetTypeInfo(typeof(RendererToAgentMessage)));
        Assert.NotNull(A2UiJsonSerializerContext.Default.GetTypeInfo(typeof(ClientToServerMessage)));
    }

    [Fact]
    public void SerializeAndDeserialize_AgentToRendererMessage_WithContext()
    {
        var original = new AgentToRendererMessage
        {
            Version = A2UiProtocol.Version10,
            CreateSurface = new CreateSurface
            {
                SurfaceId = "s1",
                CatalogId = A2UiProtocol.BasicCatalogIdV10,
                Components =
                [
                    new TextComponent
                    {
                        Id = "root",
                        Text = DynamicValue.FromString("Hello AOT"),
                    },
                ],
            },
        };

        string json = JsonSerializer.Serialize(original, A2UiJsonSerializerContext.Default.AgentToRendererMessage);
        AgentToRendererMessage? roundTripped = JsonSerializer.Deserialize(
            json,
            A2UiJsonSerializerContext.Default.AgentToRendererMessage
        );

        Assert.NotNull(roundTripped);
        Assert.Equal("s1", roundTripped.CreateSurface?.SurfaceId);
        Assert.Single(roundTripped.CreateSurface!.Components!);
        var textComp = Assert.IsType<TextComponent>(roundTripped.CreateSurface.Components![0]);
        Assert.Equal("Hello AOT", textComp.Text?.AsString?.Value);
    }
}
