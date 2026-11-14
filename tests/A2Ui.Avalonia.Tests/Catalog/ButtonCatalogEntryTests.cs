using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Blazejewicz.A2Ui.Avalonia.Catalog;
using Blazejewicz.A2Ui.Avalonia.Catalog.Entries;
using Blazejewicz.A2Ui.Core;
using Blazejewicz.A2Ui.Core.Actions;
using Blazejewicz.A2Ui.Core.Bindings;
using Blazejewicz.A2Ui.Core.Children;
using Blazejewicz.A2Ui.Core.Components;
using Blazejewicz.A2Ui.Core.Functions;
using Blazejewicz.A2Ui.Core.Messages;
using Blazejewicz.A2Ui.Core.Rpc;
using Blazejewicz.A2Ui.Core.Surfaces;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Blazejewicz.A2Ui.Avalonia.Tests.Catalog;

public sealed class ButtonCatalogEntryTests
{
    [AvaloniaFact]
    public void ButtonCatalogEntry_Create_WithAction_FiresEventOnClick()
    {
        var entry = new ButtonCatalogEntry();
        var dm = new DataModel();
        var ctx = new ActionCapturingRenderContext(dm);
        var component = new ButtonComponent
        {
            Id = "btn1",
            Text = DynamicValue.FromString("Go"),
            Action = new ComponentAction { Event = new ActionEvent { Name = "go_clicked" } },
        };

        var control = entry.Create(component, dm, ctx);
        Assert.IsType<Button>(control);
        var btn = (Button)control;

        // Simulate click via RaiseEvent
        btn.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        Assert.Single(ctx.FiredEvents);
        Assert.Equal("go_clicked", ctx.FiredEvents[0].EventName);
    }

    [AvaloniaFact]
    public void ButtonCatalogEntry_Create_WithActionContext_ResolvesPathsAtClickTime()
    {
        var entry = new ButtonCatalogEntry();
        var dm = new DataModel();
        var ctx = new ActionCapturingRenderContext(dm);

        var component = new ButtonComponent
        {
            Id = "btn2",
            Text = DynamicValue.FromString("Submit"),
            Action = new ComponentAction
            {
                Event = new ActionEvent
                {
                    Name = "form_submitted",
                    Context = new Dictionary<string, DynamicValue>
                    {
                        ["user"] = DynamicValue.FromPath("/username"),
                        ["greeting"] = DynamicValue.FromString("hello"),
                        ["limit"] = DynamicValue.FromNumber(10),
                        ["active"] = DynamicValue.FromBool(true),
                    },
                },
            },
        };

        var control = entry.Create(component, dm, ctx);
        var btn = (Button)control;

        // Set data model values AFTER button creation (simulates user typing)
        dm.Apply(
            new UpdateDataModel
            {
                SurfaceId = "test",
                Path = "/username",
                Value = JsonSerializer.SerializeToElement("peter"),
            }
        );

        // Click → context should resolve from current data model state with preserved JSON types
        btn.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        Assert.Single(ctx.FiredEvents);
        var payload = Assert.IsAssignableFrom<Dictionary<string, JsonElement>>(ctx.FiredEvents[0].Payload);
        Assert.Equal("peter", payload["user"].GetString());
        Assert.Equal("hello", payload["greeting"].GetString());
        Assert.Equal(10, payload["limit"].GetInt32());
        Assert.True(payload["active"].GetBoolean());
    }

    [AvaloniaFact]
    public void ButtonCatalogEntry_Create_WithEmptyContext_PassesEmptyDictionaryPayload()
    {
        var entry = new ButtonCatalogEntry();
        var dm = new DataModel();
        var ctx = new ActionCapturingRenderContext(dm);

        var component = new ButtonComponent
        {
            Id = "btn3",
            Text = DynamicValue.FromString("Click"),
            Action = new ComponentAction
            {
                Event = new ActionEvent { Name = "clicked", Context = [] },
            },
        };

        var control = entry.Create(component, dm, ctx);
        var btn = (Button)control;
        btn.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        Assert.Single(ctx.FiredEvents);
        var payload = Assert.IsAssignableFrom<Dictionary<string, JsonElement>>(ctx.FiredEvents[0].Payload);
        Assert.Empty(payload);
    }

