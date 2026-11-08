using System.Text.Json;
using System.Text.Json.Serialization;
using Blazejewicz.A2Ui.Core.Accessibility;
using Blazejewicz.A2Ui.Core.Actions;
using Blazejewicz.A2Ui.Core.Bindings;
using Blazejewicz.A2Ui.Core.Capabilities;
using Blazejewicz.A2Ui.Core.Children;
using Blazejewicz.A2Ui.Core.Choices;
using Blazejewicz.A2Ui.Core.Components;
using Blazejewicz.A2Ui.Core.Components.Extensions;
using Blazejewicz.A2Ui.Core.Functions;
using Blazejewicz.A2Ui.Core.Messages;
using Blazejewicz.A2Ui.Core.Tabs;
using Blazejewicz.A2Ui.Core.Validation;

namespace Blazejewicz.A2Ui.Core;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for A2UI protocol envelopes,
/// capabilities, components, bindings, and metadata models.
/// </summary>
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    AllowOutOfOrderMetadataProperties = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
[JsonSerializable(typeof(A2UiMessage))]
[JsonSerializable(typeof(AgentToRendererMessage))]
[JsonSerializable(typeof(RendererToAgentMessage))]
[JsonSerializable(typeof(ClientToServerMessage))]
[JsonSerializable(typeof(CreateSurface))]
[JsonSerializable(typeof(UpdateComponents))]
[JsonSerializable(typeof(UpdateDataModel))]
[JsonSerializable(typeof(DeleteSurface))]
[JsonSerializable(typeof(CallRendererFunction))]
[JsonSerializable(typeof(CallAgentFunction))]
[JsonSerializable(typeof(FunctionResponse))]
[JsonSerializable(typeof(FunctionError))]
[JsonSerializable(typeof(RendererAction))]
[JsonSerializable(typeof(RendererError))]
[JsonSerializable(typeof(ClientAction))]
[JsonSerializable(typeof(ClientError))]
[JsonSerializable(typeof(SurfaceMetadata))]
[JsonSerializable(typeof(ComponentMetadata))]
[JsonSerializable(typeof(ActionMetadata))]
[JsonSerializable(typeof(A2UiComponent))]
[JsonSerializable(typeof(A2UiComponent[]))]
[JsonSerializable(typeof(IReadOnlyList<A2UiComponent>))]
[JsonSerializable(typeof(TextComponent))]
[JsonSerializable(typeof(ButtonComponent))]
[JsonSerializable(typeof(ColumnComponent))]
[JsonSerializable(typeof(RowComponent))]
[JsonSerializable(typeof(CardComponent))]
[JsonSerializable(typeof(IconComponent))]
[JsonSerializable(typeof(DividerComponent))]
[JsonSerializable(typeof(ImageComponent))]
[JsonSerializable(typeof(VideoComponent))]
[JsonSerializable(typeof(AudioPlayerComponent))]
[JsonSerializable(typeof(ListComponent))]
[JsonSerializable(typeof(TabsComponent))]
[JsonSerializable(typeof(ModalComponent))]
[JsonSerializable(typeof(TextFieldComponent))]
[JsonSerializable(typeof(DateTimeInputComponent))]
[JsonSerializable(typeof(ChoicePickerComponent))]
[JsonSerializable(typeof(CheckBoxComponent))]
[JsonSerializable(typeof(SliderComponent))]
[JsonSerializable(typeof(TableComponent))]
[JsonSerializable(typeof(SurfaceComponent))]
[JsonSerializable(typeof(GenericComponent))]
[JsonSerializable(typeof(AccessibilityAttributes))]
[JsonSerializable(typeof(ActionEvent))]
[JsonSerializable(typeof(ComponentAction))]
[JsonSerializable(typeof(DynamicValue))]
[JsonSerializable(typeof(Dictionary<string, DynamicValue>))]
[JsonSerializable(typeof(FunctionCallValue))]
[JsonSerializable(typeof(ChildList))]
[JsonSerializable(typeof(ChildTemplate))]
[JsonSerializable(typeof(ChoiceOption))]
[JsonSerializable(typeof(ChoiceOption[]))]
[JsonSerializable(typeof(TabDefinition))]
[JsonSerializable(typeof(TabDefinition[]))]
[JsonSerializable(typeof(CheckRule))]
[JsonSerializable(typeof(CheckRule[]))]
[JsonSerializable(typeof(TableColumn))]
[JsonSerializable(typeof(TableColumn[]))]
[JsonSerializable(typeof(ValidationResult))]
[JsonSerializable(typeof(AgentCapabilities))]
[JsonSerializable(typeof(AgentCapabilitiesV10))]
[JsonSerializable(typeof(ClientCapabilities))]
[JsonSerializable(typeof(ClientCapabilitiesV09))]
[JsonSerializable(typeof(ClientDataModel))]
[JsonSerializable(typeof(RendererCapabilities))]
[JsonSerializable(typeof(RendererCapabilitiesV10))]
[JsonSerializable(typeof(RendererDataModel))]
[JsonSerializable(typeof(ServerCapabilities))]
[JsonSerializable(typeof(ServerCapabilitiesV09))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSerializable(typeof(Dictionary<string, string?>))]
[JsonSerializable(typeof(List<string?>))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(JsonElement[]))]
[JsonSerializable(typeof(IReadOnlyList<JsonElement>))]
[JsonSerializable(typeof(IReadOnlyList<string>))]
public sealed partial class A2UiJsonSerializerContext : JsonSerializerContext;
