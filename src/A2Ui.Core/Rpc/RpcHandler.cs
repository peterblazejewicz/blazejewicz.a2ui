using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Blazejewicz.A2Ui.Core.Bindings;
using Blazejewicz.A2Ui.Core.Components;
using Blazejewicz.A2Ui.Core.Functions;
using Blazejewicz.A2Ui.Core.Messages;
using Blazejewicz.A2Ui.Core.Surfaces;

namespace Blazejewicz.A2Ui.Core.Rpc;

/// <summary>
/// Manages catalog function definitions and bidirectional Remote Procedure Calls (RPC)
/// between the renderer and the agent per A2UI Protocol v1.0.
/// </summary>
public sealed class RpcHandler : IDisposable
{
    private static readonly ExpressionParser s_expressionParser = new();
    private readonly ConcurrentDictionary<string, FunctionDefinition> _functions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _registeredCatalogIds = new(StringComparer.Ordinal);
    private readonly FunctionRegistry _functionRegistry = FunctionRegistry.CreateDefault();
    private readonly ConcurrentDictionary<
        string,
        (string? SurfaceId, TaskCompletionSource<FunctionResponse> Tcs)
    > _pendingAgentCalls = new(StringComparer.Ordinal);
    private SurfaceManager? _surfaceManager;
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
    /// Registers a known catalog identifier so calls targeting unregistered catalogs can be distinguished from unknown functions.
    /// </summary>
    /// <param name="catalogId">The catalog identifier to register.</param>
    public void RegisterCatalog(string catalogId)
    {
        if (!string.IsNullOrEmpty(catalogId))
        {
            this._registeredCatalogIds[catalogId] = 0;
        }
    }

    /// <summary>
    /// Attempts to retrieve a registered function definition by name.
    /// </summary>
    public bool TryGetFunction(string name, [NotNullWhen(true)] out FunctionDefinition? definition) =>
        this._functions.TryGetValue(name, out definition);