    [AvaloniaFact]
    public void ButtonCatalogEntry_Create_WithoutAction_DoesNotThrow()
    {
        var entry = new ButtonCatalogEntry();
        var dm = new DataModel();
        var ctx = new ActionCapturingRenderContext(dm);

        var component = new ButtonComponent
        {
            Id = "btn4",
            Text = DynamicValue.FromString("Display Only"),
            // No Action set
        };

        var control = entry.Create(component, dm, ctx);
        Assert.IsType<Button>(control);
        var btn = (Button)control;

        // Click should not throw or fire events
        btn.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Empty(ctx.FiredEvents);
    }

    [AvaloniaFact]
    public void ButtonCatalogEntry_Create_PrimaryVariant_HasAccentClass()
    {
        var entry = new ButtonCatalogEntry();
        var dm = new DataModel();
        var ctx = new ActionCapturingRenderContext(dm);

        var component = new ButtonComponent
        {
            Id = "btn5",
            Text = DynamicValue.FromString("Primary"),
            Variant = "primary",
        };

        var control = entry.Create(component, dm, ctx);
        Assert.Contains("accent", control.Classes);
    }

    [AvaloniaFact]
    public void ButtonCatalogEntry_Create_BorderlessVariant_HasBorderlessClass()
    {
        var entry = new ButtonCatalogEntry();
        var dm = new DataModel();
        var ctx = new ActionCapturingRenderContext(dm);

        var component = new ButtonComponent
        {
            Id = "btn6",
            Text = DynamicValue.FromString("Borderless"),
            Variant = "borderless",
        };

        var control = entry.Create(component, dm, ctx);
        Assert.Contains("borderless", control.Classes);
    }

