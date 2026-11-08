using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Blazejewicz.A2Ui.Core.Bindings;
using Blazejewicz.A2Ui.Core.Components;
using Blazejewicz.A2Ui.Core.Messages;
using Blazejewicz.A2Ui.Core.Surfaces;

namespace Blazejewicz.A2Ui.Core.Rpc;

/// <summary>
/// Manages catalog function definitions and bidirectional Remote Procedure Calls (RPC)
/// between the renderer and the agent per A2UI Protocol v1.0.
/// </summary>
public sealed class RpcHandler : IDisposable
{
    private readonly ConcurrentDictionary<string, FunctionDefinition> _functions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<
        string,
        (string? SurfaceId, TaskCompletionSource<FunctionResponse> Tcs)
    > _pendingAgentCalls = new(StringComparer.Ordinal);
    private bool _disposed;

    /// <summary>
    /// Event raised when an outbound message (e.g. <see cref="CallAgentFunction"/>) must be sent to the agent asynchronously.
    /// </summary>
    public event Func<RendererToAgentMessage, Task>? MessageOutboundAsync;

    /// <summary>
    /// Event raised when an outbound message must be sent to the agent synchronously.
    /// </summary>
    public event Action<RendererToAgentMessage>? MessageOutbound;

    /// <summary>
    /// Attempts to retrieve a registered function definition by name.
    /// </summary>
    public bool TryGetFunction(string name, [NotNullWhen(true)] out FunctionDefinition? definition) =>
        this._functions.TryGetValue(name, out definition);

    /// <summary>
    /// Registers a function definition with the RPC handler.
    /// </summary>
    /// <param name="definition">The function definition to register.</param>
    public void RegisterFunction(FunctionDefinition definition)
    {
        definition.Validate();
        this._functions[definition.Name] = definition;
        if (!string.IsNullOrEmpty(definition.CatalogId))
        {
            this._functions[$"{definition.CatalogId}:{definition.Name}"] = definition;
        }
    }

    /// <summary>
    /// Registers multiple function definitions.
    /// </summary>
    /// <param name="definitions">Collection of function definitions.</param>
    public void RegisterFunctions(IEnumerable<FunctionDefinition> definitions)
    {
        foreach (var def in definitions)
        {
            this.RegisterFunction(def);
        }
    }

    /// <summary>
    /// Attaches this RPC handler to a <see cref="SurfaceManager"/>, automatically cancelling
    /// pending agent function calls when a surface is deleted.
    /// </summary>
    /// <param name="surfaceManager">The surface manager to attach to.</param>
    public void AttachTo(SurfaceManager surfaceManager)
    {
        surfaceManager.SurfaceDeleted += (_, e) => this.CancelSurfaceCalls(e.Surface.SurfaceId);
    }

