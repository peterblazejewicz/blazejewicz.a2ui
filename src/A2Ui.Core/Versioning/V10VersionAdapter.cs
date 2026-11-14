using System.Text.Json;
using Blazejewicz.A2Ui.Core.Components;
using Blazejewicz.A2Ui.Core.Messages;
using Blazejewicz.A2Ui.Core.Validation;

namespace Blazejewicz.A2Ui.Core.Versioning;

/// <summary>
/// Version adapter for A2UI v1.0 protocol payloads.
/// </summary>
public sealed class V10VersionAdapter : IVersionAdapter
{
    /// <inheritdoc />
    public string Version => A2UiProtocol.Version10;

    /// <inheritdoc />
    public InternalOperation AdaptAgentToRenderer(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return this.AdaptAgentToRenderer(doc.RootElement);
    }

    /// <summary>
    /// Adapts an in-memory <see cref="AgentToRendererMessage"/> instance into an internal operation
    /// without re-serializing through JSON (preserving explicit <c>null</c> values on <c>updateDataModel</c>).
    /// </summary>
    public InternalOperation AdaptAgentToRenderer(AgentToRendererMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.Validate();

        if (message.CreateSurface is { } cs)
        {
            return MapCreateSurfaceOp(cs);
        }

        if (message.UpdateComponents is { } uc)
        {
            return MapUpdateComponentsOp(uc);
        }

        if (message.UpdateDataModel is { } ud)
        {
            return MapUpdateDataModelOp(ud);
        }

        if (message.DeleteSurface is { } ds)
        {
            return MapDeleteSurfaceOp(ds);
        }

        if (message.CallRendererFunction is { } crf)
        {
            return MapCallRendererFunctionOp(crf);
        }

        if (message.AgentFunctionResponse is { } afr)
        {
            return MapAgentFunctionResponseOp(afr);
        }

        throw new A2UiMessageValidationException("No valid operation found in AgentToRendererMessage.");
    }

    /// <inheritdoc />
    public InternalOperation AdaptAgentToRenderer(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new A2UiMessageValidationException("Message envelope must be a JSON object.");
        }

        if (
            !root.TryGetProperty("version", out var vEl)
            || vEl.ValueKind != JsonValueKind.String
            || vEl.GetString() != A2UiProtocol.Version10
        )
        {
            string actual =
                vEl.ValueKind == JsonValueKind.String ? vEl.GetString() ?? "(null)"
                : vEl.ValueKind == JsonValueKind.Undefined ? "(missing)"
                : vEl.GetRawText();
            throw new A2UiMessageValidationException(
                $"Unsupported version '{actual}'; expected '{A2UiProtocol.Version10}'."
            );
        }

        AgentToRendererMessage message;
        try
        {
            message =
                JsonSerializer.Deserialize(root, A2UiJsonUtilities.V10Context.AgentToRendererMessage)
                ?? throw new A2UiMessageValidationException("Failed to deserialize AgentToRendererMessage from JSON.");
        }
        catch (JsonException ex)
        {
            throw new A2UiMessageValidationException($"Invalid v1.0 message JSON: {ex.Message}", ex);
        }

        message.Validate();

        if (root.TryGetProperty("createSurface", out var csElement) && csElement.ValueKind == JsonValueKind.Object)
        {
            return MapCreateSurfaceOp(message.CreateSurface!, csElement);
        }

        if (root.TryGetProperty("updateComponents", out var ucElement) && ucElement.ValueKind == JsonValueKind.Object)
        {
            return MapUpdateComponentsOp(message.UpdateComponents!, ucElement);
        }

        if (root.TryGetProperty("updateDataModel", out var udElement) && udElement.ValueKind == JsonValueKind.Object)
        {
            if (!udElement.TryGetProperty("value", out var valProp))
            {
                throw new A2UiMessageValidationException("Property 'value' is required on v1.0 updateDataModel.");
            }

            return MapUpdateDataModelOp(message.UpdateDataModel!, valProp);
        }

