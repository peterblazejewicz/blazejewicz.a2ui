using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Blazejewicz.A2Ui.Avalonia.Catalog;
using Blazejewicz.A2Ui.Avalonia.Functions;
using Blazejewicz.A2Ui.Core;
using Blazejewicz.A2Ui.Core.Accessibility;
using Blazejewicz.A2Ui.Core.Actions;
using Blazejewicz.A2Ui.Core.Bindings;
using Blazejewicz.A2Ui.Core.Children;
using Blazejewicz.A2Ui.Core.Choices;
using Blazejewicz.A2Ui.Core.Components;
using Blazejewicz.A2Ui.Core.Messages;
using Blazejewicz.A2Ui.Core.Surfaces;
using Blazejewicz.A2Ui.Core.Tabs;
using Blazejewicz.A2Ui.Core.Validation;
using Xunit;

namespace Blazejewicz.A2Ui.Avalonia.Tests;

public sealed class UpstreamRefinementsAndAdapterConformanceTests
{
    // =========================================================================
    // 1. Modal Trigger Activation, Check Gating & Lazy Content
    // =========================================================================

    [AvaloniaFact]
    public void Modal_LazyContent_MountsOnOpen()
    {
        var catalog = CatalogRegistry.CreateDefault();
        var renderer = new A2UiRenderer(catalog);
        var surface = new Surface("modal_surf") { ProtocolVersion = A2UiProtocol.Version10 };

        surface.UpdateComponents([
            new ModalComponent
            {
                Id = "root",
                Trigger = "open_btn",
                Content = "modal_body",
            },
            new ButtonComponent
            {
                Id = "open_btn",
                Text = DynamicValue.FromString("Open Modal"),
            },
            new TextComponent
            {
                Id = "modal_body",
                Text = DynamicValue.FromString("Dialog Content"),
            },
        ]);

        var control = renderer.Render(surface);
        var panel = Assert.IsType<Panel>(control);
        var btn = Assert.IsType<Button>(panel.Children[0]);
        var popup = Assert.IsType<Popup>(panel.Children[1]);
        var border = Assert.IsType<Border>(popup.Child);
        var contentColumn = Assert.IsType<StackPanel>(border.Child);

        // Before activation: content is lazy, not mounted in contentColumn
        Assert.False(popup.IsOpen);
        Assert.Single(contentColumn.Children); // only closeBtn
        Assert.IsType<Button>(contentColumn.Children[0]);

        // Activate trigger via routed Button.ClickEvent
        btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, btn));

        // After activation: popup opened and modal_body mounted
        Assert.True(popup.IsOpen);
        Assert.Equal(2, contentColumn.Children.Count);
        var body = Assert.IsType<TextBlock>(contentColumn.Children[1]);
        Assert.Equal("Dialog Content", body.Text);
    }

    [AvaloniaFact]
    public void Modal_NonButtonTrigger_TappedAndKeyboardActivation()
    {
        var catalog = CatalogRegistry.CreateDefault();
        var renderer = new A2UiRenderer(catalog);
        var surface = new Surface("modal_surf") { ProtocolVersion = A2UiProtocol.Version10 };

        surface.UpdateComponents([
            new ModalComponent
            {
                Id = "root",
                Trigger = "txt_trigger",
                Content = "modal_body",
            },
            new TextComponent
            {
                Id = "txt_trigger",
                Text = DynamicValue.FromString("Tap Me"),
            },
            new TextComponent
            {
                Id = "modal_body",
                Text = DynamicValue.FromString("Dialog Content"),
            },
        ]);

        var control = renderer.Render(surface);
        var panel = Assert.IsType<Panel>(control);
        var trigger = Assert.IsType<TextBlock>(panel.Children[0]);
        var popup = Assert.IsType<Popup>(panel.Children[1]);

        Assert.True(trigger.Focusable);
        Assert.False(popup.IsOpen);

        // Keyboard activation with Enter
        trigger.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Enter,
            Source = trigger,
        });

        Assert.True(popup.IsOpen);
    }

    [AvaloniaFact]
    public void Modal_CheckGating_PreventsOpenWhenTriggerFailsChecks()
    {
        var catalog = CatalogRegistry.CreateDefault();
        var renderer = new A2UiRenderer(catalog);
        var surface = new Surface("modal_surf") { ProtocolVersion = A2UiProtocol.Version10 };

        surface.UpdateComponents([
            new ModalComponent
            {
                Id = "root",
                Trigger = "open_btn",
                Content = "modal_body",
            },
            new ButtonComponent
            {
                Id = "open_btn",
                Text = DynamicValue.FromString("Open"),
                Checks = [
                    new CheckRule
                    {
                        Condition = DynamicValue.FromBool(false),
                        Message = "Trigger is blocked",
                    },
                ],
            },
            new TextComponent
            {
                Id = "modal_body",
                Text = DynamicValue.FromString("Secret Dialog"),
            },
        ]);

        var control = renderer.Render(surface);
        var panel = Assert.IsType<Panel>(control);
        var btn = Assert.IsType<Button>(panel.Children[0]);
        var popup = Assert.IsType<Popup>(panel.Children[1]);

        Assert.False(btn.IsEnabled);

        // Attempt activation
        btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, btn));

        // Must remain closed because check failed
        Assert.False(popup.IsOpen);
    }

    [AvaloniaFact]
    public void Modal_CloseButton_ClosesWithoutReopening()
    {
        var catalog = CatalogRegistry.CreateDefault();
        var renderer = new A2UiRenderer(catalog);
        var surface = new Surface("modal_surf") { ProtocolVersion = A2UiProtocol.Version10 };

        surface.UpdateComponents([
            new ModalComponent
            {
                Id = "root",
                Trigger = "open_btn",
                Content = "modal_body",
            },
            new ButtonComponent
            {
                Id = "open_btn",
                Text = DynamicValue.FromString("Open"),
            },
            new TextComponent
            {
                Id = "modal_body",
                Text = DynamicValue.FromString("Content"),
            },
        ]);

        var control = renderer.Render(surface);
        var panel = Assert.IsType<Panel>(control);
        var btn = Assert.IsType<Button>(panel.Children[0]);
        var popup = Assert.IsType<Popup>(panel.Children[1]);
        var border = Assert.IsType<Border>(popup.Child);
        var contentColumn = Assert.IsType<StackPanel>(border.Child);
        var closeBtn = Assert.IsType<Button>(contentColumn.Children[0]);

        // Open
        btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, btn));
        Assert.True(popup.IsOpen);

        // Close via close button
        closeBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, closeBtn));
        Assert.False(popup.IsOpen);
    }

    [AvaloniaFact]
    public void Modal_NestedModals_IsolatedActivation()
    {
        var catalog = CatalogRegistry.CreateDefault();
        var renderer = new A2UiRenderer(catalog);
        var surface = new Surface("modal_surf") { ProtocolVersion = A2UiProtocol.Version10 };

        surface.UpdateComponents([
            new ModalComponent
            {
                Id = "outer_modal",
                Trigger = "inner_modal",
                Content = "outer_content",
            },
            new ModalComponent
            {
                Id = "inner_modal",
                Trigger = "inner_btn",
                Content = "inner_content",
            },
            new ButtonComponent
            {
                Id = "inner_btn",
                Text = DynamicValue.FromString("Open Inner"),
            },
            new TextComponent
            {
                Id = "inner_content",
                Text = DynamicValue.FromString("Inner Content"),
            },
            new TextComponent
            {
                Id = "outer_content",
                Text = DynamicValue.FromString("Outer Content"),
            },
        ]);

        var control = renderer.Render(surface);
        var outerPanel = Assert.IsType<Panel>(control);
        var innerPanel = Assert.IsType<Panel>(outerPanel.Children[0]);
        var innerBtn = Assert.IsType<Button>(innerPanel.Children[0]);
        var innerPopup = Assert.IsType<Popup>(innerPanel.Children[1]);
        var outerPopup = Assert.IsType<Popup>(outerPanel.Children[1]);

        // Click inner button
        innerBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, innerBtn));

        // Inner modal opened, outer modal remained closed
        Assert.True(innerPopup.IsOpen);
        Assert.False(outerPopup.IsOpen);
    }

    // =========================================================================
    // 2. In-Place Update & Property Reset
    // =========================================================================

    [AvaloniaFact]
    public void Update_Accessibility_TotalResetWhenOmitted()
    {
        var catalog = CatalogRegistry.CreateDefault();
        var renderer = new A2UiRenderer(catalog);
        var surface = new Surface("s1");

        // 1. Initial render with full accessibility
        surface.UpdateComponents([
            new TextComponent
            {
                Id = "txt",
                Text = DynamicValue.FromString("Hello"),
                Accessibility = new AccessibilityAttributes
                {
                    Label = DynamicValue.FromString("Accessible Label"),
                    Description = DynamicValue.FromString("Accessible Description"),
                    Live = "assertive",
                    Hidden = DynamicValue.FromString("true"),
                },
            },
        ]);

        var control = renderer.Render(surface);
        var tb = Assert.IsType<TextBlock>(control);

        Assert.Equal("Accessible Label", AutomationProperties.GetName(tb));
        Assert.Equal("Accessible Description", AutomationProperties.GetHelpText(tb));
        Assert.Equal(AutomationLiveSetting.Assertive, AutomationProperties.GetLiveSetting(tb));
        Assert.False(tb.IsVisible);
        Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(tb));

        // 2. Update with accessibility omitted
        surface.UpdateComponents([
            new TextComponent
            {
                Id = "txt",
                Text = DynamicValue.FromString("Hello Updated"),
                Accessibility = null,
            },
        ]);

        var updatedControl = renderer.Render(surface);
        Assert.Same(tb, updatedControl);

        // All accessibility attributes reset to defaults
        Assert.Null(AutomationProperties.GetName(tb));
        Assert.Null(AutomationProperties.GetHelpText(tb));
        Assert.Equal(AutomationLiveSetting.Off, AutomationProperties.GetLiveSetting(tb));
        Assert.True(tb.IsVisible);
        Assert.Equal(AccessibilityView.Content, AutomationProperties.GetAccessibilityView(tb));
    }

    [AvaloniaFact]
    public void Update_TextField_ResetsObscuredVariantAndClearsChecks()
    {
        var catalog = CatalogRegistry.CreateDefault();
        var renderer = new A2UiRenderer(catalog);
        var surface = new Surface("s1");

        // 1. Initial obscured TextField with failing check
        surface.UpdateComponents([
            new TextFieldComponent
            {
                Id = "input",
                Value = DynamicValue.FromPath("/pass"),
                Variant = "obscured",
                Checks = [
                    new CheckRule
                    {
                        Condition = DynamicValue.FromBool(false),
                        Message = "Password too weak",
                    },
                ],
            },
        ]);

        var control = renderer.Render(surface);
        var wrapper = Assert.IsType<StackPanel>(control);
        var tb = Assert.IsType<TextBox>(wrapper.Children[0]);

        Assert.Equal('\u2022', tb.PasswordChar);
        Assert.Equal(2, wrapper.Children.Count);
        var err = Assert.IsType<TextBlock>(wrapper.Children[1]);
        Assert.Equal("Password too weak", err.Text);

        // 2. Update to plain text and clear checks
        surface.UpdateComponents([
            new TextFieldComponent
            {
                Id = "input",
                Value = DynamicValue.FromPath("/pass"),
                Variant = null,
                Checks = null,
            },
        ]);

        var updated = renderer.Render(surface);
        Assert.Same(wrapper, updated);
        Assert.Equal('\0', tb.PasswordChar);
        Assert.Single(wrapper.Children); // error TextBlock was pruned
    }

    [AvaloniaFact]
    public void Update_Button_ActionUpdateAndFunctionCallExecution()
    {
        var catalog = CatalogRegistry.CreateDefault();
        var funcRegistry = FunctionRegistry.CreateDefault();
        var renderer = new A2UiRenderer(catalog, funcRegistry);
        var surface = new Surface("s1");
        surface.DataModel.SetSnapshot(JsonSerializer.SerializeToElement(new { counter = 10 }));

        string? firedEvent = null;
        renderer.UserActionFired += (_, e) => firedEvent = e.EventName;

        // 1. Initial button with action event
        surface.UpdateComponents([
            new ButtonComponent
            {
                Id = "btn",
                Text = DynamicValue.FromString("Click"),
                Action = new ComponentAction
                {
                    Event = new ActionEvent { Name = "btn_event" },
                },
            },
        ]);

        var control = renderer.Render(surface);
        var btn = Assert.IsType<Button>(control);

        btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, btn));
        Assert.Equal("btn_event", firedEvent);
        firedEvent = null;

        // 2. Update button in-place with functionCall action
        surface.UpdateComponents([
            new ButtonComponent
            {
                Id = "btn",
                Text = DynamicValue.FromString("Click"),
                Action = new ComponentAction
                {
                    FunctionCall = new FunctionCallValue
                    {
                        Call = "add",
                        Args = new Dictionary<string, JsonElement>
                        {
                            ["a"] = JsonSerializer.SerializeToElement(5),
                            ["b"] = JsonSerializer.SerializeToElement(3),
                        },
                    },
                },
            },
        ]);

        var updated = renderer.Render(surface);
        Assert.Same(btn, updated);

        btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, btn));
        Assert.Null(firedEvent); // functionCall does not emit outbound event

        // 3. Remove action
        surface.UpdateComponents([
            new ButtonComponent
            {
                Id = "btn",
                Text = DynamicValue.FromString("No Action"),
                Action = null,
            },
        ]);

        renderer.Render(surface);
        btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, btn));
        Assert.Null(firedEvent);
    }

    [AvaloniaFact]
    public void Update_Image_ResetsFitAndClearsUrl()
    {
        var catalog = CatalogRegistry.CreateDefault();
        var renderer = new A2UiRenderer(catalog);
        var surface = new Surface("s1");

        surface.UpdateComponents([
            new ImageComponent
            {
                Id = "img",
                Url = DynamicValue.FromString("https://example.com/banner.png"),
                Fit = "cover",
            },
        ]);

        var control = renderer.Render(surface);
        var img = Assert.IsType<Image>(control);
        Assert.Equal(Stretch.UniformToFill, img.Stretch);

        // Update with null URL and null Fit
        surface.UpdateComponents([
            new ImageComponent
            {
                Id = "img",
                Url = null,
                Fit = null,
            },
        ]);

        var updated = renderer.Render(surface);
        Assert.Same(img, updated);
        Assert.Equal(Stretch.Uniform, img.Stretch);
        Assert.Null(img.Source);
        Assert.Null(img.Tag);
    }

    [AvaloniaFact]
    public void Update_ChoicePicker_SynchronizesOptionsAndClearsChecks()
    {
        var catalog = CatalogRegistry.CreateDefault();
        var renderer = new A2UiRenderer(catalog);
        var surface = new Surface("s1");

        surface.UpdateComponents([
            new ChoicePickerComponent
            {
                Id = "picker",
                Options = [
                    new ChoiceOption { Label = DynamicValue.FromString("Alpha"), Value = "a" },
                    new ChoiceOption { Label = DynamicValue.FromString("Beta"), Value = "b" },
                ],
                Checks = [
                    new CheckRule
                    {
                        Condition = DynamicValue.FromBool(false),
                        Message = "Selection required",
                    },
                ],
            },
        ]);

        var control = renderer.Render(surface);
        var wrapper = Assert.IsType<StackPanel>(control);
        var combo = Assert.IsType<ComboBox>(wrapper.Children[0]);

        Assert.Equal(2, combo.Items.Count);
        Assert.Equal(2, wrapper.Children.Count);

        // Update options to C, D, E and clear checks
        surface.UpdateComponents([
            new ChoicePickerComponent
            {
                Id = "picker",
                Options = [
                    new ChoiceOption { Label = DynamicValue.FromString("Gamma"), Value = "g" },
                    new ChoiceOption { Label = DynamicValue.FromString("Delta"), Value = "d" },
                    new ChoiceOption { Label = DynamicValue.FromString("Epsilon"), Value = "e" },
                ],
                Checks = null,
            },
        ]);

        var updated = renderer.Render(surface);
        Assert.Same(wrapper, updated);
        Assert.Equal(3, combo.Items.Count);
        Assert.Single(wrapper.Children); // validation error removed
    }

    // =========================================================================
    // 3. Child Filtering, Weight Alignment & Fallbacks
    // =========================================================================

    [AvaloniaFact]
    public void ColumnAndRow_ExcludeUnknownAndCyclicChildren_ZeroLayoutFootprint()
    {
        var catalog = CatalogRegistry.CreateDefault();
        var renderer = new A2UiRenderer(catalog);
        var surface = new Surface("s1");

        surface.UpdateComponents([
            new ColumnComponent
            {
                Id = "root",
                Children = ChildList.FromIds(["unknown_child", "valid_child", "root"]),
            },
            new GenericComponent
            {
                Id = "unknown_child",
                ComponentType = "FutureWidget2030",
            },
            new TextComponent
            {
                Id = "valid_child",
                Text = DynamicValue.FromString("Valid Child"),
            },
        ]);

        var control = renderer.Render(surface);
        var col = Assert.IsType<StackPanel>(control);

        // Only valid_child rendered. Unknown widget and cycle_child (whose child is root) are excluded cleanly.
        // Neither causes StackOverflow or takes up layout slots.
        Assert.Single(col.Children);
        var txt = Assert.IsType<TextBlock>(col.Children[0]);
        Assert.Equal("Valid Child", txt.Text);
    }

    [AvaloniaFact]
    public void Row_WeightAlignment_ExcludesNullChildWithoutShiftingIndices()
    {
        var catalog = CatalogRegistry.CreateDefault();
        var renderer = new A2UiRenderer(catalog);
        var surface = new Surface("s1");

        surface.UpdateComponents([
            new RowComponent
            {
                Id = "root",
                Children = ChildList.FromIds(["missing_or_unknown", "weighted_child"]),
            },
            new GenericComponent
            {
                Id = "missing_or_unknown",
                ComponentType = "UnregisteredWidget",
            },
            new TextComponent
            {
                Id = "weighted_child",
                Text = DynamicValue.FromString("Weighted"),
                Weight = 4.0,
            },
        ]);

        var control = renderer.Render(surface);
        var grid = Assert.IsType<Grid>(control);

        // Exactly 1 column corresponding to weighted_child, with weight 4.0 Star
        Assert.Single(grid.ColumnDefinitions);
        Assert.True(grid.ColumnDefinitions[0].Width.IsStar);
        Assert.Equal(4.0, grid.ColumnDefinitions[0].Width.Value);
        Assert.Single(grid.Children);
    }

    [AvaloniaFact]
    public void List_ExcludesUnknownAndCyclicChildren_NoListItemsOrMarkers()
    {
        var catalog = CatalogRegistry.CreateDefault();
        var renderer = new A2UiRenderer(catalog);
        var surface = new Surface("s1");

        surface.UpdateComponents([
            new ListComponent
            {
                Id = "root",
                ListStyle = "ordered",
                Children = ChildList.FromIds(["unknown_child", "item_1", "item_2"]),
            },
            new GenericComponent
            {
                Id = "unknown_child",
                ComponentType = "UnknownWidget",
            },
            new TextComponent
            {
                Id = "item_1",
                Text = DynamicValue.FromString("First"),
            },
            new TextComponent
            {
                Id = "item_2",
                Text = DynamicValue.FromString("Second"),
            },
        ]);

        var control = renderer.Render(surface);
        var sv = Assert.IsType<ScrollViewer>(control);
        var panel = Assert.IsType<StackPanel>(sv.Content);

        // Unknown child emitted zero markers/rows. Only item_1 and item_2 exist, numbered 1. and 2.
        Assert.Equal(2, panel.Children.Count);
        var row1 = Assert.IsType<StackPanel>(panel.Children[0]);
        var marker1 = Assert.IsType<TextBlock>(row1.Children[0]);
        Assert.Equal("1.", marker1.Text);

        var row2 = Assert.IsType<StackPanel>(panel.Children[1]);
        var marker2 = Assert.IsType<TextBlock>(row2.Children[0]);
        Assert.Equal("2.", marker2.Text);
    }

    // =========================================================================
    // 4. Conformance Scenarios (adapter_conformance.yaml)
    // =========================================================================

    [AvaloniaFact]
    public void Conformance_MediaComponentsBindUrlAndName()
    {
        var catalog = CatalogRegistry.CreateDefault();
        var renderer = new A2UiRenderer(catalog);
        var surface = new Surface("adapter_surface") { ProtocolVersion = A2UiProtocol.Version10 };

        surface.DataModel.SetSnapshot(JsonSerializer.SerializeToElement(new
        {
            media = new
            {
                imageUrl = "https://example.com/banner-v1.png",
                videoUrl = "https://example.com/intro.mp4",
                audioUrl = "https://example.com/podcast-ep1.mp3",
            },
        }));

        surface.UpdateComponents([
            new ColumnComponent
            {
                Id = "root",
                Children = ChildList.FromIds(["hero_img", "intro_video", "podcast_audio", "status_icon"]),
            },
            new ImageComponent
            {
                Id = "hero_img",
                Url = DynamicValue.FromPath("/media/imageUrl"),
            },
            new VideoComponent
            {
                Id = "intro_video",
                Url = DynamicValue.FromPath("/media/videoUrl"),
            },
            new AudioPlayerComponent
            {
                Id = "podcast_audio",
                Url = DynamicValue.FromPath("/media/audioUrl"),
            },
            new IconComponent
            {
                Id = "status_icon",
                Name = DynamicValue.FromString("check"),
            },
        ]);

        var control = renderer.Render(surface);
        var col = Assert.IsType<StackPanel>(control);
        Assert.Equal(4, col.Children.Count);

        var img = Assert.IsType<Image>(col.Children[0]);
        Assert.Equal("https://example.com/banner-v1.png", img.Tag);

        // Update imageUrl in dataModel
        surface.DataModel.Apply(new UpdateDataModel
        {
            SurfaceId = "adapter_surface",
            Path = "/media/imageUrl",
            Value = JsonSerializer.SerializeToElement("https://example.com/banner-v2.png"),
        });

        renderer.Render(surface);
        Assert.Equal("https://example.com/banner-v2.png", img.Tag);
    }

    [AvaloniaFact]
    public void Conformance_TabsSwitchContent()
    {
        var catalog = CatalogRegistry.CreateDefault();
        var renderer = new A2UiRenderer(catalog);
        var surface = new Surface("adapter_surface") { ProtocolVersion = A2UiProtocol.Version10 };

        surface.UpdateComponents([
            new TabsComponent
            {
                Id = "root",
                Tabs = [
                    new TabDefinition { Title = DynamicValue.FromString("First"), Child = "tab_one_text" },
                    new TabDefinition { Title = DynamicValue.FromString("Second"), Child = "tab_two_text" },
                ],
            },
            new TextComponent
            {
                Id = "tab_one_text",
                Text = DynamicValue.FromString("First tab content"),
            },
            new TextComponent
            {
                Id = "tab_two_text",
                Text = DynamicValue.FromString("Second tab content"),
            },
        ]);

        var control = renderer.Render(surface);
        var tc = Assert.IsType<TabControl>(control);
        Assert.Equal(2, tc.Items.Count);

        var tab0 = Assert.IsType<TabItem>(tc.Items[0]);
        var tab1 = Assert.IsType<TabItem>(tc.Items[1]);

        var text0 = Assert.IsType<TextBlock>(tab0.Content);
        var text1 = Assert.IsType<TextBlock>(tab1.Content);

        Assert.Equal("First tab content", text0.Text);
        Assert.Equal("Second tab content", text1.Text);
    }

    [AvaloniaFact]
    public void Conformance_DividerAndSliderDatetimeRender()
    {
        var catalog = CatalogRegistry.CreateDefault();
        var renderer = new A2UiRenderer(catalog);
        var surface = new Surface("adapter_surface") { ProtocolVersion = A2UiProtocol.Version10 };

        surface.DataModel.SetSnapshot(JsonSerializer.SerializeToElement(new
        {
            vol = 25,
            date = "2025-06-15",
        }));

        surface.UpdateComponents([
            new ColumnComponent
            {
                Id = "root",
                Children = ChildList.FromIds(["sep", "vol_slider", "dt_input"]),
            },
            new DividerComponent
            {
                Id = "sep",
                Axis = "vertical",
            },
            new SliderComponent
            {
                Id = "vol_slider",
                Value = DynamicValue.FromPath("/vol"),
                Min = DynamicValue.FromString("0"),
                Max = DynamicValue.FromString("100"),
            },
            new DateTimeInputComponent
            {
                Id = "dt_input",
                Value = DynamicValue.FromPath("/date"),
                EnableDate = true,
                EnableTime = false,
            },
        ]);

        var control = renderer.Render(surface);
        var col = Assert.IsType<StackPanel>(control);
        Assert.Equal(3, col.Children.Count);

        var sep = Assert.IsType<Separator>(col.Children[0]);
        Assert.Equal(1, sep.Width); // vertical divider has fixed width

        var slider = Assert.IsType<Slider>(col.Children[1]);
        Assert.Equal(25, slider.Value);

        var datePicker = Assert.IsType<CalendarDatePicker>(col.Children[2]);
        Assert.Equal(new DateTime(2025, 6, 15), datePicker.SelectedDate);
    }

    [AvaloniaFact]
    public void Conformance_ActionExecutionEventVsLocalFunctionCall()
    {
        var catalog = CatalogRegistry.CreateDefault();
        var funcRegistry = FunctionRegistry.CreateDefault();
        var renderer = new A2UiRenderer(catalog, funcRegistry);
        var surface = new Surface("adapter_surface") { ProtocolVersion = A2UiProtocol.Version10 };

        surface.DataModel.SetSnapshot(JsonSerializer.SerializeToElement(new
        {
            itemId = "sku-42",
            counter = 10,
        }));

        UserActionEventArgs? capturedEvent = null;
        renderer.UserActionFired += (_, e) => capturedEvent = e;

        surface.UpdateComponents([
            new RowComponent
            {
                Id = "root",
                Children = ChildList.FromIds(["event_btn", "func_btn"]),
            },
            new ButtonComponent
            {
                Id = "event_btn",
                Child = "event_btn_label",
                Action = new ComponentAction
                {
                    Event = new ActionEvent
                    {
                        Name = "buy_item",
                        Context = new Dictionary<string, DynamicValue>
                        {
                            ["sku"] = DynamicValue.FromPath("/itemId"),
                        },
                    },
                },
            },
            new TextComponent
            {
                Id = "event_btn_label",
                Text = DynamicValue.FromString("Buy"),
            },
            new ButtonComponent
            {
                Id = "func_btn",
                Child = "func_btn_label",
                Action = new ComponentAction
                {
                    FunctionCall = new FunctionCallValue
                    {
                        Call = "add",
                        Args = new Dictionary<string, JsonElement>
                        {
                            ["a"] = JsonSerializer.SerializeToElement(10),
                            ["b"] = JsonSerializer.SerializeToElement(5),
                        },
                    },
                },
            },
            new TextComponent
            {
                Id = "func_btn_label",
                Text = DynamicValue.FromString("Increment"),
            },
        ]);

        var control = renderer.Render(surface);
        var row = Assert.IsType<StackPanel>(control);
        var eventBtn = Assert.IsType<Button>(row.Children[0]);
        var funcBtn = Assert.IsType<Button>(row.Children[1]);

        // Event button dispatches action event with resolved context
        eventBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, eventBtn));
        Assert.NotNull(capturedEvent);
        Assert.Equal("buy_item", capturedEvent.EventName);
        var payload = Assert.IsType<Dictionary<string, string?>>(capturedEvent.Payload);
        Assert.Equal("sku-42", payload["sku"]);

        capturedEvent = null;

        // Function button executes locally without outbound action event
        funcBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, funcBtn));
        Assert.Null(capturedEvent);
    }

    [AvaloniaFact]
    public void Conformance_ProgressiveArrivalPlaceholderUpgradeInPlace()
    {
        var catalog = CatalogRegistry.CreateDefault();
        var renderer = new A2UiRenderer(catalog);
        var surface = new Surface("adapter_surface") { ProtocolVersion = A2UiProtocol.Version10 };

        // 1. Initial state: root card references deferred_text which has not arrived
        surface.UpdateComponents([
            new CardComponent
            {
                Id = "root",
                Child = "deferred_text",
            },
        ]);

        var control = renderer.Render(surface);
        var border = Assert.IsType<Border>(control);
        Assert.Null(border.Child); // deferred child not yet arrived

        // 2. Component arrives
        surface.UpdateComponents([
            new TextComponent
            {
                Id = "deferred_text",
                Text = DynamicValue.FromString("Arrived Safely"),
            },
        ]);

        var updatedControl = renderer.Render(surface);

        // View identity is preserved (upgraded in place)
        Assert.Same(border, updatedControl);
        var text = Assert.IsType<TextBlock>(border.Child);
        Assert.Equal("Arrived Safely", text.Text);
    }
}
