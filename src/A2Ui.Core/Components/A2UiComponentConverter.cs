using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;
using Blazejewicz.A2Ui.Core.Accessibility;
using Blazejewicz.A2Ui.Core.Actions;
using Blazejewicz.A2Ui.Core.Bindings;
using Blazejewicz.A2Ui.Core.Children;
using Blazejewicz.A2Ui.Core.Choices;
using Blazejewicz.A2Ui.Core.Components.Extensions;
using Blazejewicz.A2Ui.Core.Messages;
using Blazejewicz.A2Ui.Core.Tabs;
using Blazejewicz.A2Ui.Core.Validation;

namespace Blazejewicz.A2Ui.Core.Components;

/// <summary>
/// Custom polymorphic JSON converter for <see cref="A2UiComponent"/>.
/// Dispatches known Basic Catalog types and project extensions to their concrete types,
/// and deserializes any other valid component type into <see cref="GenericComponent"/>.
/// </summary>
/// <param name="fixedProtocolVersion">Optional fixed protocol version overriding options and ambient context.</param>
public sealed class A2UiComponentConverter(string? fixedProtocolVersion) : JsonConverter<A2UiComponent>
{
    private readonly string? _fixedProtocolVersion = fixedProtocolVersion;

    /// <summary>Initializes a new instance of the <see cref="A2UiComponentConverter"/> class.</summary>
    public A2UiComponentConverter()
        : this(null) { }