    [AvaloniaFact]
    public void ButtonCatalogEntry_V10TemplateScopeAndNonStringUserMessage_ResolvesTypedContextAndRejectsNonStringMessage()
    {
        using var renderer = new A2UiRenderer(CatalogRegistry.CreateDefault(), FunctionRegistry.CreateDefault());
        var surface = new Surface("main_surface", A2UiProtocol.BasicCatalogIdV10)
        {
            ProtocolVersion = A2UiProtocol.Version10,
        };
        surface.DataModel.SetSnapshot(
            JsonDocument.Parse("""{"items":[{"title":"First"},{"title":"Second"}]}""").RootElement
        );

        surface.UpdateComponents([
            new ColumnComponent
            {
                Id = "root",
                Children = ChildList.FromTemplate("row_btn", "/items"),
            },
            new ButtonComponent
            {
                Id = "row_btn",
                Text = DynamicValue.FromPath("title"),
                Action = new ComponentAction
                {
                    Event = new ActionEvent
                    {
                        Name = "item_selected",
                        Context = new Dictionary<string, DynamicValue>
                        {
                            ["itemTitle"] = DynamicValue.FromPath("title"),
                            ["upperTitle"] = DynamicValue.FromFunctionCall(
                                new FunctionCallValue
                                {
                                    Call = "formatString",
                                    Args = new Dictionary<string, JsonElement>
                                    {
                                        ["value"] = JsonSerializer.SerializeToElement("Selected: ${title}"),
                                    },
                                }
                            ),
                            ["itemIndex"] = DynamicValue.FromFunctionCall(new FunctionCallValue { Call = "@index" }),
                            ["oneBasedIndex"] = DynamicValue.FromFunctionCall(
                                new FunctionCallValue
                                {
                                    Call = "@index",
                                    Args = new Dictionary<string, JsonElement>
                                    {
                                        ["offset"] = JsonSerializer.SerializeToElement(1),
                                    },
                                }
                            ),
                            ["escapedKey"] = DynamicValue.FromObject(
                                new Dictionary<string, object?> { ["@path"] = "/literal/not/bound" }
                            ),
                        },
                    },
                },
            },
        ]);

        var fired = new List<UserActionEventArgs>();
        renderer.UserActionFired += (_, e) => fired.Add(e);

        var col = Assert.IsType<StackPanel>(renderer.Render(surface));
        Assert.Equal(2, col.Children.Count);

        var secondButton = Assert.IsType<Button>(col.Children[1]);
        secondButton.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        UserActionEventArgs evt = Assert.Single(fired);
        var context = Assert.IsType<Dictionary<string, JsonElement>>(evt.Payload);
        Assert.Equal("Second", context["itemTitle"].GetString());
        Assert.Equal("Selected: Second", context["upperTitle"].GetString());
        Assert.Equal(JsonValueKind.Number, context["itemIndex"].ValueKind);
        Assert.Equal(1, context["itemIndex"].GetInt32());
        Assert.Equal(2, context["oneBasedIndex"].GetInt32());
        Assert.Equal("/literal/not/bound", context["escapedKey"].GetProperty("@path").GetString());

        // Non-string userMessage is rejected and does not fire UserAction
        fired.Clear();
        var badBtn = (Button)new ButtonCatalogEntry().Create(
            new ButtonComponent
            {
                Id = "bad_msg_btn",
                Text = DynamicValue.FromString("Submit"),
                Action = new ComponentAction
                {
                    Event = new ActionEvent
                    {
                        Name = "submit",
                        UserMessage = DynamicValue.FromNumber(12345),
                    },
                },
            },
            surface.DataModel,
            new ActionCapturingRenderContext(surface.DataModel)
        );
        var badCtx = new ActionCapturingRenderContext(surface.DataModel);
        new ButtonCatalogEntry().Update(
            badBtn,
            new ButtonComponent
            {
                Id = "bad_msg_btn",
                Text = DynamicValue.FromString("Submit"),
                Action = new ComponentAction
                {
                    Event = new ActionEvent
                    {
                        Name = "submit",
                        UserMessage = DynamicValue.FromNumber(12345),
                    },
                },
            },
            surface.DataModel,
            badCtx
        );
        badBtn.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Empty(badCtx.FiredEvents);

        // Failing expression in context or functionCall is caught and suppresses action dispatch without crashing UI thread
        surface.UpdateComponents([
            new ColumnComponent
            {
                Id = "root",
                Children = ChildList.FromIds(["fail_ctx_btn", "fail_fn_btn"]),
            },
            new ButtonComponent
            {
                Id = "fail_ctx_btn",
                Text = DynamicValue.FromString("Bad Context"),
                Action = new ComponentAction
                {
                    Event = new ActionEvent
                    {
                        Name = "should_not_fire",
                        Context = new Dictionary<string, DynamicValue>
                        {
                            ["badIndex"] = DynamicValue.FromFunctionCall(new FunctionCallValue { Call = "@index" }),
                        },
                    },
                },
            },
            new ButtonComponent
            {
                Id = "fail_fn_btn",
                Text = DynamicValue.FromString("Bad OpenUrl"),
                Action = new ComponentAction
                {
                    FunctionCall = new FunctionCallValue
                    {
                        Call = "openUrl",
                        Args = new Dictionary<string, JsonElement>
                        {
                            ["url"] = JsonSerializer.SerializeToElement("javascript:alert(1)"),
                        },
                    },
                },
            },
        ]);

        var updatedRoot = Assert.IsType<StackPanel>(renderer.Render(surface));
        var failCtxButton = Assert.IsType<Button>(updatedRoot.Children[0]);
        var failFnButton = Assert.IsType<Button>(updatedRoot.Children[1]);

        failCtxButton.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        failFnButton.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Empty(fired);
    }

