using System.Collections.Generic;
using System.Text.Json;
using Blazejewicz.A2Ui.Core.Bindings;
using Blazejewicz.A2Ui.Core.Capabilities;
using Blazejewicz.A2Ui.Core.Components;
using Blazejewicz.A2Ui.Core.Messages;
using Blazejewicz.A2Ui.Core.Surfaces;
using Blazejewicz.A2Ui.Core.Versioning;

namespace Blazejewicz.A2Ui.Core.Tests;

public sealed class V10EnvelopeAndAdapterTests
{
    private static readonly JsonSerializerOptions s_opts = new(JsonSerializerDefaults.Web)
    {
        AllowOutOfOrderMetadataProperties = true,
    };

    [Fact]
    public void AgentToRendererMessage_CreateSurface_RoundTrip()
    {
        var msg = new AgentToRendererMessage
        {
            Version = "1.0",
            CreateSurface = new CreateSurface
            {
                SurfaceId = "main",
                CatalogId = A2UiProtocol.BasicCatalogIdV10,
                SendDataModel = true,
                Components = [new TextComponent { Id = "root", Text = DynamicValue.FromString("Hello v1.0") }],
            },
        };

        msg.Validate();

        var json = JsonSerializer.Serialize(msg, s_opts);
        var deserialized = JsonSerializer.Deserialize<AgentToRendererMessage>(json, s_opts)!;

        deserialized.Validate();
        Assert.Equal("1.0", deserialized.Version);
        Assert.NotNull(deserialized.CreateSurface);
        Assert.Equal("main", deserialized.CreateSurface!.SurfaceId);
        Assert.Equal(A2UiProtocol.BasicCatalogIdV10, deserialized.CreateSurface.CatalogId);
        Assert.True(deserialized.CreateSurface.SendDataModel);
        Assert.Single(deserialized.CreateSurface.Components!);
    }

    [Fact]
    public void AgentToRendererMessage_Validate_RequiresVersion10()
    {
        var msg = new AgentToRendererMessage
        {
            Version = "v0.9",
            DeleteSurface = new DeleteSurface { SurfaceId = "main" },
        };

        var ex = Assert.Throws<A2UiMessageValidationException>(() => msg.Validate());
        Assert.Contains("expected '1.0'", ex.Message);
    }

    [Fact]
    public void AgentToRendererMessage_Validate_RequiresExactlyOneOperation()
    {
        var emptyMsg = new AgentToRendererMessage { Version = "1.0" };
        Assert.Throws<A2UiMessageValidationException>(() => emptyMsg.Validate());

        var multipleMsg = new AgentToRendererMessage
        {
            Version = "1.0",
            DeleteSurface = new DeleteSurface { SurfaceId = "main" },
            UpdateDataModel = new UpdateDataModel
            {
                SurfaceId = "main",
                Path = "/name",
                Value = JsonSerializer.SerializeToElement("test"),
            },
        };
        Assert.Throws<A2UiMessageValidationException>(() => multipleMsg.Validate());
    }

    [Fact]
    public void RendererToAgentMessage_Action_RoundTrip()
    {
        var msg = new RendererToAgentMessage
        {
            Version = "1.0",
            Action = new RendererAction
            {
                Name = "submitForm",
                SurfaceId = "main",
                SourceComponentId = "btn_submit",
                Timestamp = "2026-11-07T00:00:00Z",
                UserMessage = "User clicked submit",
                Context = new Dictionary<string, JsonElement> { ["key"] = JsonSerializer.SerializeToElement("val") },
            },
        };

        msg.Validate();

        var json = JsonSerializer.Serialize(msg, s_opts);
        var deserialized = JsonSerializer.Deserialize<RendererToAgentMessage>(json, s_opts)!;

        deserialized.Validate();
        Assert.Equal("1.0", deserialized.Version);
        Assert.NotNull(deserialized.Action);
        Assert.Equal("submitForm", deserialized.Action!.Name);
        Assert.Equal("main", deserialized.Action.SurfaceId);
        Assert.Equal("btn_submit", deserialized.Action.SourceComponentId);
        Assert.Equal("User clicked submit", deserialized.Action.UserMessage);
    }

    [Fact]
    public void RendererError_Validate_SurfaceIdAndFunctionCallIdAreMutuallyExclusive()
    {
        var both = new RendererError
        {
            Code = "GENERIC_ERROR",
            Message = "error",
            SurfaceId = "s1",
            FunctionCallId = "call_1",
        };
        Assert.Throws<A2UiMessageValidationException>(() => both.Validate());

        var neither = new RendererError { Code = "GENERIC_ERROR", Message = "error" };
        Assert.Throws<A2UiMessageValidationException>(() => neither.Validate());

        var validSurface = new RendererError
        {
            Code = "GENERIC_ERROR",
            Message = "error",
            SurfaceId = "s1",
        };
        validSurface.Validate(); // Should not throw

        var validFunc = new RendererError
        {
            Code = "GENERIC_ERROR",
            Message = "error",
            FunctionCallId = "call_1",
        };
        validFunc.Validate(); // Should not throw
    }