    /// <summary>
    /// Registers a function definition with the RPC handler.
    /// </summary>
    /// <param name="definition">The function definition to register.</param>
    /// <param name="validateCatalogConstraints">Whether to enforce static catalog schema constraints.</param>
    public void RegisterFunction(FunctionDefinition definition, bool validateCatalogConstraints = true)
    {
        if (validateCatalogConstraints)
        {
            definition.Validate();
        }

        this._functions[definition.Name] = definition;
        if (!string.IsNullOrEmpty(definition.CatalogId))
        {
            this._registeredCatalogIds[definition.CatalogId] = 0;
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
    /// Registers all built-in A2UI Basic Catalog functions as local implementations.
    /// </summary>
    /// <param name="catalogId">Optional catalog ID to associate with the definitions.</param>
    public void RegisterBuiltInFunctions(string? catalogId = null)
    {
        var registry = FunctionRegistry.CreateDefault();
        void Reg(string name, string returnType, bool requiresUserActivation = false)
        {
            this.RegisterFunction(new FunctionDefinition
            {
                Name = name,
                CatalogId = catalogId,
                ReturnType = returnType,
                AllowedCallers = AllowedCallers.RendererOnly,
                RequiresUserActivation = requiresUserActivation,
                Implementation = args => registry.Evaluate(name, args),
            });
        }

        Reg("capitalize", "string");
        Reg("formatNumber", "string");
        Reg("formatCurrency", "string");
        Reg("formatDate", "string");
        Reg("pluralize", "string");
        Reg("add", "number");
        Reg("subtract", "number");
        Reg("multiply", "number");
        Reg("divide", "number");
        Reg("equals", "boolean");
        Reg("not_equals", "boolean");
        Reg("greater_than", "boolean");
        Reg("less_than", "boolean");
        Reg("and", "boolean");
        Reg("or", "boolean");
        Reg("not", "boolean");
        Reg("contains", "boolean");
        Reg("starts_with", "boolean");
        Reg("ends_with", "boolean");
        Reg("required", "validationResult");
        Reg("email", "validationResult");
        Reg("regex", "validationResult");
        Reg("length", "validationResult");
        Reg("numeric", "validationResult");
        Reg("openUrl", "void", requiresUserActivation: true);
    }

    /// <summary>
    /// Attaches this RPC handler to a <see cref="SurfaceManager"/>, wiring surface deletion cancellation,
    /// inbound <see cref="CallRendererFunction"/> execution, and inbound <see cref="FunctionResponse"/> correlation.
    /// </summary>
    /// <param name="surfaceManager">The surface manager to attach to.</param>
    public void AttachTo(SurfaceManager surfaceManager)
    {
        ArgumentNullException.ThrowIfNull(surfaceManager);
        this._surfaceManager = surfaceManager;
        surfaceManager.SurfaceDeleted += (_, e) => this.CancelSurfaceCalls(e.Surface.SurfaceId);
        surfaceManager.AgentFunctionResponseReceived += (_, op) =>
            this.HandleAgentFunctionResponse(
                new FunctionResponse
                {
                    FunctionCallId = op.FunctionCallId,
                    Value = op.Value,
                    Error = op.Error,
                }
            );
        surfaceManager.CallRendererFunctionReceived += (_, op) =>
        {
            var call = new CallRendererFunction
            {
                SurfaceId = op.SurfaceId,
                FunctionCallId = op.FunctionCallId,
                CallFunction = op.CallFunction,
            };
            RendererToAgentMessage response = this.HandleCallRendererFunction(call);
            this.MessageOutbound?.Invoke(response);
            if (this.MessageOutboundAsync is { } asyncHandlers)
            {
                foreach (Delegate d in asyncHandlers.GetInvocationList())
                {
                    _ = ((Func<RendererToAgentMessage, Task>)d)(response);
                }
            }
        };
    }

    /// <summary>
    /// Handles an incoming <see cref="CallRendererFunction"/> request from the agent.
    /// </summary>
    /// <param name="call">The inbound function call message.</param>
    /// <param name="dataModel">Optional surface data model for resolving dynamic arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="userActivationPresent">Whether a user activation gesture context is present.</param>
    /// <returns>A <see cref="RendererToAgentMessage"/> containing the function result or error.</returns>
    public async Task<RendererToAgentMessage> HandleCallRendererFunctionAsync(
        CallRendererFunction call,
        DataModel? dataModel = null,
        CancellationToken cancellationToken = default,
        bool userActivationPresent = false
    )
    {
        if (!this.TryPrepareRendererFunctionCall(
            call,
            dataModel,
            userActivationPresent,
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
            return CreateErrorResponse(call.FunctionCallId, "EXECUTION_ERROR", ex.Message);
        }
    }

    /// <summary>
    /// Synchronously handles an incoming <see cref="CallRendererFunction"/> request on the calling thread.
    /// </summary>
    public RendererToAgentMessage HandleCallRendererFunction(
        CallRendererFunction call,
        DataModel? dataModel = null,
        bool userActivationPresent = false
    )
    {
        if (!this.TryPrepareRendererFunctionCall(
            call,
            dataModel,
            userActivationPresent,
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
            return CreateErrorResponse(call.FunctionCallId, "EXECUTION_ERROR", ex.Message);
        }
    }

    private bool TryPrepareRendererFunctionCall(
        CallRendererFunction call,
        DataModel? dataModel,
        bool userActivationPresent,
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

        if (!string.IsNullOrEmpty(call.SurfaceId) && this._surfaceManager is not null)
        {
            Surface? surface = this._surfaceManager.GetSurface(call.SurfaceId);
            if (surface is null)
            {
                errorResponse = CreateErrorResponse(
                    call.FunctionCallId,
                    "INVALID_FUNCTION_CALL",
                    $"Surface not found: {call.SurfaceId}"
                );
                return false;
            }

            dataModel ??= surface.DataModel;
        }
        else if (string.IsNullOrEmpty(call.SurfaceId) && dataModel is null)
        {
            dataModel = new DataModel();
        }

        if (!string.IsNullOrEmpty(call.CallFunction.CatalogId))
        {
            this._functions.TryGetValue($"{call.CallFunction.CatalogId}:{funcName}", out def);
            if (def is null && !this._registeredCatalogIds.IsEmpty && !this._registeredCatalogIds.ContainsKey(call.CallFunction.CatalogId))
            {
                errorResponse = CreateErrorResponse(
                    call.FunctionCallId,
                    "INVALID_FUNCTION_CALL",
                    $"Catalog not found: {call.CallFunction.CatalogId}"
                );
                return false;
            }
        }

        if (def is null && (!this._functions.TryGetValue(funcName, out def) || (!string.IsNullOrEmpty(def.CatalogId) && !string.IsNullOrEmpty(call.CallFunction.CatalogId) && !string.Equals(def.CatalogId, call.CallFunction.CatalogId, StringComparison.Ordinal))))
        {
            def = null;
            errorResponse = CreateErrorResponse(
                call.FunctionCallId,
                "INVALID_FUNCTION_CALL",
                $"Function not found: {funcName}"
            );
            return false;
        }

        if (def.Implementation is null)
        {
            errorResponse = CreateErrorResponse(
                call.FunctionCallId,
                "INVALID_FUNCTION_CALL",
                $"Function not found: {funcName}"
            );
            return false;
        }

        if (def.AllowedCallers == AllowedCallers.RendererOnly)
        {
            errorResponse = CreateErrorResponse(
                call.FunctionCallId,
                "INVALID_FUNCTION_CALL",
                $"Function '{funcName}' cannot be called by agent (allowedCallers is rendererOnly)."
            );
            return false;
        }

        if (def.RequiresUserActivation && !userActivationPresent)
        {
            errorResponse = CreateErrorResponse(
                call.FunctionCallId,
                "INVALID_FUNCTION_CALL",
                $"Function '{funcName}' requires user activation context to execute."
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
                        DynamicValue? dv = JsonSerializer.Deserialize(
                            v,
                            A2UiJsonUtilities.V10Context.DynamicValue
                        );
                        resolvedArgs[k] = this.ResolveArgDynamicValue(dv, dataModel);
                    }
                    catch (A2UiEvaluationException ex)
                    {
                        errorResponse = CreateErrorResponse(call.FunctionCallId, "EXECUTION_ERROR", ex.Message);
                        return false;
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

    private string? ResolveArgDynamicValue(DynamicValue? dv, DataModel dataModel)
    {
        if (dv is null)
        {
            return null;
        }

        if (dv is DynamicValue.FunctionValue fv)
        {
            return this.ResolveArgFunctionCall(fv.Call, dataModel);
        }

        return dataModel.Resolve(dv);
    }

    private string? ResolveArgFunctionCall(FunctionCallValue fc, DataModel dataModel)
    {
        if (fc.Call == "@index")
        {
            throw new A2UiEvaluationException("Cannot evaluate @index at root DataContext.");
        }

        if (fc.Call == "formatString")
        {
            string? template = null;
            if (fc.Args is not null && fc.Args.TryGetValue("value", out JsonElement valEl))
            {
                if (valEl.ValueKind == JsonValueKind.String)
                {
                    template = valEl.GetString();
                }
                else
                {
                    DynamicValue? valDv = JsonSerializer.Deserialize(
                        valEl,
                        A2UiJsonUtilities.V10Context.DynamicValue
                    );
                    template = this.ResolveArgDynamicValue(valDv, dataModel);
                }
            }

            if (string.IsNullOrEmpty(template))
            {
                return string.Empty;
            }

            IReadOnlyList<ExpressionToken> tokens = s_expressionParser.Parse(template);
            var sb = new StringBuilder();
            foreach (ExpressionToken token in tokens)
            {
                string? part = token switch
                {
                    LiteralToken lit => lit.Value,
                    PathToken pt => pt.Path == "@index"
                        ? throw new A2UiEvaluationException("Cannot evaluate @index at root DataContext.")
                        : dataModel.Resolve(DynamicValue.FromPath(pt.Path)),
                    NumberToken nlt => nlt.Value.ToString("G", CultureInfo.InvariantCulture),
                    BoolToken blt => blt.Value ? "true" : "false",
                    _ => null,
                };
                if (part is not null)
                {
                    sb.Append(part);
                }
            }

            return sb.ToString();
        }

        var nestedArgs = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (fc.Args is not null)
        {
            foreach (var (k, v) in fc.Args)
            {
                DynamicValue? argDv = JsonSerializer.Deserialize(v, A2UiJsonUtilities.V10Context.DynamicValue);
                nestedArgs[k] = this.ResolveArgDynamicValue(argDv, dataModel);
            }
        }

        return this._functionRegistry.Evaluate(fc.Call, nestedArgs);
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
            if (this.MessageOutboundAsync is { } asyncHandlers)
            {
                Delegate[] invocationList = asyncHandlers.GetInvocationList();
                if (invocationList.Length == 1)
                {
                    await ((Func<RendererToAgentMessage, Task>)invocationList[0])(msg).ConfigureAwait(false);
                }
                else
                {
                    var tasks = new Task[invocationList.Length];
                    for (int i = 0; i < invocationList.Length; i++)
                    {
                        tasks[i] = ((Func<RendererToAgentMessage, Task>)invocationList[i])(msg);
                    }

                    await Task.WhenAll(tasks).ConfigureAwait(false);
                }
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