    [AvaloniaFact]
    public void ButtonCatalogEntry_ActionFunctionCall_NeverCachesAndAlwaysDispatchesFreshCall()
    {
        using var rpcHandler = new RpcHandler();
        var outbound = new List<CallAgentFunction>();
        int counter = 0;
        rpcHandler.MessageOutbound += msg =>
        {
            if (msg.CallAgentFunction is { } call)
            {
                outbound.Add(call);
                rpcHandler.HandleAgentFunctionResponse(new FunctionResponse
                {
                    FunctionCallId = call.FunctionCallId,
                    Value = JsonSerializer.SerializeToElement($"ok-{++counter}"),
                });
            }
        };

        using var renderer = new A2UiRenderer(
            CatalogRegistry.CreateDefault(),
            FunctionRegistry.CreateDefault(),
            rpcHandler: rpcHandler
        );
        var surface = new Surface("s1", A2UiProtocol.BasicCatalogIdV10) { ProtocolVersion = A2UiProtocol.Version10 };
        surface.UpdateComponents([
            new ColumnComponent
            {
                Id = "root",
                Children = ChildList.FromIds("reactive_txt", "action_btn"),
            },
            new TextComponent
            {
                Id = "reactive_txt",
                Text = DynamicValue.FromFunctionCall(new FunctionCallValue { Call = "fetchBanner" }),
            },
            new ButtonComponent
            {
                Id = "action_btn",
                Text = DynamicValue.FromString("Submit"),
                Action = new ComponentAction
                {
                    FunctionCall = new FunctionCallValue
                    {
                        Call = "submitOrder",
                        Args = new Dictionary<string, JsonElement>
                        {
                            ["item"] = JsonSerializer.SerializeToElement("widget"),
                        },
                    },
                },
            },
        ]);

        var panel = Assert.IsType<StackPanel>(renderer.Render(surface));
        Assert.Single(outbound, c => c.CallFunction.Call == "fetchBanner");

        // Second render hits surface cache for reactive binding
        _ = renderer.Render(surface);
        Assert.Single(outbound, c => c.CallFunction.Call == "fetchBanner");

        // Action button must NEVER use reactive cache — every click dispatches a new CallAgentFunction
        var btn = Assert.IsType<Button>(panel.Children[1]);
        btn.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        btn.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(2, outbound.Count(c => c.CallFunction.Call == "submitOrder"));
    }

    [AvaloniaFact]
    public void ButtonCatalogEntry_InvalidEventAction_EmitsInvalidActionAndAllowsNullUserMessage()
    {
        using var renderer = new A2UiRenderer(CatalogRegistry.CreateDefault(), FunctionRegistry.CreateDefault());
        var fired = new List<UserActionEventArgs>();
        var errors = new List<RendererToAgentMessage>();
        renderer.UserActionFired += (_, e) => fired.Add(e);
        renderer.ErrorReported += (_, e) => errors.Add(e);

        var surface = new Surface("s1", A2UiProtocol.BasicCatalogIdV10) { ProtocolVersion = A2UiProtocol.Version10 };
        surface.UpdateComponents([
            new ColumnComponent
            {
                Id = "root",
                Children = ChildList.FromIds("empty_name_btn", "bad_msg_btn", "null_msg_btn"),
            },
            new ButtonComponent
            {
                Id = "empty_name_btn",
                Text = DynamicValue.FromString("Empty"),
                Action = new ComponentAction { Event = new ActionEvent { Name = string.Empty } },
            },
            new ButtonComponent
            {
                Id = "bad_msg_btn",
                Text = DynamicValue.FromString("BadMsg"),
                Action = new ComponentAction
                {
                    Event = new ActionEvent
                    {
                        Name = "submit",
                        UserMessage = DynamicValue.FromNumber(12345),
                    },
                },
            },
            new ButtonComponent
            {
                Id = "null_msg_btn",
                Text = DynamicValue.FromString("NullMsg"),
                Action = new ComponentAction
                {
                    Event = new ActionEvent
                    {
                        Name = "submit_null",
                        UserMessage = DynamicValue.FromPath("/missing"),
                    },
                },
            },
        ]);

        var panel = Assert.IsType<StackPanel>(renderer.Render(surface));

        ((Button)panel.Children[0]).RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Empty(fired);
        RendererToAgentMessage emptyNameErr = Assert.Single(errors);
        emptyNameErr.Validate();
        Assert.Equal("INVALID_ACTION", emptyNameErr.Error!.Code);
        Assert.Equal("s1", emptyNameErr.Error.SurfaceId);

        errors.Clear();
        ((Button)panel.Children[1]).RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Empty(fired);
        RendererToAgentMessage badMsgErr = Assert.Single(errors);
        badMsgErr.Validate();
        Assert.Equal("INVALID_ACTION", badMsgErr.Error!.Code);
        Assert.Equal("s1", badMsgErr.Error.SurfaceId);

        errors.Clear();
        ((Button)panel.Children[2]).RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Empty(errors);
        UserActionEventArgs dispatched = Assert.Single(fired);
        Assert.Equal("submit_null", dispatched.EventName);
        Assert.Null(dispatched.UserMessage);
    }