    [Fact]
    public void RendererError_Validate_RequiresPathForValidationAndCompositionCodes()
    {
        var err = new RendererError
        {
            Code = "VALIDATION_FAILED",
            Message = "validation failed",
            SurfaceId = "s1",
            Path = null,
        };
        Assert.Throws<A2UiMessageValidationException>(() => err.Validate());

        var validErr = new RendererError
        {
            Code = "UNALLOWED_PARENT",
            Message = "unallowed parent",
            SurfaceId = "s1",
            Path = "/components/0",
        };
        validErr.Validate(); // Should not throw
    }

    [Fact]
    public void Capabilities_RoundTrip()
    {
        var rc = new RendererCapabilities
        {
            V10 = new RendererCapabilitiesV10
            {
                SupportedCatalogIds = ["https://a2ui.org/catalogs/basic/v1/catalog.json"],
            },
        };

        var json = JsonSerializer.Serialize(rc, s_opts);
        var deserialized = JsonSerializer.Deserialize<RendererCapabilities>(json, s_opts)!;

        Assert.NotNull(deserialized.V10);
        Assert.Contains("https://a2ui.org/catalogs/basic/v1/catalog.json", deserialized.V10.SupportedCatalogIds);
    }

    [Fact]
    public void V10VersionAdapter_RejectsThemeOnCreateSurface()
    {
        var adapter = new V10VersionAdapter();
        const string Json = """
            {
                "version": "1.0",
                "createSurface": {
                    "surfaceId": "s1",
                    "theme": { "primaryColor": "#ff0000" }
                }
            }
            """;

        var ex = Assert.Throws<A2UiMessageValidationException>(() => adapter.AdaptAgentToRenderer(Json));
        Assert.Contains("theme", ex.Message);
    }

    [Fact]
    public void V10VersionAdapter_RejectsValidationRegexpOnTextField()
    {
        var adapter = new V10VersionAdapter();
        const string Json = """
            {
                "version": "1.0",
                "createSurface": {
                    "surfaceId": "s1",
                    "components": [
                        {
                            "component": "TextField",
                            "id": "tf1",
                            "value": "initial",
                            "validationRegexp": "^[0-9]+$"
                        }
                    ]
                }
            }
            """;

        var ex = Assert.Throws<A2UiMessageValidationException>(() => adapter.AdaptAgentToRenderer(Json));
        Assert.Contains("validationRegexp", ex.Message);
    }

    [Fact]
    public void V10VersionAdapter_RequiresValueOnTextField()
    {
        var adapter = new V10VersionAdapter();
        const string Json = """
            {
                "version": "1.0",
                "createSurface": {
                    "surfaceId": "s1",
                    "components": [
                        {
                            "component": "TextField",
                            "id": "tf1"
                        }
                    ]
                }
            }
            """;

        var ex = Assert.Throws<A2UiMessageValidationException>(() => adapter.AdaptAgentToRenderer(Json));
        Assert.Contains("requires 'value'", ex.Message);
    }

    [Fact]
    public void V10VersionAdapter_RequiresValueOnUpdateDataModel()
    {
        var adapter = new V10VersionAdapter();
        const string JsonMissingValue = """
            {
                "version": "1.0",
                "updateDataModel": {
                    "surfaceId": "s1",
                    "path": "/user/name"
                }
            }
            """;

        var ex = Assert.Throws<A2UiMessageValidationException>(() => adapter.AdaptAgentToRenderer(JsonMissingValue));
        Assert.Contains("value", ex.Message);

        const string JsonWithNullValue = """
            {
                "version": "1.0",
                "updateDataModel": {
                    "surfaceId": "s1",
                    "path": "/user/name",
                    "value": null
                }
            }
            """;

        var op = adapter.AdaptAgentToRenderer(JsonWithNullValue);
        Assert.IsType<UpdateDataModelOp>(op);
        var udOp = (UpdateDataModelOp)op;
        Assert.True(udOp.DeleteOnNull);
        Assert.Equal("/user/name", udOp.Path);
    }

    [Fact]
    public void VersionAdapterFactory_RoutesCorrectly()
    {
        var v10Adapter = VersionAdapterFactory.GetAdapter("1.0");
        Assert.IsType<V10VersionAdapter>(v10Adapter);

        var v09Adapter = VersionAdapterFactory.GetAdapter("v0.9");
        Assert.IsType<V09VersionAdapter>(v09Adapter);

        var v091Adapter = VersionAdapterFactory.GetAdapter("v0.9.1");
        Assert.IsType<V09VersionAdapter>(v091Adapter);

        Assert.Throws<A2UiMessageValidationException>(() => VersionAdapterFactory.GetAdapter("v2.0"));
    }