    /// <summary>
    /// Handles an incoming <see cref="CallRendererFunction"/> request from the agent.
    /// </summary>
    /// <param name="call">The inbound function call message.</param>
    /// <param name="dataModel">Optional surface data model for resolving dynamic arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="RendererToAgentMessage"/> containing the function result or error.</returns>
    public async Task<RendererToAgentMessage> HandleCallRendererFunctionAsync(
        CallRendererFunction call,
        DataModel? dataModel = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!this.TryPrepareRendererFunctionCall(
            call,
            dataModel,
            out FunctionDefinition? def,
            out Dictionary<string, string?>? resolvedArgs,
            out RendererToAgentMessage? errorResponse))
        {
            return errorResponse;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? result = await Task.Run(() => def.Implementation!(resolvedArgs), cancellationToken)
                .ConfigureAwait(false);

            return CreateSuccessResponse(call.FunctionCallId, result, def.ReturnType);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return CreateErrorResponse(call.FunctionCallId, "FUNCTION_EXECUTION_FAILED", ex.Message);
        }
    }

    /// <summary>
    /// Synchronously handles an incoming <see cref="CallRendererFunction"/> request on the calling thread.
    /// </summary>
    public RendererToAgentMessage HandleCallRendererFunction(CallRendererFunction call, DataModel? dataModel = null)
    {
        if (!this.TryPrepareRendererFunctionCall(
            call,
            dataModel,
            out FunctionDefinition? def,
            out Dictionary<string, string?>? resolvedArgs,
            out RendererToAgentMessage? errorResponse))
        {
            return errorResponse;
        }

        try
        {
            string? result = def.Implementation!(resolvedArgs);
            return CreateSuccessResponse(call.FunctionCallId, result, def.ReturnType);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return CreateErrorResponse(call.FunctionCallId, "FUNCTION_EXECUTION_FAILED", ex.Message);
        }
    }

    private bool TryPrepareRendererFunctionCall(
        CallRendererFunction call,
        DataModel? dataModel,
        [NotNullWhen(true)] out FunctionDefinition? def,
        [NotNullWhen(true)] out Dictionary<string, string?>? resolvedArgs,
        [NotNullWhen(false)] out RendererToAgentMessage? errorResponse
    )
    {
        call.Validate();
        string funcName = call.CallFunction.Call;
        resolvedArgs = null;
        errorResponse = null;

        def = null;
        if (!string.IsNullOrEmpty(call.CallFunction.CatalogId))
        {
            this._functions.TryGetValue($"{call.CallFunction.CatalogId}:{funcName}", out def);
        }

        if (def is null && !this._functions.TryGetValue(funcName, out def))
        {
            errorResponse = CreateErrorResponse(
                call.FunctionCallId,
                "INVALID_FUNCTION_CALL",
                $"Function '{funcName}' not found or has no local implementation."
            );
            return false;
        }

        if (def.Implementation is null)
        {
            errorResponse = CreateErrorResponse(
                call.FunctionCallId,
                "INVALID_FUNCTION_CALL",
                $"Function '{funcName}' not found or has no local implementation."
            );
            return false;
        }

        if (def.AllowedCallers == AllowedCallers.RendererOnly)
        {
            errorResponse = CreateErrorResponse(
                call.FunctionCallId,
                "INVALID_FUNCTION_CALL",
                $"Function '{funcName}' has AllowedCallers set to RendererOnly and cannot be invoked by the agent."
            );
            return false;
        }

        if (def.RequiresUserActivation)
        {
            errorResponse = CreateErrorResponse(
                call.FunctionCallId,
                "INVALID_FUNCTION_CALL",
                $"Function '{funcName}' requires user activation."
            );
            return false;
        }

        resolvedArgs = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (call.CallFunction.Args is not null)
        {
            foreach (var (k, v) in call.CallFunction.Args)
            {
                if (dataModel is not null && v.ValueKind == JsonValueKind.Object)
                {
                    try
                    {
                        using IDisposable scope = DynamicValueConverter.EnterVersionScope(A2UiProtocol.Version10);
                        DynamicValue? dv = JsonSerializer.Deserialize(
                            v.GetRawText(),
                            A2UiJsonSerializerContext.Default.DynamicValue
                        );
                        resolvedArgs[k] = dataModel.Resolve(dv);
                    }
                    catch
                    {
                        resolvedArgs[k] = v.GetRawText();
                    }
                }
                else
                {
                    resolvedArgs[k] = v.ValueKind switch
                    {
                        JsonValueKind.String => v.GetString(),
                        JsonValueKind.Null => null,
                        _ => v.GetRawText(),
                    };
                }
            }
        }

        return true;
    }

    private static RendererToAgentMessage CreateSuccessResponse(
        string functionCallId,
        string? result,
        string? returnType
    ) =>
        new()
        {
            Version = A2UiProtocol.Version10,
            RendererFunctionResponse = new FunctionResponse
            {
                FunctionCallId = functionCallId,
                Value = SerializeFunctionResult(result, returnType),
            },
        };

    private static JsonElement SerializeFunctionResult(string? result, string? returnType)
    {
        if (result is null)
        {
            using JsonDocument nullDoc = JsonDocument.Parse("null");
            return nullDoc.RootElement.Clone();
        }

        if (string.Equals(returnType, "boolean", StringComparison.OrdinalIgnoreCase)
            || string.Equals(returnType, "number", StringComparison.OrdinalIgnoreCase)
            || string.Equals(returnType, "object", StringComparison.OrdinalIgnoreCase)
            || string.Equals(returnType, "array", StringComparison.OrdinalIgnoreCase)
            || string.Equals(returnType, "any", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using JsonDocument parsedDoc = JsonDocument.Parse(result);
                return parsedDoc.RootElement.Clone();
            }
            catch (JsonException)
            {
                // Fall back to string serialization below.
            }
        }

        return JsonSerializer.SerializeToElement(result, A2UiJsonSerializerContext.Default.String!);
    }

    /// <summary>
    /// Invokes a remote function on the agent per v1.0 <c>callAgentFunction</c>.
    /// </summary>
    /// <param name="surfaceId">Surface identifier where the invocation originated.</param>
    /// <param name="callFunction">Function call name, catalogId, and arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="timeout">Optional timeout for the agent response.</param>
    /// <returns>The agent's function response.</returns>
    public async Task<FunctionResponse> InvokeAgentFunctionAsync(
        string surfaceId,
        FunctionCallValue callFunction,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null
    )
    {
        ObjectDisposedException.ThrowIf(this._disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(surfaceId);

        if (this.MessageOutboundAsync is null && this.MessageOutbound is null)
        {
            throw new InvalidOperationException("RPC_NO_LISTENER: No outbound message listener is attached to RpcHandler.");
        }

        string callId = Guid.NewGuid().ToString("n");
        var tcs = new TaskCompletionSource<FunctionResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        using CancellationTokenRegistration reg = cancellationToken.Register(
            () => tcs.TrySetCanceled(cancellationToken)
        );
        this._pendingAgentCalls[callId] = (surfaceId, tcs);

        var msg = new RendererToAgentMessage
        {
            Version = A2UiProtocol.Version10,
            CallAgentFunction = new CallAgentFunction
            {
                FunctionCallId = callId,
                SurfaceId = surfaceId,
                CallFunction = callFunction,
            },
        };

        try
        {
            if (this.MessageOutboundAsync is not null)
            {
                await this.MessageOutboundAsync(msg).ConfigureAwait(false);
            }
            else
            {
                this.MessageOutbound?.Invoke(msg);
            }

            if (timeout.HasValue)
            {
                return await tcs.Task.WaitAsync(timeout.Value, cancellationToken).ConfigureAwait(false);
            }

            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            this._pendingAgentCalls.TryRemove(callId, out _);
        }
    }

    /// <summary>
    /// Delivers an incoming <see cref="FunctionResponse"/> from the agent to the matching pending call.
    /// </summary>
    /// <param name="response">The agent's function response.</param>
    /// <returns>True if a pending call was found and completed; false otherwise.</returns>
    public bool HandleAgentFunctionResponse(FunctionResponse response)
    {
        if (this._disposed)
        {
            return false;
        }

        if (this._pendingAgentCalls.TryRemove(response.FunctionCallId, out var entry))
        {
            entry.Tcs.TrySetResult(response);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Cancels all pending agent function calls associated with the specified surface ID.
    /// </summary>
    /// <param name="surfaceId">The surface ID whose pending calls should be cancelled.</param>
    public void CancelSurfaceCalls(string surfaceId)
    {
        foreach (var (k, v) in this._pendingAgentCalls)
        {
            if (v.SurfaceId == surfaceId && this._pendingAgentCalls.TryRemove(k, out var removed))
            {
                removed.Tcs.TrySetCanceled();
            }
        }
    }

    /// <summary>
    /// Cancels all pending agent function calls.
    /// </summary>
    public void CancelAll()
    {
        foreach (var (k, _) in this._pendingAgentCalls)
        {
            if (this._pendingAgentCalls.TryRemove(k, out var removed))
            {
                removed.Tcs.TrySetCanceled();
            }
        }
    }

    /// <summary>
    /// Validates component dynamic value bindings and function calls, ensuring no component
    /// attempts to call a function with <see cref="AllowedCallers"/> set to <see cref="AllowedCallers.AgentOnly"/>.
    /// </summary>
    /// <param name="components">Collection of components to validate.</param>
    /// <exception cref="A2UiMessageValidationException">
    /// Thrown when a component binding references a function with AllowedCallers set to AgentOnly.
    /// </exception>
    public void ValidateComponentBindings(IEnumerable<A2UiComponent> components)
    {
        ArgumentNullException.ThrowIfNull(components);

        foreach (var c in components)
        {
            this.InspectDynamicValue(c.Text);
            this.InspectDynamicValue(c.Label);

            if (c.Checks is not null)
            {
                foreach (var check in c.Checks)
                {
                    this.InspectDynamicValue(check.Condition);
                }
            }

            switch (c)
            {
                case ButtonComponent btn:
                    if (btn.Action is not null)
                    {
                        if (btn.Action.FunctionCall is not null)
                        {
                            this.CheckFunctionCall(btn.Action.FunctionCall);
                        }

                        if (btn.Action.Event?.UserMessage is not null)
                        {
                            this.InspectDynamicValue(btn.Action.Event.UserMessage);
                        }

                        if (btn.Action.Event?.Context is not null)
                        {
                            foreach (var ctxVal in btn.Action.Event.Context.Values)
                            {
                                this.InspectDynamicValue(ctxVal);
                            }
                        }
                    }
                    break;
                case TextFieldComponent tf:
                    this.InspectDynamicValue(tf.Value);
                    this.InspectDynamicValue(tf.Placeholder);
                    this.InspectDynamicValue(tf.Rows);
                    break;
                case ImageComponent img:
                    this.InspectDynamicValue(img.Url);
                    this.InspectDynamicValue(img.Description);
                    break;
                case VideoComponent vid:
                    this.InspectDynamicValue(vid.Url);
                    this.InspectDynamicValue(vid.PosterUrl);
                    break;
                case AudioPlayerComponent audio:
                    this.InspectDynamicValue(audio.Url);
                    this.InspectDynamicValue(audio.Description);
                    break;
                case IconComponent icon:
                    this.InspectDynamicValue(icon.Name);
                    break;
                case SliderComponent slider:
                    this.InspectDynamicValue(slider.Value);
                    this.InspectDynamicValue(slider.Min);
                    this.InspectDynamicValue(slider.Max);
                    this.InspectDynamicValue(slider.Steps);
                    break;
                case ChoicePickerComponent choice:
                    this.InspectDynamicValue(choice.Value);
                    if (choice.Options is not null)
                    {
                        foreach (var opt in choice.Options)
                        {
                            this.InspectDynamicValue(opt.Label);
                        }
                    }
                    break;
                case TabsComponent tabs:
                    if (tabs.Tabs is not null)
                    {
                        foreach (var tab in tabs.Tabs)
                        {
                            this.InspectDynamicValue(tab.Title);
                        }
                    }
                    break;
                case CheckBoxComponent cb:
                    this.InspectDynamicValue(cb.Value);
                    break;
                case DateTimeInputComponent dt:
                    this.InspectDynamicValue(dt.Value);
                    this.InspectDynamicValue(dt.Min);
                    this.InspectDynamicValue(dt.Max);
                    break;
                case TextComponent text:
                    this.InspectDynamicValue(text.Description);
                    break;
                case GenericComponent gen:
                    foreach (var prop in gen.Properties.Values)
                    {
                        this.InspectJsonElement(prop);
                    }
                    break;
            }

            if (c.ExtensionData is not null)
            {
                foreach (var ext in c.ExtensionData.Values)
                {
                    this.InspectJsonElement(ext);
                }
            }
        }
    }

    private void InspectDynamicValue(DynamicValue? dv)
    {
        if (dv is null)
        {
            return;
        }

        if (dv is DynamicValue.FunctionValue fv)
        {
            this.CheckFunctionCall(fv.Call);
        }
        else if (dv is DynamicValue.ArrayValue av)
        {
            this.InspectJsonElement(av.Value);
        }
        else if (dv is DynamicValue.ObjectValue ov)
        {
            this.InspectObjectValue(ov.Value);
        }
    }

    private void CheckFunctionCall(FunctionCallValue fc)
    {
        FunctionDefinition? def = null;
        if (!string.IsNullOrEmpty(fc.CatalogId))
        {
            this._functions.TryGetValue($"{fc.CatalogId}:{fc.Call}", out def);
        }

        if (def is null && !this._functions.TryGetValue(fc.Call, out def))
        {
            // Not registered
        }

        if (def is not null && def.AllowedCallers == AllowedCallers.AgentOnly)
        {
            throw new A2UiMessageValidationException(
                $"INVALID_FUNCTION_CALL: Function '{fc.Call}' has allowedCallers set to agentOnly and cannot be called by the renderer."
            );
        }

        if (fc.Args is not null)
        {
            foreach (var arg in fc.Args.Values)
            {
                this.InspectJsonElement(arg);
            }
        }
    }

    private void InspectJsonElement(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Object)
        {
            string? call = null;
            if (el.TryGetProperty("@call", out var atCall) && atCall.ValueKind == JsonValueKind.String)
            {
                call = atCall.GetString();
            }
            else if (el.TryGetProperty("call", out var legCall) && legCall.ValueKind == JsonValueKind.String)
            {
                call = legCall.GetString();
            }

            if (!string.IsNullOrEmpty(call))
            {
                string? cat = el.TryGetProperty("catalogId", out var catEl) && catEl.ValueKind == JsonValueKind.String
                    ? catEl.GetString()
                    : null;

                FunctionDefinition? def = null;
                if (!string.IsNullOrEmpty(cat))
                {
                    this._functions.TryGetValue($"{cat}:{call}", out def);
                }

                if (def is null && !this._functions.TryGetValue(call, out def))
                {
                    // Not registered
                }

                if (def is not null && def.AllowedCallers == AllowedCallers.AgentOnly)
                {
                    throw new A2UiMessageValidationException(
                        $"INVALID_FUNCTION_CALL: Function '{call}' has allowedCallers set to agentOnly and cannot be called by the renderer."
                    );
                }
            }

            foreach (var prop in el.EnumerateObject())
            {
                this.InspectJsonElement(prop.Value);
            }
        }
        else if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
            {
                this.InspectJsonElement(item);
            }
        }
    }

    private void InspectObjectValue(object? val)
    {
        if (val is null)
        {
            return;
        }

        if (val is DynamicValue dv)
        {
            this.InspectDynamicValue(dv);
        }
        else if (val is FunctionCallValue fc)
        {
            this.CheckFunctionCall(fc);
        }
        else if (val is JsonElement el)
        {
            this.InspectJsonElement(el);
        }
        else if (val is IReadOnlyDictionary<string, object?> dict)
        {
            foreach (var v in dict.Values)
            {
                this.InspectObjectValue(v);
            }
        }
        else if (val is IEnumerable<object?> list)
        {
            foreach (var item in list)
            {
                this.InspectObjectValue(item);
            }
        }
    }

    /// <summary>
    /// Disposes the RPC handler, cancelling all pending agent function calls.
    /// </summary>
    public void Dispose()
    {
        if (this._disposed)
        {
            return;
        }

        this._disposed = true;
        foreach (var (k, _) in this._pendingAgentCalls)
        {
            if (this._pendingAgentCalls.TryRemove(k, out var removed))
            {
                removed.Tcs.TrySetException(
                    new ObjectDisposedException(nameof(RpcHandler), "RPC_DISPOSED: RpcHandler was disposed.")
                );
            }
        }
    }

    private static RendererToAgentMessage CreateErrorResponse(string functionCallId, string code, string message) =>
        new()
        {
            Version = A2UiProtocol.Version10,
            RendererFunctionResponse = new FunctionResponse
            {
                FunctionCallId = functionCallId,
                Error = new FunctionError { Code = code, Message = message },
            },
        };
}