    [AvaloniaFact]
    public void ButtonCatalogEntry_FailingLocalActionAndMalformedContext_EmitValidRendererErrors()
    {
        using var renderer = new A2UiRenderer(CatalogRegistry.CreateDefault(), FunctionRegistry.CreateDefault());
        var fired = new List<UserActionEventArgs>();
        var errors = new List<RendererToAgentMessage>();
        renderer.UserActionFired += (_, e) => fired.Add(e);
        renderer.ErrorReported += (_, e) => errors.Add(e);

        var surface = new Surface("s1", A2UiProtocol.BasicCatalogIdV10) { ProtocolVersion = A2UiProtocol.Version10 };
        surface.UpdateComponents([
            new ColumnComponent
            {
                Id = "root",
                Children = ChildList.FromIds("bad_url_btn", "unknown_fn_btn", "bad_ctx_btn"),
            },
            new ButtonComponent
            {
                Id = "bad_url_btn",
                Text = DynamicValue.FromString("BadUrl"),
                Action = new ComponentAction
                {
                    FunctionCall = new FunctionCallValue
                    {
                        Call = "openUrl",
                        Args = new Dictionary<string, JsonElement>
                        {
                            ["url"] = JsonSerializer.SerializeToElement("javascript:alert(1)"),
                        },
                    },
                },
            },
            new ButtonComponent
            {
                Id = "unknown_fn_btn",
                Text = DynamicValue.FromString("UnknownFn"),
                Action = new ComponentAction
                {
                    FunctionCall = new FunctionCallValue { Call = "nonExistentLocalAction" },
                },
            },
            new ButtonComponent
            {
                Id = "bad_ctx_btn",
                Text = DynamicValue.FromString("BadCtx"),
                Action = new ComponentAction
                {
                    Event = new ActionEvent
                    {
                        Name = "checkout",
                        Context = new Dictionary<string, DynamicValue>
                        {
                            ["bad"] = DynamicValue.FromFunctionCall(new FunctionCallValue { Call = "@unknown" }),
                        },
                    },
                },
            },
        ]);

        var panel = Assert.IsType<StackPanel>(renderer.Render(surface));

        ((Button)panel.Children[0]).RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        RendererToAgentMessage urlErr = Assert.Single(errors);
        urlErr.Validate();
        Assert.Equal("EXECUTION_ERROR", urlErr.Error!.Code);
        Assert.Equal("s1", urlErr.Error.SurfaceId);

        errors.Clear();
        ((Button)panel.Children[1]).RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        RendererToAgentMessage unknownFnErr = Assert.Single(errors);
        unknownFnErr.Validate();
        Assert.Equal("EXECUTION_ERROR", unknownFnErr.Error!.Code);
        Assert.Equal("s1", unknownFnErr.Error.SurfaceId);

        errors.Clear();
        ((Button)panel.Children[2]).RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Empty(fired);
        RendererToAgentMessage ctxErr = Assert.Single(errors);
        ctxErr.Validate();
        Assert.Equal("EXPRESSION_ERROR", ctxErr.Error!.Code);
        Assert.Equal("s1", ctxErr.Error.SurfaceId);
        Assert.Contains("Action event context entry 'bad' is invalid", ctxErr.Error.Message);
    }

