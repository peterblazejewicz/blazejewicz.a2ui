using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;
using Blazejewicz.A2Ui.Core.Actions;
using Blazejewicz.A2Ui.Core.Bindings;
using Blazejewicz.A2Ui.Core.Components;
using Blazejewicz.A2Ui.Core.Functions;
using Blazejewicz.A2Ui.Core.Messages;
using Blazejewicz.A2Ui.Core.Rpc;
using Blazejewicz.A2Ui.Core.Surfaces;
using Blazejewicz.A2Ui.Core.Validation;

namespace Blazejewicz.A2Ui.Core.Tests.A2Ui;

public sealed class RpcHandlerTests
{
    [Fact]
    public void FunctionDefinition_RequiresUserActivation_ThrowsWhenNotRendererOnly()
    {
        var def = new FunctionDefinition
        {
            Name = "openUrl",
            CatalogId = "test",
            AllowedCallers = AllowedCallers.AgentOnly,
            RequiresUserActivation = true,
        };

        var ex = Assert.Throws<A2UiMessageValidationException>(() => def.Validate());
        Assert.Contains("RequiresUserActivation can only be true when AllowedCallers is RendererOnly", ex.Message);
    }

    [Fact]
    public void FunctionDefinition_ValidatesAllowedCallersRendererOrAgent()
    {
        var def = new FunctionDefinition
        {
            Name = "formatDate",
            CatalogId = "test",
            AllowedCallers = AllowedCallers.RendererOrAgent,
            RequiresUserActivation = false,
        };

        def.Validate(); // Should not throw
        Assert.Equal("formatDate", def.Name);
    }

    [Fact]
    public void HandleCallRendererFunction_SuccessfulExecution_ReturnsRendererFunctionResponse()
    {
        var handler = new RpcHandler();
        handler.RegisterFunction(
            new FunctionDefinition
            {
                Name = "add",
                CatalogId = "math",
                AllowedCallers = AllowedCallers.RendererOrAgent,
                Implementation = args =>
                {
                    int a =
                        args.TryGetValue("a", out string? aStr)
                        && int.TryParse(aStr, CultureInfo.InvariantCulture, out int aVal)
                            ? aVal
                            : 0;
                    int b =
                        args.TryGetValue("b", out string? bStr)
                        && int.TryParse(bStr, CultureInfo.InvariantCulture, out int bVal)
                            ? bVal
                            : 0;
                    return (a + b).ToString(CultureInfo.InvariantCulture);
                },
            }
        );

        using var argsDoc = JsonDocument.Parse("{\"a\": 20, \"b\": 22}");
        var argsDict = new Dictionary<string, JsonElement>();
        foreach (var prop in argsDoc.RootElement.EnumerateObject())
        {
            argsDict[prop.Name] = prop.Value.Clone();
        }

        var call = new CallRendererFunction
        {
            FunctionCallId = "call-1",
            CallFunction = new FunctionCallValue
            {
                Call = "add",
                CatalogId = "math",
                Args = argsDict,
            },
        };

        var response = handler.HandleCallRendererFunction(call);

        Assert.Equal(A2UiProtocol.Version10, response.Version);
        Assert.NotNull(response.RendererFunctionResponse);
        Assert.Equal("call-1", response.RendererFunctionResponse.FunctionCallId);
        Assert.NotNull(response.RendererFunctionResponse.Value);
        Assert.Equal("42", response.RendererFunctionResponse.Value.Value.GetString());
    }

    [Fact]
    public void HandleCallRendererFunction_UnknownFunction_ReturnsInvalidFunctionCallError()
    {
        var handler = new RpcHandler();
        var call = new CallRendererFunction
        {
            FunctionCallId = "call-unknown",
            CallFunction = new FunctionCallValue { Call = "nonExistent", CatalogId = "test" },
        };

        var response = handler.HandleCallRendererFunction(call);

        Assert.Equal(A2UiProtocol.Version10, response.Version);
        Assert.NotNull(response.RendererFunctionResponse?.Error);
        Assert.Equal("INVALID_FUNCTION_CALL", response.RendererFunctionResponse.Error.Code);
        Assert.Equal("call-unknown", response.RendererFunctionResponse.FunctionCallId);
    }

