using System;
using System.Text.Json;
using System.Threading.Tasks;
using Blazejewicz.A2Ui.Core.Bindings;
using Blazejewicz.A2Ui.Core.Components;
using Blazejewicz.A2Ui.Core.Messages;

namespace Blazejewicz.A2Ui.Core.Tests;

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

    [Fact]
    public void VersionBoundOptions_DeserializeAndSerialize_V09AndV10WithoutEnterVersionScope()
    {
        Assert.Equal(A2UiProtocol.Version091, A2UiJsonUtilities.GetProtocolVersion(A2UiJsonUtilities.V09Options));
        Assert.Equal(A2UiProtocol.Version10, A2UiJsonUtilities.GetProtocolVersion(A2UiJsonUtilities.V10Options));
        Assert.Null(A2UiJsonUtilities.GetProtocolVersion(A2UiJsonUtilities.DefaultOptions));

        JsonSerializerOptions customV10 = A2UiJsonUtilities.CreateOptions(A2UiProtocol.Version10);
        Assert.True(customV10.IsReadOnly);
        Assert.Equal(A2UiProtocol.Version10, A2UiJsonUtilities.GetProtocolVersion(customV10));

        const string V10PathJson = """{"@path":"/user/name"}""";
        const string V10CallJson = """{"@call":"formatString","args":{"value":"Hi ${/user/name}"},"returnType":"string"}""";
        const string V10ObjectJson = """{"nested":{"@path":"/items/0"},"@@escaped":"ok"}""";
        const string V09PathJson = """{"path":"/user/name"}""";
        const string V09CallJson = """{"call":"required","returnType":"boolean"}""";

        DynamicValue? v10Path = JsonSerializer.Deserialize(V10PathJson, A2UiJsonUtilities.V10Context.DynamicValue);
        Assert.Equal("/user/name", Assert.IsType<DynamicValue.PathValue>(v10Path).DataPath);
        Assert.Equal(V10PathJson, JsonSerializer.Serialize(v10Path, A2UiJsonUtilities.V10Options));

        DynamicValue? v10Call = JsonSerializer.Deserialize<DynamicValue>(V10CallJson, A2UiJsonUtilities.V10Options);
        Assert.Equal("formatString", Assert.IsType<DynamicValue.FunctionValue>(v10Call).Call.Call);

        DynamicValue? v10Obj = JsonSerializer.Deserialize<DynamicValue>(V10ObjectJson, customV10);
        var objVal = Assert.IsType<DynamicValue.ObjectValue>(v10Obj);
        Assert.Equal("/items/0", Assert.IsType<DynamicValue.PathValue>(objVal.Value["nested"]).DataPath);
        Assert.Equal("ok", objVal.Value["@escaped"]);

        DynamicValue? v09Path = JsonSerializer.Deserialize(V09PathJson, A2UiJsonUtilities.V09Context.DynamicValue);
        Assert.Equal("/user/name", Assert.IsType<DynamicValue.PathValue>(v09Path).DataPath);
        Assert.Equal(V09PathJson, JsonSerializer.Serialize(v09Path, A2UiJsonUtilities.V09Options));

        DynamicValue? v09Call = JsonSerializer.Deserialize<DynamicValue>(V09CallJson, A2UiJsonUtilities.V09Options);
        Assert.Equal("required", Assert.IsType<DynamicValue.FunctionValue>(v09Call).Call.Call);

        // Cross-version rejection works purely from options without AsyncLocal scope
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<DynamicValue>(V10PathJson, A2UiJsonUtilities.V09Options)
        );
    }

    [Fact]
    public void FixedConverterVersion_OverridesAmbientVersionScope()
    {
        var pinnedV09Options = new JsonSerializerOptions();
        pinnedV09Options.Converters.Add(new DynamicValueConverter(A2UiProtocol.Version091));
        pinnedV09Options.Converters.Add(new A2UiComponentConverter(A2UiProtocol.Version091));

        using IDisposable ambientV10 = DynamicValueConverter.EnterVersionScope(A2UiProtocol.Version10);

        DynamicValue? dv = JsonSerializer.Deserialize<DynamicValue>("""{"path":"/legacy"}""", pinnedV09Options);
        Assert.Equal("/legacy", Assert.IsType<DynamicValue.PathValue>(dv).DataPath);
        Assert.Equal("""{"path":"/legacy"}""", JsonSerializer.Serialize(dv, pinnedV09Options));
    }

    [Fact]
    public async Task ConcurrentV09AndV10Deserialization_WithoutVersionScope_SucceedsDeterministicallyAsync()
    {
        const string V09Envelope = """
            {
                "version": "v0.9.1",
                "updateComponents": {
                    "surfaceId": "s09",
                    "components": [
                        { "id": "root", "component": "Text", "text": { "path": "/greeting" } },
                        { "id": "tf", "component": "TextField", "value": { "call": "required" } }
                    ]
                }
            }
            """;
        const string V10Envelope = """
            {
                "version": "1.0",
                "updateComponents": {
                    "surfaceId": "s10",
                    "components": [
                        { "id": "root", "component": "Text", "text": { "@path": "/greeting" } },
                        { "id": "tf", "component": "TextField", "value": { "@call": "formatString", "args": { "value": "Hi ${/name}" } } }
                    ]
                }
            }
            """;

        var tasks = new Task[64];
        for (int i = 0; i < tasks.Length; i++)
        {
            int index = i;
            tasks[i] = Task.Run(
                () =>
                {
                    for (int iter = 0; iter < 25; iter++)
                    {
                        if ((index + iter) % 2 == 0)
                        {
                            A2UiMessage? m09 = JsonSerializer.Deserialize(
                                V09Envelope,
                                A2UiJsonUtilities.V09Context.A2UiMessage
                            );
                            Assert.NotNull(m09);
                            var t09 = Assert.IsType<TextComponent>(m09.UpdateComponents!.Components[0]);
                            Assert.Equal("/greeting", t09.Text?.AsPath?.DataPath);
                        }
                        else
                        {
                            AgentToRendererMessage? m10 = JsonSerializer.Deserialize(
                                V10Envelope,
                                A2UiJsonUtilities.V10Context.AgentToRendererMessage
                            );
                            Assert.NotNull(m10);
                            var t10 = Assert.IsType<TextComponent>(m10.UpdateComponents!.Components[0]);
                            Assert.Equal("/greeting", t10.Text?.AsPath?.DataPath);
                        }
                    }
                },
                TestContext.Current.CancellationToken
            );
        }

        await Task.WhenAll(tasks).ConfigureAwait(true);
    }
}