        if (root.TryGetProperty("deleteSurface", out var dsElement) && dsElement.ValueKind == JsonValueKind.Object)
        {
            return MapDeleteSurfaceOp(message.DeleteSurface!);
        }

        if (
            root.TryGetProperty("callRendererFunction", out var crfElement)
            && crfElement.ValueKind == JsonValueKind.Object
        )
        {
            return MapCallRendererFunctionOp(message.CallRendererFunction!);
        }

        if (
            root.TryGetProperty("agentFunctionResponse", out var afrElement)
            && afrElement.ValueKind == JsonValueKind.Object
        )
        {
            return MapAgentFunctionResponseOp(message.AgentFunctionResponse!);
        }

        throw new A2UiMessageValidationException("No valid operation found in AgentToRendererMessage.");
    }

    private static CreateSurfaceOp MapCreateSurfaceOp(CreateSurface cs, JsonElement csElement = default)
    {
        if (cs.Theme is { } themeEl && themeEl.ValueKind != JsonValueKind.Null)
        {
            throw new A2UiMessageValidationException("Property 'theme' is forbidden in v1.0 createSurface.");
        }

        if (csElement.ValueKind == JsonValueKind.Object)
        {
            if (csElement.TryGetProperty("theme", out var rawThemeEl) && rawThemeEl.ValueKind != JsonValueKind.Null)
            {
                throw new A2UiMessageValidationException("Property 'theme' is forbidden in v1.0 createSurface.");
            }

            foreach (var prop in csElement.EnumerateObject())
            {
                if (
                    prop.Name
                    is not (
                        "surfaceId"
                        or "catalogId"
                        or "sendDataModel"
                        or "components"
                        or "dataModel"
                        or "metadata"
                    )
                )
                {
                    throw new A2UiMessageValidationException(
                        $"Unexpected property '{prop.Name}' in v1.0 createSurface."
                    );
                }
            }
        }

        if (cs.DataModel is { } dmEl && dmEl.ValueKind != JsonValueKind.Null)
        {
            if (dmEl.ValueKind != JsonValueKind.Object)
            {
                throw new A2UiMessageValidationException(
                    "Property 'dataModel' in v1.0 createSurface must be a JSON object."
                );
            }

            UpdateDataModel.ValidateNoReservedKeys(dmEl);
        }

        if (cs.Components is { } comps)
        {
            ValidateComponentsV10(comps, csElement);
        }
        else if (
            csElement.ValueKind == JsonValueKind.Object
            && csElement.TryGetProperty("components", out var rawComps)
            && rawComps.ValueKind != JsonValueKind.Null
        )
        {
            throw new A2UiMessageValidationException(
                "Property 'components' in v1.0 createSurface must be an array."
            );
        }

        return new CreateSurfaceOp
        {
            SurfaceId = cs.SurfaceId,
            CatalogId = cs.CatalogId,
            Components = cs.Components,
            DataModel = cs.DataModel?.Clone(),
            Theme = null,
            SendDataModel = cs.SendDataModel ?? false,
            Metadata = cs.Metadata,
            EnforceUniqueness = true,
            ProtocolVersion = A2UiProtocol.Version10,
        };
    }

    private static UpdateComponentsOp MapUpdateComponentsOp(UpdateComponents uc, JsonElement ucElement = default)
    {
        ValidateComponentsV10(uc.Components, ucElement);
        return new UpdateComponentsOp
        {
            SurfaceId = uc.SurfaceId,
            Components = uc.Components,
            ProtocolVersion = A2UiProtocol.Version10,
        };
    }

    private static UpdateDataModelOp MapUpdateDataModelOp(UpdateDataModel ud, JsonElement? rawValue = null)
    {
        JsonElement? effectiveValue = rawValue ?? ud.Value;
        if (effectiveValue is { } valEl)
        {
            UpdateDataModel.ValidateNoReservedKeys(valEl);
        }

        bool isNullVal = effectiveValue is null || effectiveValue.Value.ValueKind == JsonValueKind.Null;
        bool deleteOnNull = isNullVal && !string.IsNullOrEmpty(ud.Path) && ud.Path != "/";

        return new UpdateDataModelOp
        {
            SurfaceId = ud.SurfaceId,
            Path = ud.Path,
            Value = effectiveValue?.Clone(),
            DeleteOnNull = deleteOnNull,
            ProtocolVersion = A2UiProtocol.Version10,
        };
    }

    private static DeleteSurfaceOp MapDeleteSurfaceOp(DeleteSurface ds) =>
        new() { SurfaceId = ds.SurfaceId, ProtocolVersion = A2UiProtocol.Version10 };

    private static CallRendererFunctionOp MapCallRendererFunctionOp(CallRendererFunction crf)
    {
        crf.Validate();
        return new CallRendererFunctionOp
        {
            SurfaceId = crf.SurfaceId,
            FunctionCallId = crf.FunctionCallId,
            CallFunction = crf.CallFunction,
        };
    }

    private static AgentFunctionResponseOp MapAgentFunctionResponseOp(FunctionResponse afr)
    {
        afr.Validate();
        return new AgentFunctionResponseOp
        {
            FunctionCallId = afr.FunctionCallId,
            Value = afr.Value?.Clone(),
            Error = afr.Error,
        };
    }

    private static void ValidateComponentsV10(IReadOnlyList<A2UiComponent> components, JsonElement parentElement)
    {
        if (components.Count == 0)
        {
            throw new A2UiMessageValidationException("Components list cannot be empty.");
        }

        JsonElement? rawCompsArray = null;
        if (
            parentElement.ValueKind == JsonValueKind.Object
            && parentElement.TryGetProperty("components", out var ca)
            && ca.ValueKind == JsonValueKind.Array
        )
        {
            rawCompsArray = ca;
        }

        for (int i = 0; i < components.Count; i++)
        {
            var comp = components[i];
            if (comp is null)
            {
                throw new A2UiMessageValidationException("Component in 'components' array cannot be null.");
            }

            Uax31Validator.ValidateIdentifier(comp.Id, "Component Id");
            Uax31Validator.ValidateComponentName(comp.Component);
            comp.Metadata?.Validate();

            if (comp.ExtensionData is not null)
            {
                foreach (var k in comp.ExtensionData.Keys)
                {
                    Uax31Validator.ValidateExtensionKey(k);
                }

                if (comp is not GenericComponent && comp.ExtensionData.Count > 0)
                {
                    throw new A2UiMessageValidationException(
                        $"Component '{comp.Id}': Unexpected property '{comp.ExtensionData.Keys.First()}' on {comp.Component} envelope."
                    );
                }
            }

            bool isTextField =
                comp is TextFieldComponent
                || string.Equals(comp.Component, "TextField", StringComparison.OrdinalIgnoreCase);

            if (isTextField)
            {
                JsonElement? rawComp =
                    rawCompsArray is not null && rawCompsArray.Value.GetArrayLength() > i
                        ? rawCompsArray.Value[i]
                        : null;

                if (rawComp?.TryGetProperty("validationRegexp", out _) == true)
                {
                    throw new A2UiMessageValidationException(
                        $"Component '{comp.Id}': Property 'validationRegexp' on TextField is forbidden in v1.0. Use 'checks' instead."
                    );
                }

                if (comp is TextFieldComponent tf && tf.ValidationRegexp is not null)
                {
                    throw new A2UiMessageValidationException(
                        $"Component '{comp.Id}': Property 'validationRegexp' on TextField is forbidden in v1.0. Use 'checks' instead."
                    );
                }

                bool hasValue = false;
                if (comp is TextFieldComponent tfComp)
                {
                    hasValue = tfComp.Value is not null;
                }
                else if (comp is GenericComponent gc)
                {
                    hasValue = gc.Properties.ContainsKey("value");
                }
                else if (rawComp?.TryGetProperty("value", out _) == true)
                {
                    hasValue = true;
                }

                if (!hasValue)
                {
                    throw new A2UiMessageValidationException(
                        $"Component '{comp.Id}': TextField requires 'value' property in v1.0."
                    );
                }
            }
        }
    }
}