    [Fact]
    public void HandleCallRendererFunction_RequiresUserActivation_ReturnsInvalidFunctionCallError()
    {
        var handler = new RpcHandler();
        handler.RegisterFunction(
            new FunctionDefinition
            {
                Name = "launchApp",
                CatalogId = "test",
                AllowedCallers = AllowedCallers.RendererOnly,
                RequiresUserActivation = true,
            }
        );

        var call = new CallRendererFunction
        {
            FunctionCallId = "call-act",
            CallFunction = new FunctionCallValue { Call = "launchApp", CatalogId = "test" },
        };

        var response = handler.HandleCallRendererFunction(call);

        Assert.NotNull(response.RendererFunctionResponse?.Error);
        Assert.Equal("INVALID_FUNCTION_CALL", response.RendererFunctionResponse.Error.Code);
    }

    [Fact]
    public async Task InvokeAgentFunctionAsync_EmitsCallAgentFunction_ResolvesOnAgentResponseAsync()
    {
        var handler = new RpcHandler();
        RendererToAgentMessage? emitted = null;

        handler.MessageOutboundAsync += msg =>
        {
            emitted = msg;
            return Task.CompletedTask;
        };

        var callTask = handler.InvokeAgentFunctionAsync(
            "surface-1",
            new FunctionCallValue { Call = "fetchUserProfile" },
            TestContext.Current.CancellationToken
        );

        Assert.NotNull(emitted);
        Assert.Equal(A2UiProtocol.Version10, emitted.Version);
        Assert.NotNull(emitted.CallAgentFunction);
        Assert.Equal("surface-1", emitted.CallAgentFunction.SurfaceId);
        string callId = emitted.CallAgentFunction.FunctionCallId;

        using var valDoc = JsonDocument.Parse("{\"name\": \"Alice\"}");
        bool handled = handler.HandleAgentFunctionResponse(
            new FunctionResponse { FunctionCallId = callId, Value = valDoc.RootElement.Clone() }
        );

        Assert.True(handled);
        var result = await callTask.ConfigureAwait(true);
        Assert.Equal(callId, result.FunctionCallId);
        Assert.NotNull(result.Value);
        Assert.Equal("Alice", result.Value.Value.GetProperty("name").GetString());
    }

    [Fact]
    public async Task CancelSurfaceCalls_CancelsPendingCallsForSurfaceAsync()
    {
        var handler = new RpcHandler();
        handler.MessageOutbound += _ => { };

        var task1 = handler.InvokeAgentFunctionAsync(
            "surface-A",
            new FunctionCallValue { Call = "f1" },
            TestContext.Current.CancellationToken
        );
        var task2 = handler.InvokeAgentFunctionAsync(
            "surface-B",
            new FunctionCallValue { Call = "f2" },
            TestContext.Current.CancellationToken
        );

        handler.CancelSurfaceCalls("surface-A");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task1);
        Assert.False(task2.IsCompleted);