    /// <inheritdoc />
    public override A2UiComponent? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        using IDisposable? scope =
            this._fixedProtocolVersion is not null && A2UiJsonUtilities.GetProtocolVersion(options) is null
                ? DynamicValueConverter.EnterVersionScope(this._fixedProtocolVersion)
                : null;

        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("A2UiComponent must be a JSON object.");
        }

        if (!root.TryGetProperty("component", out var compProp) || compProp.ValueKind != JsonValueKind.String)
        {
            throw new JsonException("A2UiComponent missing required 'component' discriminator.");
        }

        string componentType = compProp.GetString()!;

        // Common properties across all A2UI components
        if (
            !root.TryGetProperty("id", out var idProp)
            || idProp.ValueKind != JsonValueKind.String
            || string.IsNullOrEmpty(idProp.GetString())
        )
        {
            throw new JsonException("A2UiComponent missing required non-empty 'id' property.");
        }

        string id = idProp.GetString()!;
        bool isV10 =
            DynamicValueConverter.ResolveProtocolVersion(this._fixedProtocolVersion, options) == A2UiProtocol.Version10;
        if (isV10)
        {
            Uax31Validator.ValidateIdentifier(id, "Component ID");
            Uax31Validator.ValidateComponentName(componentType);
        }

        string? catalogId = root.TryGetProperty("catalogId", out var catProp) ? catProp.GetString() : null;
        double? weight = root.TryGetProperty("weight", out var wProp) && wProp.TryGetDouble(out var w) ? w : null;
        string? parent = root.TryGetProperty("parent", out var pProp) ? pProp.GetString() : null;
        string? child = root.TryGetProperty("child", out var cProp) ? cProp.GetString() : null;

        AccessibilityAttributes? accessibility = null;
        if (root.TryGetProperty("accessibility", out var accProp))
        {
            accessibility = accProp.Deserialize<AccessibilityAttributes>(options);
        }

        ComponentMetadata? metadata = null;
        if (root.TryGetProperty("metadata", out var metaProp))
        {
            metadata = metaProp.Deserialize<ComponentMetadata>(options);
        }

        ChildList? children = null;
        if (root.TryGetProperty("children", out var childrenProp))
        {
            children = childrenProp.Deserialize<ChildList>(options);
        }

        CheckRule[]? checks = null;
        if (root.TryGetProperty("checks", out var checksProp))
        {
            checks = checksProp.Deserialize<CheckRule[]>(options);
        }

        DynamicValue? text = null;
        if (root.TryGetProperty("text", out var textProp))
        {
            text = textProp.Deserialize<DynamicValue>(options);
        }

        DynamicValue? label = null;
        if (root.TryGetProperty("label", out var labelProp))
        {
            label = labelProp.Deserialize<DynamicValue>(options);
        }

        if (isV10)
        {
            metadata?.Validate();
        }

        var component = DeserializeConcreteComponent(
            componentType,
            root,
            id,
            catalogId,
            weight,
            parent,
            child,
            children,
            accessibility,
            metadata,
            checks,
            text,
            label,
            options,
            isV10
        );

        var consumed = GetConsumedProperties(componentType);
        Dictionary<string, JsonElement>? extData = null;
        foreach (var prop in root.EnumerateObject())
        {
            if (consumed.Contains(prop.Name))
            {
                continue;
            }

            if (isV10)
            {
                if (prop.Name.StartsWith("a2ui_", StringComparison.Ordinal))
                {
                    throw new JsonException(
                        $"Component '{id}': Extension property '{prop.Name}' cannot start with reserved prefix 'a2ui_'."
                    );
                }

                if (!Uax31Validator.IsValidIdentifier(prop.Name))
                {
                    throw new JsonException(
                        $"Component '{id}': Extension property '{prop.Name}' does not conform to UAX 31 identifier syntax."
                    );
                }
            }

            extData ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            extData[prop.Name] = prop.Value.Clone();
        }

        if (component is not GenericComponent && extData is not null)
        {
            component = component with { ExtensionData = extData };
        }

        return component;
    }

    private static A2UiComponent DeserializeConcreteComponent(
        string componentType,
        JsonElement root,
        string id,
        string? catalogId,
        double? weight,
        string? parent,
        string? child,
        ChildList? children,
        AccessibilityAttributes? accessibility,
        ComponentMetadata? metadata,
        CheckRule[]? checks,
        DynamicValue? text,
        DynamicValue? label,
        JsonSerializerOptions options,
        bool isV10
    )
    {
        switch (componentType)
        {
            case "Text":
                return new TextComponent
                {
                    Id = id,
                    CatalogId = catalogId,
                    Weight = weight,
                    Parent = parent,
                    Child = child,
                    Children = children,
                    Accessibility = accessibility,
                    Metadata = metadata,
                    Checks = checks,
                    Text = text,
                    Label = label,
                    Variant = root.TryGetProperty("variant", out var tv) ? tv.GetString() : null,
                    Description = root.TryGetProperty("description", out var td)
                        ? td.Deserialize<DynamicValue>(options)
                        : null,
                };

            case "Button":
                return new ButtonComponent
                {
                    Id = id,
                    CatalogId = catalogId,
                    Weight = weight,
                    Parent = parent,
                    Child = child,
                    Children = children,
                    Accessibility = accessibility,
                    Metadata = metadata,
                    Checks = checks,
                    Text = text,
                    Label = label,
                    Variant = root.TryGetProperty("variant", out var bv) ? bv.GetString() : null,
                    Action = root.TryGetProperty("action", out var ba)
                        ? ba.Deserialize<ComponentAction>(options)
                        : null,
                };

            case "Column":
                return new ColumnComponent
                {
                    Id = id,
                    CatalogId = catalogId,
                    Weight = weight,
                    Parent = parent,
                    Child = child,
                    Children = children,
                    Accessibility = accessibility,
                    Metadata = metadata,
                    Checks = checks,
                    Text = text,
                    Label = label,
                    Justify = root.TryGetProperty("justify", out var colJ) ? colJ.GetString() : null,
                    Align = root.TryGetProperty("align", out var colA) ? colA.GetString() : null,
                    Direction = root.TryGetProperty("direction", out var colD) ? colD.GetString() : null,
                };

            case "Row":
                return new RowComponent
                {
                    Id = id,
                    CatalogId = catalogId,
                    Weight = weight,
                    Parent = parent,
                    Child = child,
                    Children = children,
                    Accessibility = accessibility,
                    Metadata = metadata,
                    Checks = checks,
                    Text = text,
                    Label = label,
                    Justify = root.TryGetProperty("justify", out var rowJ) ? rowJ.GetString() : null,
                    Align = root.TryGetProperty("align", out var rowA) ? rowA.GetString() : null,
                    Direction = root.TryGetProperty("direction", out var rowD) ? rowD.GetString() : null,
                };

            case "Card":
                return new CardComponent
                {
                    Id = id,
                    CatalogId = catalogId,
                    Weight = weight,
                    Parent = parent,
                    Child = child,
                    Children = children,
                    Accessibility = accessibility,
                    Metadata = metadata,
                    Checks = checks,
                    Text = text,
                    Label = label,
                };

            case "Icon":
                return new IconComponent
                {
                    Id = id,
                    CatalogId = catalogId,
                    Weight = weight,
                    Parent = parent,
                    Child = child,
                    Children = children,
                    Accessibility = accessibility,
                    Metadata = metadata,
                    Checks = checks,
                    Text = text,
                    Label = label,
                    Name = root.TryGetProperty("name", out var iname) ? iname.Deserialize<DynamicValue>(options) : null,
                };

            case "Divider":
                return new DividerComponent
                {
                    Id = id,
                    CatalogId = catalogId,
                    Weight = weight,
                    Parent = parent,
                    Child = child,
                    Children = children,
                    Accessibility = accessibility,
                    Metadata = metadata,
                    Checks = checks,
                    Text = text,
                    Label = label,
                    Axis = root.TryGetProperty("axis", out var dAxis) ? dAxis.GetString() : null,
                };

            case "Image":
                return new ImageComponent
                {
                    Id = id,
                    CatalogId = catalogId,
                    Weight = weight,
                    Parent = parent,
                    Child = child,
                    Children = children,
                    Accessibility = accessibility,
                    Metadata = metadata,
                    Checks = checks,
                    Text = text,
                    Label = label,
                    Url = root.TryGetProperty("url", out var iu) ? iu.Deserialize<DynamicValue>(options) : null,
                    Description = root.TryGetProperty("description", out var idesc)
                        ? idesc.Deserialize<DynamicValue>(options)
                        : null,
                    Fit = root.TryGetProperty("fit", out var ifit) ? ifit.GetString() : null,
                    Variant = root.TryGetProperty("variant", out var iv) ? iv.GetString() : null,
                };

            case "Video":
                return new VideoComponent
                {
                    Id = id,
                    CatalogId = catalogId,
                    Weight = weight,
                    Parent = parent,
                    Child = child,
                    Children = children,
                    Accessibility = accessibility,
                    Metadata = metadata,
                    Checks = checks,
                    Text = text,
                    Label = label,
                    Url = root.TryGetProperty("url", out var vu) ? vu.Deserialize<DynamicValue>(options) : null,
                    PosterUrl = root.TryGetProperty("posterUrl", out var vpu)
                        ? vpu.Deserialize<DynamicValue>(options)
                        : null,
                };

            case "AudioPlayer":
                return new AudioPlayerComponent
                {
                    Id = id,
                    CatalogId = catalogId,
                    Weight = weight,
                    Parent = parent,
                    Child = child,
                    Children = children,
                    Accessibility = accessibility,
                    Metadata = metadata,
                    Checks = checks,
                    Text = text,
                    Label = label,
                    Url = root.TryGetProperty("url", out var au) ? au.Deserialize<DynamicValue>(options) : null,
                    Description = root.TryGetProperty("description", out var adesc)
                        ? adesc.Deserialize<DynamicValue>(options)
                        : null,
                };

            case "List":
                return new ListComponent
                {
                    Id = id,
                    CatalogId = catalogId,
                    Weight = weight,
                    Parent = parent,
                    Child = child,
                    Children = children,
                    Accessibility = accessibility,
                    Metadata = metadata,
                    Checks = checks,
                    Text = text,
                    Label = label,
                    Direction = root.TryGetProperty("direction", out var ld) ? ld.GetString() : null,
                    Align = root.TryGetProperty("align", out var la) ? la.GetString() : null,
                    ListStyle = root.TryGetProperty("listStyle", out var lls) ? lls.GetString() : null,
                };

            case "Tabs":
                return new TabsComponent
                {
                    Id = id,
                    CatalogId = catalogId,
                    Weight = weight,
                    Parent = parent,
                    Child = child,
                    Children = children,
                    Accessibility = accessibility,
                    Metadata = metadata,
                    Checks = checks,
                    Text = text,
                    Label = label,
                    Tabs = root.TryGetProperty("tabs", out var tt) ? tt.Deserialize<TabDefinition[]>(options) : null,
                };

            case "Modal":
                return new ModalComponent
                {
                    Id = id,
                    CatalogId = catalogId,
                    Weight = weight,
                    Parent = parent,
                    Child = child,
                    Children = children,
                    Accessibility = accessibility,
                    Metadata = metadata,
                    Checks = checks,
                    Text = text,
                    Label = label,
                    Trigger = root.TryGetProperty("trigger", out var mt) ? mt.GetString() : null,
                    Content = root.TryGetProperty("content", out var mc) ? mc.GetString() : null,
                };

            case "TextField":
                return new TextFieldComponent
                {
                    Id = id,
                    CatalogId = catalogId,
                    Weight = weight,
                    Parent = parent,
                    Child = child,
                    Children = children,
                    Accessibility = accessibility,
                    Metadata = metadata,
                    Checks = checks,
                    Text = text,
                    Label = label,
                    Value = root.TryGetProperty("value", out var tvVal)
                        ? tvVal.Deserialize<DynamicValue>(options)
                        : null,
                    Placeholder = root.TryGetProperty("placeholder", out var tfp)
                        ? tfp.Deserialize<DynamicValue>(options)
                        : null,
                    ValidationRegexp = root.TryGetProperty("validationRegexp", out var tvr) ? tvr.GetString() : null,
                    Rows = root.TryGetProperty("rows", out var tfRows)
                        ? tfRows.Deserialize<DynamicValue>(options)
                        : null,
                    Variant = root.TryGetProperty("variant", out var tfVar) ? tfVar.GetString() : null,
                };

            case "DateTimeInput":
                return new DateTimeInputComponent
                {
                    Id = id,
                    CatalogId = catalogId,
                    Weight = weight,
                    Parent = parent,
                    Child = child,
                    Children = children,
                    Accessibility = accessibility,
                    Metadata = metadata,
                    Checks = checks,
                    Text = text,
                    Label = label,
                    Value = root.TryGetProperty("value", out var dtv) ? dtv.Deserialize<DynamicValue>(options) : null,
                    EnableDate =
                        root.TryGetProperty("enableDate", out var ed) && ed.ValueKind != JsonValueKind.Null
                            ? ed.GetBoolean()
                            : null,
                    EnableTime =
                        root.TryGetProperty("enableTime", out var et) && et.ValueKind != JsonValueKind.Null
                            ? et.GetBoolean()
                            : null,
                    Min = root.TryGetProperty("min", out var dtmin) ? dtmin.Deserialize<DynamicValue>(options) : null,
                    Max = root.TryGetProperty("max", out var dtmax) ? dtmax.Deserialize<DynamicValue>(options) : null,
                };

            case "ChoicePicker":
                return new ChoicePickerComponent
                {
                    Id = id,
                    CatalogId = catalogId,
                    Weight = weight,
                    Parent = parent,
                    Child = child,
                    Children = children,
                    Accessibility = accessibility,
                    Metadata = metadata,
                    Checks = checks,
                    Text = text,
                    Label = label,
                    Value = root.TryGetProperty("value", out var cpv) ? cpv.Deserialize<DynamicValue>(options) : null,
                    Options = root.TryGetProperty("options", out var cpo)
                        ? cpo.Deserialize<ChoiceOption[]>(options)
                        : null,
                    DisplayStyle = root.TryGetProperty("displayStyle", out var cpds) ? cpds.GetString() : null,
                    Filterable =
                        root.TryGetProperty("filterable", out var cpf) && cpf.ValueKind != JsonValueKind.Null
                            ? cpf.GetBoolean()
                            : null,
                    Variant = root.TryGetProperty("variant", out var cpvar) ? cpvar.GetString() : null,
                };

            case "CheckBox":
                return new CheckBoxComponent
                {
                    Id = id,
                    CatalogId = catalogId,
                    Weight = weight,
                    Parent = parent,
                    Child = child,
                    Children = children,
                    Accessibility = accessibility,
                    Metadata = metadata,
                    Checks = checks,
                    Text = text,
                    Label = label,
                    Value = root.TryGetProperty("value", out var cbv) ? cbv.Deserialize<DynamicValue>(options) : null,
                };

            case "Slider":
                return new SliderComponent
                {
                    Id = id,
                    CatalogId = catalogId,
                    Weight = weight,
                    Parent = parent,
                    Child = child,
                    Children = children,
                    Accessibility = accessibility,
                    Metadata = metadata,
                    Checks = checks,
                    Text = text,
                    Label = label,
                    Value = root.TryGetProperty("value", out var sv) ? sv.Deserialize<DynamicValue>(options) : null,
                    Min = root.TryGetProperty("min", out var smin) ? smin.Deserialize<DynamicValue>(options) : null,
                    Max = root.TryGetProperty("max", out var smax) ? smax.Deserialize<DynamicValue>(options) : null,
                    Steps = root.TryGetProperty("steps", out var sst) ? sst.Deserialize<DynamicValue>(options) : null,
                };

            case "Table":
                return new TableComponent
                {
                    Id = id,
                    CatalogId = catalogId,
                    Weight = weight,
                    Parent = parent,
                    Child = child,
                    Children = children,
                    Accessibility = accessibility,
                    Metadata = metadata,
                    Checks = checks,
                    Text = text,
                    Label = label,
                    Columns = root.TryGetProperty("columns", out var tcCols)
                        ? tcCols.Deserialize<TableColumn[]>(options)
                        : null,
                    Rows = root.TryGetProperty("rows", out var tcRows)
                        ? tcRows.Deserialize<DynamicValue>(options)
                        : null,
                };

            case "Surface":
                return new SurfaceComponent
                {
                    Id = id,
                    CatalogId = catalogId,
                    Weight = weight,
                    Parent = parent,
                    Child = child,
                    Children = children,
                    Accessibility = accessibility,
                    Metadata = metadata,
                    Checks = checks,
                    Text = text,
                    Label = label,
                };

            default:
                var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                foreach (var prop in root.EnumerateObject())
                {
                    if (isV10 && !s_commonProperties.Contains(prop.Name))
                    {
                        if (prop.Name.StartsWith("a2ui_", StringComparison.Ordinal))
                        {
                            throw new JsonException(
                                $"Component '{id}': Extension property '{prop.Name}' cannot start with reserved prefix 'a2ui_'."
                            );
                        }

                        if (!Uax31Validator.IsValidIdentifier(prop.Name))
                        {
                            throw new JsonException(
                                $"Component '{id}': Extension property '{prop.Name}' does not conform to UAX 31 identifier syntax."
                            );
                        }

                        if (prop.Value.ValueKind != JsonValueKind.Null)
                        {
                            _ = prop.Value.Deserialize<DynamicValue>(options);
                        }
                    }

                    properties[prop.Name] = prop.Value.Clone();
                }

                return new GenericComponent
                {
                    Id = id,
                    ComponentType = componentType,
                    CatalogId = catalogId,
                    Weight = weight,
                    Parent = parent,
                    Child = child,
                    Children = children,
                    Accessibility = accessibility,
                    Metadata = metadata,
                    Checks = checks,
                    Text = text,
                    Label = label,
                    Properties = properties,
                };
        }
    }

    private static readonly FrozenSet<string> s_commonProperties = FrozenSet.ToFrozenSet(
        [
            "component",
            "id",
            "catalogId",
            "weight",
            "parent",
            "child",
            "children",
            "accessibility",
            "metadata",
            "checks",
            "text",
            "label",
        ],
        StringComparer.Ordinal
    );

    private static HashSet<string> GetConsumedProperties(string componentType)
    {
        var set = new HashSet<string>(s_commonProperties, StringComparer.Ordinal);
        switch (componentType)
        {
            case "Text":
                set.Add("variant");
                set.Add("description");
                break;
            case "Button":
                set.Add("variant");
                set.Add("action");
                break;
            case "Column":
            case "Row":
                set.Add("justify");
                set.Add("align");
                set.Add("direction");
                break;
            case "List":
                set.Add("justify");
                set.Add("align");
                set.Add("direction");
                set.Add("listStyle");
                break;
            case "Icon":
                set.Add("name");
                break;
            case "Divider":
                set.Add("axis");
                break;
            case "Image":
                set.Add("url");
                set.Add("description");
                set.Add("fit");
                set.Add("variant");
                break;
            case "Video":
            case "AudioPlayer":
                set.Add("url");
                set.Add("description");
                break;
            case "Tabs":
                set.Add("tabs");
                set.Add("activeTab");
                break;
            case "Modal":
                set.Add("trigger");
                set.Add("content");
                set.Add("isOpen");
                break;
            case "TextField":
                set.Add("value");
                set.Add("placeholder");
                set.Add("variant");
                set.Add("action");
                set.Add("description");
                set.Add("validationRegexp");
                break;
            case "DateTimeInput":
                set.Add("value");
                set.Add("min");
                set.Add("max");
                set.Add("enableTime");
                set.Add("enableDate");
                set.Add("variant");
                break;
            case "ChoicePicker":
                set.Add("choices");
                set.Add("selectedId");
                set.Add("multiSelect");
                set.Add("variant");
                set.Add("value");
                set.Add("options");
                set.Add("displayStyle");
                set.Add("filterable");
                break;
            case "CheckBox":
                set.Add("value");
                break;
            case "Slider":
                set.Add("value");
                set.Add("min");
                set.Add("max");
                set.Add("steps");
                break;
            case "Table":
                set.Add("columns");
                set.Add("rows");
                break;
        }

        return set;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, A2UiComponent value, JsonSerializerOptions options)
    {
        using IDisposable? scope =
            this._fixedProtocolVersion is not null && A2UiJsonUtilities.GetProtocolVersion(options) is null
                ? DynamicValueConverter.EnterVersionScope(this._fixedProtocolVersion)
                : null;

        writer.WriteStartObject();
        writer.WriteString("component", value.Component);
        writer.WriteString("id", value.Id);

        if (!string.IsNullOrEmpty(value.CatalogId))
        {
            writer.WriteString("catalogId", value.CatalogId);
        }
        if (value.Weight.HasValue)
        {
            writer.WriteNumber("weight", value.Weight.Value);
        }
        if (!string.IsNullOrEmpty(value.Parent))
        {
            writer.WriteString("parent", value.Parent);
        }
        if (!string.IsNullOrEmpty(value.Child))
        {
            writer.WriteString("child", value.Child);
        }
        if (value.Children is not null)
        {
            writer.WritePropertyName("children");
            JsonSerializer.Serialize(writer, value.Children, options);
        }
        if (value.Accessibility is not null)
        {
            writer.WritePropertyName("accessibility");
            JsonSerializer.Serialize(writer, value.Accessibility, options);
        }
        if (value.Metadata is not null)
        {
            writer.WritePropertyName("metadata");
            JsonSerializer.Serialize(writer, value.Metadata, options);
        }
        if (value.Checks is not null)
        {
            writer.WritePropertyName("checks");
            JsonSerializer.Serialize(writer, value.Checks, options);
        }
        if (value.Text is not null)
        {
            writer.WritePropertyName("text");
            JsonSerializer.Serialize(writer, value.Text, options);
        }
        if (value.Label is not null)
        {
            writer.WritePropertyName("label");
            JsonSerializer.Serialize(writer, value.Label, options);
        }

        switch (value)
        {
            case TextComponent tc:
                if (!string.IsNullOrEmpty(tc.Variant))
                {
                    writer.WriteString("variant", tc.Variant);
                }

                if (tc.Description is not null)
                {
                    writer.WritePropertyName("description");
                    JsonSerializer.Serialize(writer, tc.Description, options);
                }
                break;

            case ColumnComponent col:
                if (!string.IsNullOrEmpty(col.Justify))
                {
                    writer.WriteString("justify", col.Justify);
                }

                if (!string.IsNullOrEmpty(col.Align))
                {
                    writer.WriteString("align", col.Align);
                }

                if (!string.IsNullOrEmpty(col.Direction))
                {
                    writer.WriteString("direction", col.Direction);
                }

                break;

            case RowComponent row:
                if (!string.IsNullOrEmpty(row.Justify))
                {
                    writer.WriteString("justify", row.Justify);
                }

                if (!string.IsNullOrEmpty(row.Align))
                {
                    writer.WriteString("align", row.Align);
                }

                if (!string.IsNullOrEmpty(row.Direction))
                {
                    writer.WriteString("direction", row.Direction);
                }

                break;

            case DividerComponent dc:
                if (!string.IsNullOrEmpty(dc.Axis))
                {
                    writer.WriteString("axis", dc.Axis);
                }

                break;

            case ListComponent lc:
                if (!string.IsNullOrEmpty(lc.Direction))
                {
                    writer.WriteString("direction", lc.Direction);
                }

                if (!string.IsNullOrEmpty(lc.Align))
                {
                    writer.WriteString("align", lc.Align);
                }

                if (!string.IsNullOrEmpty(lc.ListStyle))
                {
                    writer.WriteString("listStyle", lc.ListStyle);
                }

                break;

            case ButtonComponent bc:
                if (!string.IsNullOrEmpty(bc.Variant))
                {
                    writer.WriteString("variant", bc.Variant);
                }

                if (bc.Action is not null)
                {
                    writer.WritePropertyName("action");
                    JsonSerializer.Serialize(writer, bc.Action, options);
                }
                break;

            case IconComponent ic:
                if (ic.Name is not null)
                {
                    writer.WritePropertyName("name");
                    JsonSerializer.Serialize(writer, ic.Name, options);
                }
                break;

            case ImageComponent imc:
                if (imc.Url is not null)
                {
                    writer.WritePropertyName("url");
                    JsonSerializer.Serialize(writer, imc.Url, options);
                }
                if (imc.Description is not null)
                {
                    writer.WritePropertyName("description");
                    JsonSerializer.Serialize(writer, imc.Description, options);
                }
                if (!string.IsNullOrEmpty(imc.Fit))
                {
                    writer.WriteString("fit", imc.Fit);
                }

                if (!string.IsNullOrEmpty(imc.Variant))
                {
                    writer.WriteString("variant", imc.Variant);
                }

                break;

            case VideoComponent vc:
                if (vc.Url is not null)
                {
                    writer.WritePropertyName("url");
                    JsonSerializer.Serialize(writer, vc.Url, options);
                }
                if (vc.PosterUrl is not null)
                {
                    writer.WritePropertyName("posterUrl");
                    JsonSerializer.Serialize(writer, vc.PosterUrl, options);
                }
                break;

            case AudioPlayerComponent apc:
                if (apc.Url is not null)
                {
                    writer.WritePropertyName("url");
                    JsonSerializer.Serialize(writer, apc.Url, options);
                }
                if (apc.Description is not null)
                {
                    writer.WritePropertyName("description");
                    JsonSerializer.Serialize(writer, apc.Description, options);
                }
                break;

            case TabsComponent tabsc:
                if (tabsc.Tabs is not null)
                {
                    writer.WritePropertyName("tabs");
                    JsonSerializer.Serialize(writer, tabsc.Tabs, options);
                }
                break;

            case ModalComponent mc:
                if (!string.IsNullOrEmpty(mc.Trigger))
                {
                    writer.WriteString("trigger", mc.Trigger);
                }

                if (!string.IsNullOrEmpty(mc.Content))
                {
                    writer.WriteString("content", mc.Content);
                }

                break;

            case TextFieldComponent tfc:
                if (tfc.Value is not null)
                {
                    writer.WritePropertyName("value");
                    JsonSerializer.Serialize(writer, tfc.Value, options);
                }
                if (tfc.Placeholder is not null)
                {
                    writer.WritePropertyName("placeholder");
                    JsonSerializer.Serialize(writer, tfc.Placeholder, options);
                }
                if (!string.IsNullOrEmpty(tfc.ValidationRegexp))
                {
                    writer.WriteString("validationRegexp", tfc.ValidationRegexp);
                }

                if (tfc.Rows is not null)
                {
                    writer.WritePropertyName("rows");
                    JsonSerializer.Serialize(writer, tfc.Rows, options);
                }
                if (!string.IsNullOrEmpty(tfc.Variant))
                {
                    writer.WriteString("variant", tfc.Variant);
                }

                break;

            case DateTimeInputComponent dtc:
                if (dtc.Value is not null)
                {
                    writer.WritePropertyName("value");
                    JsonSerializer.Serialize(writer, dtc.Value, options);
                }
                if (dtc.EnableDate.HasValue)
                {
                    writer.WriteBoolean("enableDate", dtc.EnableDate.Value);
                }

                if (dtc.EnableTime.HasValue)
                {
                    writer.WriteBoolean("enableTime", dtc.EnableTime.Value);
                }

                if (dtc.Min is not null)
                {
                    writer.WritePropertyName("min");
                    JsonSerializer.Serialize(writer, dtc.Min, options);
                }
                if (dtc.Max is not null)
                {
                    writer.WritePropertyName("max");
                    JsonSerializer.Serialize(writer, dtc.Max, options);
                }
                break;

            case ChoicePickerComponent cpc:
                if (cpc.Value is not null)
                {
                    writer.WritePropertyName("value");
                    JsonSerializer.Serialize(writer, cpc.Value, options);
                }
                if (cpc.Options is not null)
                {
                    writer.WritePropertyName("options");
                    JsonSerializer.Serialize(writer, cpc.Options, options);
                }
                if (!string.IsNullOrEmpty(cpc.DisplayStyle))
                {
                    writer.WriteString("displayStyle", cpc.DisplayStyle);
                }

                if (cpc.Filterable.HasValue)
                {
                    writer.WriteBoolean("filterable", cpc.Filterable.Value);
                }

                if (!string.IsNullOrEmpty(cpc.Variant))
                {
                    writer.WriteString("variant", cpc.Variant);
                }

                break;

            case CheckBoxComponent cbc:
                if (cbc.Value is not null)
                {
                    writer.WritePropertyName("value");
                    JsonSerializer.Serialize(writer, cbc.Value, options);
                }
                break;

            case SliderComponent sc:
                if (sc.Value is not null)
                {
                    writer.WritePropertyName("value");
                    JsonSerializer.Serialize(writer, sc.Value, options);
                }
                if (sc.Min is not null)
                {
                    writer.WritePropertyName("min");
                    JsonSerializer.Serialize(writer, sc.Min, options);
                }
                if (sc.Max is not null)
                {
                    writer.WritePropertyName("max");
                    JsonSerializer.Serialize(writer, sc.Max, options);
                }
                if (sc.Steps is not null)
                {
                    writer.WritePropertyName("steps");
                    JsonSerializer.Serialize(writer, sc.Steps, options);
                }
                break;

            case TableComponent tc:
                if (tc.Columns is not null)
                {
                    writer.WritePropertyName("columns");
                    JsonSerializer.Serialize(writer, tc.Columns, options);
                }
                if (tc.Rows is not null)
                {
                    writer.WritePropertyName("rows");
                    JsonSerializer.Serialize(writer, tc.Rows, options);
                }
                break;

            case GenericComponent gc:
                foreach (var (k, v) in gc.Properties)
                {
                    if (
                        k
                        is "component"
                            or "id"
                            or "catalogId"
                            or "weight"
                            or "parent"
                            or "child"
                            or "children"
                            or "accessibility"
                            or "metadata"
                            or "checks"
                            or "text"
                            or "label"
                    )
                    {
                        continue;
                    }
                    writer.WritePropertyName(k);
                    v.WriteTo(writer);
                }
                break;
        }

        if (value.ExtensionData is not null)
        {
            foreach (var (k, v) in value.ExtensionData)
            {
                writer.WritePropertyName(k);
                v.WriteTo(writer);
            }
        }

        writer.WriteEndObject();
    }
}