    [AvaloniaFact]
    public async Task ButtonCatalogEntry_ActionRpcError_SyncAndAsync_EmitSingleExpressionErrorWithFunctionCallIdAsync()
    {
        // Synchronous RPC failure inside button action
        using (var syncRpc = new RpcHandler())
        {
            CallAgentFunction? syncCall = null;
            syncRpc.MessageOutbound += msg =>
            {
                if (msg.CallAgentFunction is { } call)
                {
                    syncCall = call;
                    syncRpc.HandleAgentFunctionResponse(new FunctionResponse
                    {
                        FunctionCallId = call.FunctionCallId,
                        Error = new FunctionError { Code = "EXECUTION_ERROR", Message = "Sync action rejected" },
                    });
                }
            };

            using var syncRenderer = new A2UiRenderer(CatalogRegistry.CreateDefault(), rpcHandler: syncRpc);
            var syncErrors = new List<RendererToAgentMessage>();
            syncRenderer.ErrorReported += (_, e) => syncErrors.Add(e);

            var syncSurface = new Surface("s_sync", A2UiProtocol.BasicCatalogIdV10)
            {
                ProtocolVersion = A2UiProtocol.Version10,
            };
            syncSurface.UpdateComponents([
                new ButtonComponent
                {
                    Id = "btn",
                    Text = DynamicValue.FromString("Run"),
                    Action = new ComponentAction
                    {
                        FunctionCall = new FunctionCallValue { Call = "runAgentTask" },
                    },
                },
            ]);

            var syncBtn = Assert.IsType<Button>(syncRenderer.Render(syncSurface));
            syncBtn.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

            Assert.NotNull(syncCall);
            RendererToAgentMessage syncErrMsg = Assert.Single(syncErrors);
            syncErrMsg.Validate();
            Assert.NotNull(syncErrMsg.Error);
            Assert.Equal("EXPRESSION_ERROR", syncErrMsg.Error.Code);
            Assert.Equal(syncCall.FunctionCallId, syncErrMsg.Error.FunctionCallId);
            Assert.Null(syncErrMsg.Error.SurfaceId);
            Assert.Contains("Sync action rejected", syncErrMsg.Error.Message);
        }

        // Asynchronous RPC failure inside button action
        using var asyncRpc = new RpcHandler();
        var asyncOutbound = new List<CallAgentFunction>();
        asyncRpc.MessageOutbound += msg =>
        {
            if (msg.CallAgentFunction is { } call)
            {
                asyncOutbound.Add(call);
            }
        };

        using var asyncRenderer = new A2UiRenderer(CatalogRegistry.CreateDefault(), rpcHandler: asyncRpc);
        var asyncErrors = new List<RendererToAgentMessage>();
        var errorTcs = new TaskCompletionSource<RendererToAgentMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        asyncRenderer.ErrorReported += (_, e) =>
        {
            asyncErrors.Add(e);
            errorTcs.TrySetResult(e);
        };

        var asyncSurface = new Surface("s_async", A2UiProtocol.BasicCatalogIdV10)
        {
            ProtocolVersion = A2UiProtocol.Version10,
        };
        asyncSurface.UpdateComponents([
            new ButtonComponent
            {
                Id = "btn",
                Text = DynamicValue.FromString("RunAsync"),
                Action = new ComponentAction
                {
                    FunctionCall = new FunctionCallValue { Call = "runAgentTaskAsync" },
                },
            },
        ]);

        var asyncBtn = Assert.IsType<Button>(asyncRenderer.Render(asyncSurface));
        asyncBtn.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        CallAgentFunction asyncCall = Assert.Single(asyncOutbound);
        Assert.Empty(asyncErrors);

        asyncRpc.HandleAgentFunctionResponse(new FunctionResponse
        {
            FunctionCallId = asyncCall.FunctionCallId,
            Error = new FunctionError { Code = "EXECUTION_ERROR", Message = "Async action failed" },
        });

        RendererToAgentMessage asyncErrMsg = await errorTcs.Task
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        asyncErrMsg.Validate();
        Assert.Single(asyncErrors);
        Assert.NotNull(asyncErrMsg.Error);
        Assert.Equal("EXPRESSION_ERROR", asyncErrMsg.Error.Code);
        Assert.Equal(asyncCall.FunctionCallId, asyncErrMsg.Error.FunctionCallId);
        Assert.Null(asyncErrMsg.Error.SurfaceId);
        Assert.Contains("Async action failed", asyncErrMsg.Error.Message);
    }
}

/// <summary>
/// Render context that captures fired user actions for assertion.
/// </summary>
internal sealed class ActionCapturingRenderContext(DataModel dm) : IRenderContext
{
    public List<(string EventName, object? Payload, string? ComponentId)> FiredEvents { get; } = [];

    public Control? RenderChild(string? childId) => null;

    public IEnumerable<Control> RenderChildren(string parentId) => [];

    public void FireUserAction(string eventName, object? payload = null, string? componentId = null) =>
        this.FiredEvents.Add((eventName, payload, componentId));

    public string? Resolve(DynamicValue? value) => dm.Resolve(value);

    public JsonElement? ResolveJsonElement(DynamicValue? value) => dm.ResolveJsonElement(value);

    public void UpdateDataModel(string path, string? value) { }

    public double? GetComponentWeight(string componentId) => null;

    public ILogger? Logger => null;

    public CancellationToken SurfaceCancellation => CancellationToken.None;
}