    [Fact]
    public void SurfaceManager_Process_V10EnforcesSurfaceIdUniquenessEvenWhenStrictModeFalse()
    {
        var sm = new SurfaceManager(options: new SurfaceManagerOptions { StrictMode = false });

        const string Json = """
            {
                "version": "1.0",
                "createSurface": {
                    "surfaceId": "s1",
                    "components": [
                        { "component": "Text", "id": "root", "text": "Hello" }
                    ]
                }
            }
            """;

        sm.Process(Json);

        // Attempting to create the same surfaceId again should throw A2UiMessageValidationException
        var ex = Assert.Throws<A2UiMessageValidationException>(() => sm.Process(Json));
        Assert.Contains("Duplicate createSurface", ex.Message);
    }

    [Fact]
    public void SurfaceManager_Process_V10InlineComponentsAndDataModel()
    {
        var sm = new SurfaceManager();

        const string Json = """
            {
                "version": "1.0",
                "createSurface": {
                    "surfaceId": "s1",
                    "components": [
                        { "component": "Text", "id": "root", "text": "Inline Text" }
                    ],
                    "dataModel": {
                        "count": 42
                    }
                }
            }
            """;

        sm.Process(Json);

        var surface = sm.GetSurface("s1");
        Assert.NotNull(surface);
        Assert.True(surface.Components.ContainsKey("root"));
        Assert.Equal("42", surface.DataModel.Resolve(DynamicValue.FromPath("/count")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CallAgentFunction_Validate_RequiresSurfaceId(string? surfaceId)
    {
        var callAgentFunction = new CallAgentFunction
        {
            SurfaceId = surfaceId!,
            FunctionCallId = "call-1",
            CallFunction = new FunctionCallValue { Call = "pingAgent" },
        };

        var ex = Assert.Throws<A2UiMessageValidationException>(() => callAgentFunction.Validate());
        Assert.Contains("surfaceId", ex.Message);

        var envelope = new RendererToAgentMessage
        {
            Version = "1.0",
            CallAgentFunction = callAgentFunction,
        };
        var envEx = Assert.Throws<A2UiMessageValidationException>(() => envelope.Validate());
        Assert.Contains("surfaceId", envEx.Message);
    }

    [Fact]
    public void CallAgentFunction_Validate_ValidWithSurfaceId_Succeeds()
    {
        var callAgentFunction = new CallAgentFunction
        {
            SurfaceId = "surface_123",
            FunctionCallId = "call-1",
            CallFunction = new FunctionCallValue { Call = "pingAgent" },
        };

        callAgentFunction.Validate();
    }

    [Fact]
    public void RendererToAgentMessage_CallAgentFunction_JsonRoundTripAndMissingSurfaceIdThrows()
    {
        var msg = new RendererToAgentMessage
        {
            Version = "1.0",
            CallAgentFunction = new CallAgentFunction
            {
                SurfaceId = "surface_123",
                FunctionCallId = "call-1",
                CallFunction = new FunctionCallValue { Call = "pingAgent" },
            },
        };

        string json = JsonSerializer.Serialize(msg, s_opts);
        var deserialized = JsonSerializer.Deserialize<RendererToAgentMessage>(json, s_opts);

        Assert.NotNull(deserialized?.CallAgentFunction);
        deserialized.Validate();
        Assert.Equal("surface_123", deserialized.CallAgentFunction.SurfaceId);
        Assert.Equal("call-1", deserialized.CallAgentFunction.FunctionCallId);
        Assert.Equal("pingAgent", deserialized.CallAgentFunction.CallFunction.Call);

        const string MissingSurfaceIdJson = """
            {
                "version": "1.0",
                "callAgentFunction": {
                    "functionCallId": "call-1",
                    "callFunction": {
                        "@call": "pingAgent"
                    }
                }
            }
            """;

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<RendererToAgentMessage>(MissingSurfaceIdJson, s_opts)
        );
    }

    [Theory]
    [InlineData("""{ "deleteSurface": { "surfaceId": "s1" } }""")]
    [InlineData("""{ "version": 1, "deleteSurface": { "surfaceId": "s1" } }""")]
    [InlineData("""{ "version": true, "deleteSurface": { "surfaceId": "s1" } }""")]
    [InlineData("""{ "version": null, "deleteSurface": { "surfaceId": "s1" } }""")]
    public void V10VersionAdapter_MissingOrNonStringVersion_ThrowsA2UiMessageValidationException(string json)
    {
        var adapter = new V10VersionAdapter();
        var ex = Assert.Throws<A2UiMessageValidationException>(() => adapter.AdaptAgentToRenderer(json));
        Assert.Contains("expected '1.0'", ex.Message);
    }
}