        // Complete task2 cleanly
        handler.CancelSurfaceCalls("surface-B");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task2);
    }

    [Fact]
    public async Task AttachTo_CancelsOnSurfaceDeletedAsync()
    {
        var manager = new SurfaceManager();
        var handler = new RpcHandler();
        handler.AttachTo(manager);
        handler.MessageOutbound += _ => { };

        manager.Process(
            new AgentToRendererMessage
            {
                Version = A2UiProtocol.Version10,
                CreateSurface = new CreateSurface { SurfaceId = "surface-X" },
            }
        );

        var task = handler.InvokeAgentFunctionAsync(
            "surface-X",
            new FunctionCallValue { Call = "op" },
            TestContext.Current.CancellationToken
        );
        Assert.False(task.IsCompleted);

        manager.Process(
            new AgentToRendererMessage
            {
                Version = A2UiProtocol.Version10,
                DeleteSurface = new DeleteSurface { SurfaceId = "surface-X" },
            }
        );

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public void ValidationResult_PropertiesAndDefaults()
    {
        var res1 = new ValidationResult { Valid = true };
        Assert.True(res1.Valid);
        Assert.Equal("error", res1.Severity);
        Assert.Null(res1.Code);
        Assert.Null(res1.Message);

        var res2 = new ValidationResult
        {
            Valid = false,
            Code = "TOO_SHORT",
            Message = "Minimum length is 5",
            Severity = "warning",
        };
        Assert.False(res2.Valid);
        Assert.Equal("TOO_SHORT", res2.Code);
        Assert.Equal("Minimum length is 5", res2.Message);
        Assert.Equal("warning", res2.Severity);
    }

    [Fact]
    public async Task Dispose_CancelsPendingCalls_ThrowsObjectDisposedExceptionAsync()
    {
        var handler = new RpcHandler();
        handler.MessageOutbound += _ => { };

        var task = handler.InvokeAgentFunctionAsync(
            "surface-1",
            new FunctionCallValue { Call = "fetchData" },
            TestContext.Current.CancellationToken
        );

        handler.Dispose();

        var ex = await Assert.ThrowsAsync<ObjectDisposedException>(() => task);
        Assert.Contains("RPC_DISPOSED: RpcHandler was disposed.", ex.Message);
    }

    [Fact]
    public async Task InvokeAgentFunctionAsync_WhenDisposed_ThrowsObjectDisposedExceptionAsync()
    {
        var handler = new RpcHandler();
        handler.MessageOutbound += _ => { };
        handler.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            handler.InvokeAgentFunctionAsync(
                "surface-1",
                new FunctionCallValue { Call = "fetchData" },
                TestContext.Current.CancellationToken
            )
        );
    }

    [Fact]
    public async Task InvokeAgentFunctionAsync_WhenNoListenerAttached_ThrowsInvalidOperationExceptionAsync()
    {
        var handler = new RpcHandler();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.InvokeAgentFunctionAsync(
                "surface-1",
                new FunctionCallValue { Call = "fetchData" },
                TestContext.Current.CancellationToken
            )
        );

        Assert.Contains("RPC_NO_LISTENER: No outbound message listener is attached to RpcHandler.", ex.Message);
    }

    [Fact]
    public void HandleAgentFunctionResponse_WhenDisposed_ReturnsFalse()
    {
        var handler = new RpcHandler();
        handler.Dispose();

        using var valDoc = JsonDocument.Parse("42");
        bool handled = handler.HandleAgentFunctionResponse(
            new FunctionResponse { FunctionCallId = "call-1", Value = valDoc.RootElement.Clone() }
        );

        Assert.False(handled);
    }

    [Fact]
    public void HandleAgentFunctionResponse_WhenUnknownCallId_ReturnsFalse()
    {
        var handler = new RpcHandler();

        using var valDoc = JsonDocument.Parse("42");
        bool handled = handler.HandleAgentFunctionResponse(
            new FunctionResponse { FunctionCallId = "nonexistent-call", Value = valDoc.RootElement.Clone() }
        );

        Assert.False(handled);
    }

    [Fact]
    public void ValidateComponentBindings_RejectsAgentOnlyFunctionInDynamicValue()
    {
        var handler = new RpcHandler();
        handler.RegisterFunction(
            new FunctionDefinition
            {
                Name = "secretServerOp",
                CatalogId = "test",
                AllowedCallers = AllowedCallers.AgentOnly,
            }
        );

        var comp = new TextComponent
        {
            Id = "txt1",
            Text = DynamicValue.FromFunctionCall(new FunctionCallValue { Call = "secretServerOp", CatalogId = "test" }),
        };

        var ex = Assert.Throws<A2UiMessageValidationException>(() => handler.ValidateComponentBindings([comp]));
        Assert.Contains("INVALID_FUNCTION_CALL: Function 'secretServerOp' has allowedCallers set to agentOnly and cannot be called by the renderer.", ex.Message);
    }

    [Fact]
    public void ValidateComponentBindings_RejectsAgentOnlyFunctionInCheckRule()
    {
        var handler = new RpcHandler();
        handler.RegisterFunction(
            new FunctionDefinition
            {
                Name = "agentCheck",
                CatalogId = "test",
                AllowedCallers = AllowedCallers.AgentOnly,
            }
        );

        var comp = new TextFieldComponent
        {
            Id = "tf1",
            Checks =
            [
                new CheckRule
                {
                    Condition = DynamicValue.FromFunctionCall(new FunctionCallValue { Call = "agentCheck" }),
                },
            ],
        };

        var ex = Assert.Throws<A2UiMessageValidationException>(() => handler.ValidateComponentBindings([comp]));
        Assert.Contains("INVALID_FUNCTION_CALL: Function 'agentCheck' has allowedCallers set to agentOnly and cannot be called by the renderer.", ex.Message);
    }

    [Fact]
    public void ValidateComponentBindings_RejectsAgentOnlyFunctionInButtonAction()
    {
        var handler = new RpcHandler();
        handler.RegisterFunction(
            new FunctionDefinition
            {
                Name = "agentAction",
                CatalogId = "test",
                AllowedCallers = AllowedCallers.AgentOnly,
            }
        );

        var btn = new ButtonComponent
        {
            Id = "btn1",
            Action = new ComponentAction
            {
                FunctionCall = new FunctionCallValue { Call = "agentAction" },
            },
        };

        var ex = Assert.Throws<A2UiMessageValidationException>(() => handler.ValidateComponentBindings([btn]));
        Assert.Contains("INVALID_FUNCTION_CALL: Function 'agentAction' has allowedCallers set to agentOnly and cannot be called by the renderer.", ex.Message);
    }

    [Fact]
    public void ValidateComponentBindings_RejectsAgentOnlyFunctionInNestedArg()
    {
        var handler = new RpcHandler();
        handler.RegisterFunction(
            new FunctionDefinition
            {
                Name = "nestedAgentOp",
                CatalogId = "test",
                AllowedCallers = AllowedCallers.AgentOnly,
            }
        );

        using var nestedDoc = JsonDocument.Parse("{\"@call\": \"nestedAgentOp\"}");
        var comp = new TextComponent
        {
            Id = "txt1",
            Text = DynamicValue.FromFunctionCall(
                new FunctionCallValue
                {
                    Call = "formatString",
                    Args = new Dictionary<string, JsonElement>
                    {
                        ["arg1"] = nestedDoc.RootElement.Clone(),
                    },
                }
            ),
        };

        var ex = Assert.Throws<A2UiMessageValidationException>(() => handler.ValidateComponentBindings([comp]));
        Assert.Contains("INVALID_FUNCTION_CALL: Function 'nestedAgentOp' has allowedCallers set to agentOnly and cannot be called by the renderer.", ex.Message);
    }

    [Fact]
    public void ValidateComponentBindings_AllowsRendererOrAgentFunction()
    {
        var handler = new RpcHandler();
        handler.RegisterFunction(
            new FunctionDefinition
            {
                Name = "allowedOp",
                CatalogId = "test",
                AllowedCallers = AllowedCallers.RendererOrAgent,
            }
        );

        var comp = new TextComponent
        {
            Id = "txt1",
            Text = DynamicValue.FromFunctionCall(new FunctionCallValue { Call = "allowedOp", CatalogId = "test" }),
        };

        handler.ValidateComponentBindings([comp]); // Should not throw
    }
}
