using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Blazejewicz.A2Ui.Avalonia.Catalog;
using Blazejewicz.A2Ui.Avalonia.Catalog.Entries;
using Blazejewicz.A2Ui.Core.Accessibility;
using Blazejewicz.A2Ui.Core.Actions;
using Blazejewicz.A2Ui.Core.Bindings;
using Blazejewicz.A2Ui.Core.Children;
using Blazejewicz.A2Ui.Core.Components;
using Blazejewicz.A2Ui.Core.Surfaces;
using Blazejewicz.A2Ui.Core.Validation;
using Xunit;

namespace Blazejewicz.A2Ui.Avalonia.Tests.Catalog;

public sealed class V10ComponentUpdatesTests
{
    [AvaloniaFact]
    public void TextFieldCatalogEntry_Placeholder_SetsWatermarkOverLabel()
    {
        var dm = new DataModel();
        var ctx = new MockRenderContext(dm);
        var entry = new TextFieldCatalogEntry();

        var compWithPlaceholder = new TextFieldComponent
        {
            Id = "tf1",
            Label = DynamicValue.FromString("Default Label"),
            Placeholder = DynamicValue.FromString("Custom Watermark"),
        };

        var control = entry.Create(compWithPlaceholder, dm, ctx);
        var tb = Assert.IsType<TextBox>(control);
        Assert.Equal("Custom Watermark", tb.PlaceholderText);

        var compFallback = new TextFieldComponent { Id = "tf2", Label = DynamicValue.FromString("Only Label") };

        var controlFallback = entry.Create(compFallback, dm, ctx);
        var tbFallback = Assert.IsType<TextBox>(controlFallback);
        Assert.Equal("Only Label", tbFallback.PlaceholderText);
    }

    [AvaloniaFact]
    public void TextFieldCatalogEntry_TwoWayBinding_CreatesMissingDataModelPath()
    {
        var dm = new DataModel();
        var ctx = new MockRenderContext(dm);
        var entry = new TextFieldCatalogEntry();

        var comp = new TextFieldComponent { Id = "tf", Value = DynamicValue.FromPath("/user/profile/name") };

        var control = entry.Create(comp, dm, ctx);
        var tb = Assert.IsType<TextBox>(control);

        tb.Text = "Alice";

        // Assert path was created and updated in data model
        Assert.Equal("Alice", dm.Resolve(DynamicValue.FromPath("/user/profile/name")));
    }

    [AvaloniaFact]
    public void SliderCatalogEntry_Steps_ConfiguresTickSnapping()
    {
        var dm = new DataModel();
        var ctx = new MockRenderContext(dm);
        var entry = new SliderCatalogEntry();

        var compWithSteps = new SliderComponent
        {
            Id = "sl1",
            Min = DynamicValue.FromNumber(0),
            Max = DynamicValue.FromNumber(100),
            Steps = DynamicValue.FromNumber(5),
        };

        var control = entry.Create(compWithSteps, dm, ctx);
        var slider = Assert.IsType<Slider>(control);

        Assert.True(slider.IsSnapToTickEnabled);
        Assert.Equal(20.0, slider.TickFrequency);

        var compNoSteps = new SliderComponent
        {
            Id = "sl2",
            Min = DynamicValue.FromNumber(0),
            Max = DynamicValue.FromNumber(100),
        };

        var controlNoSteps = entry.Create(compNoSteps, dm, ctx);
        var sliderNoSteps = Assert.IsType<Slider>(controlNoSteps);

        Assert.False(sliderNoSteps.IsSnapToTickEnabled);
    }

    [AvaloniaFact]
    public void VideoCatalogEntry_PosterUrl_ValidatesAndFormats()
    {
        var dm = new DataModel();
        var ctx = new MockRenderContext(dm);
        var entry = new VideoCatalogEntry();

        var safeComp = new VideoComponent
        {
            Id = "v1",
            Url = DynamicValue.FromString("https://example.com/video.mp4"),
            PosterUrl = DynamicValue.FromString("https://example.com/poster.jpg"),
        };

        var control = entry.Create(safeComp, dm, ctx);
        var tb = Assert.IsType<TextBlock>(control);
        Assert.Contains("Poster: https://example.com/poster.jpg", tb.Text);

        var unsafeComp = new VideoComponent
        {
            Id = "v2",
            Url = DynamicValue.FromString("https://example.com/video.mp4"),
            PosterUrl = DynamicValue.FromString("javascript:alert(1)"),
        };

        var unsafeControl = entry.Create(unsafeComp, dm, ctx);
        var tbUnsafe = Assert.IsType<TextBlock>(unsafeControl);
        Assert.Contains("Poster: blocked unsafe url", tbUnsafe.Text);
    }

    [AvaloniaFact]
    public void IconCatalogEntry_SvgPath_CreatesAndUpdatesPathIcon()
    {
        var dm = new DataModel();
        var ctx = new MockRenderContext(dm);
        var entry = new IconCatalogEntry();

        var svgObj = DynamicValue.FromObject(
            new Dictionary<string, object?> { ["svgPath"] = "M10 20v-6h4v6h5v-8h3L12 3 2 12h3v8z" }
        );

        var comp = new IconComponent { Id = "ico1", Name = svgObj };

        var control = entry.Create(comp, dm, ctx);
        var pathIcon = Assert.IsType<PathIcon>(control);
        Assert.NotNull(pathIcon.Data);

        // Update with new svgPath
        var updatedSvgObj = DynamicValue.FromObject(new Dictionary<string, object?> { ["svgPath"] = "M0 0h24v24H0z" });
        var updatedComp = new IconComponent { Id = "ico1", Name = updatedSvgObj };

        bool updated = entry.Update(pathIcon, updatedComp, dm, ctx);
        Assert.True(updated);
        Assert.NotNull(pathIcon.Data);

        // Name as text icon creates TextBlock
        var textComp = new IconComponent { Id = "ico2", Name = DynamicValue.FromString("home") };
        var textControl = entry.Create(textComp, dm, ctx);
        Assert.IsType<TextBlock>(textControl);
    }

    [AvaloniaFact]
    public void ButtonCatalogEntry_UserMessage_ForwardsInUserActionEventArgs()
    {
        var dm = new DataModel();
        var surface = new Surface("btn-surface", "https://a2ui.org/specification/v0_9/basic_catalog.json");
        var catalog = CatalogRegistry.CreateDefault();
        var renderer = new A2UiRenderer(catalog);

        surface.UpdateComponents([
            new ButtonComponent
            {
                Id = "btn1",
                Text = DynamicValue.FromString("Submit"),
                Action = new ComponentAction
                {
                    Event = new ActionEvent
                    {
                        Name = "submitForm",
                        UserMessage = DynamicValue.FromString("User clicked submit button"),
                    },
                },
            },
        ]);

        UserActionEventArgs? fired = null;
        renderer.UserActionFired += (_, e) => fired = e;

        var control = renderer.Render(surface);
        var btn = Assert.IsType<Button>(control);

        btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.NotNull(fired);
        Assert.Equal("submitForm", fired.EventName);
        Assert.Equal("User clicked submit button", fired.UserMessage);
    }

    [AvaloniaFact]
    public void Accessibility_LiveAndHidden_AppliedCorrectly()
    {
        var surface = new Surface("acc-surface", "https://a2ui.org/specification/v0_9/basic_catalog.json");
        var catalog = CatalogRegistry.CreateDefault();
        var renderer = new A2UiRenderer(catalog);

        surface.UpdateComponents([
            new TextComponent
            {
                Id = "t1",
                Text = DynamicValue.FromString("Status update"),
                Accessibility = new AccessibilityAttributes
                {
                    Label = DynamicValue.FromString("Status"),
                    Description = DynamicValue.FromString("Current system status"),
                    Live = "polite",
                    Hidden = DynamicValue.FromBool(false),
                },
            },
        ]);

        var control = renderer.Render(surface);
        Assert.Equal("Status", AutomationProperties.GetName(control));
        Assert.Equal("Current system status", AutomationProperties.GetHelpText(control));
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(control));
        Assert.True(control.IsVisible);

        // Hidden component
        surface.UpdateComponents([
            new TextComponent
            {
                Id = "t2",
                Text = DynamicValue.FromString("Decorative"),
                Accessibility = new AccessibilityAttributes { Hidden = DynamicValue.FromBool(true) },
            },
        ]);

        var hiddenControl = renderer.Render(surface);
        // t2 in StackPanel when multiple roots
        var panel = Assert.IsType<StackPanel>(hiddenControl);
        var t2Control = panel.Children[1];
        Assert.True(t2Control.IsVisible);
        Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(t2Control));
    }

    [AvaloniaFact]
    public void Accessibility_HiddenSubtreeAndVersionScoping_ConformsToSpec()
    {
        var catalog = CatalogRegistry.CreateDefault();
        var renderer = new A2UiRenderer(catalog);
        var surface = new Surface("a11y-v10", "https://a2ui.org/specification/v1_0/catalogs/basic/catalog.json")
        {
            ProtocolVersion = "1.0",
        };
        surface.DataModel.Apply("/parentHidden", System.Text.Json.JsonDocument.Parse("true").RootElement);

        surface.UpdateComponents([
            new ColumnComponent
            {
                Id = "root",
                Children = ChildList.FromIds("visible_text", "explicitly_hidden_child"),
                Accessibility = new AccessibilityAttributes
                {
                    Live = "POLITE", // Invalid uppercase must be ignored per spec
                    Hidden = DynamicValue.FromPath("/parentHidden"),
                },
            },
            new TextComponent
            {
                Id = "visible_text",
                Text = DynamicValue.FromString("Visible Content"),
            },
            new TextComponent
            {
                Id = "explicitly_hidden_child",
                Text = DynamicValue.FromString("Child With Own Hidden"),
                Accessibility = new AccessibilityAttributes
                {
                    Hidden = DynamicValue.FromBool(true),
                },
            },
        ]);

        var rootControl = Assert.IsType<StackPanel>(renderer.Render(surface));
        var child1 = rootControl.Children[0];
        var child2 = rootControl.Children[1];

        // 1. Visual visibility is unaffected; subtree is hidden from assistive tech; invalid live "POLITE" is Off
        Assert.True(rootControl.IsVisible);
        Assert.True(child1.IsVisible);
        Assert.True(child2.IsVisible);
        Assert.Equal(AutomationLiveSetting.Off, AutomationProperties.GetLiveSetting(rootControl));
        Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(rootControl));
        Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(child1));
        Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(child2));

        // 2. Toggle parent hidden -> false: child1 restores Content while child2 stays Raw
        surface.DataModel.Apply("/parentHidden", System.Text.Json.JsonDocument.Parse("false").RootElement);
        renderer.Render(surface);

        Assert.Equal(AccessibilityView.Content, AutomationProperties.GetAccessibilityView(rootControl));
        Assert.Equal(AccessibilityView.Content, AutomationProperties.GetAccessibilityView(child1));
        Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(child2));

        // 3. On v0.9/v0.9.1 surface, live and hidden are ignored
        var v09Surface = new Surface("a11y-v09")
        {
            ProtocolVersion = "v0.9.1",
        };
        v09Surface.UpdateComponents([
            new TextComponent
            {
                Id = "root",
                Text = DynamicValue.FromString("v0.9 text"),
                Accessibility = new AccessibilityAttributes
                {
                    Label = DynamicValue.FromString("v0.9 label"),
                    Live = "polite",
                    Hidden = DynamicValue.FromBool(true),
                },
            },
        ]);

        var v09Control = renderer.Render(v09Surface)!;
        Assert.Equal("v0.9 label", AutomationProperties.GetName(v09Control));
        Assert.Equal(AutomationLiveSetting.Off, AutomationProperties.GetLiveSetting(v09Control));
        Assert.Equal(AccessibilityView.Content, AutomationProperties.GetAccessibilityView(v09Control));
    }

    [AvaloniaFact]
    public void CheckHelper_DynamicValidationResult_SeveritiesHandled()
    {
        var dm = new DataModel();
        var ctx = new MockRenderContext(dm);

        // Error severity fails check and blocks validity
        var errorComp = new TextFieldComponent
        {
            Id = "f1",
            Checks =
            [
                new CheckRule
                {
                    Condition = DynamicValue.FromString(
                        "{\"valid\": false, \"message\": \"Required field\", \"severity\": \"error\"}"
                    ),
                },
            ],
        };

        Assert.False(CheckHelper.AllChecksPassing(errorComp, ctx));
        var errorWrapper = CheckHelper.ApplyChecks(new TextBox(), errorComp, ctx);
        var errorPanel = Assert.IsType<StackPanel>(errorWrapper);
        Assert.Equal(2, errorPanel.Children.Count);
        var errorTb = Assert.IsType<TextBlock>(errorPanel.Children[1]);
        Assert.Contains("ValidationError", errorTb.Classes);
        Assert.Equal("Required field", errorTb.Text);

        // Warning severity shows message but does NOT block validity
        var warningComp = new TextFieldComponent
        {
            Id = "f2",
            Checks =
            [
                new CheckRule
                {
                    Condition = DynamicValue.FromString(
                        "{\"valid\": false, \"message\": \"Password is weak\", \"severity\": \"warning\"}"
                    ),
                },
            ],
        };

        Assert.True(CheckHelper.AllChecksPassing(warningComp, ctx));
        var warnWrapper = CheckHelper.ApplyChecks(new TextBox(), warningComp, ctx);
        var warnPanel = Assert.IsType<StackPanel>(warnWrapper);
        var warnTb = Assert.IsType<TextBlock>(warnPanel.Children[1]);
        Assert.Contains("ValidationWarning", warnTb.Classes);
        Assert.Equal("Password is weak", warnTb.Text);

        // Info severity shows message and does NOT block validity
        var infoComp = new TextFieldComponent
        {
            Id = "f3",
            Checks =
            [
                new CheckRule
                {
                    Condition = DynamicValue.FromString(
                        "{\"valid\": false, \"message\": \"Format: YYYY-MM-DD\", \"severity\": \"info\"}"
                    ),
                },
            ],
        };

        Assert.True(CheckHelper.AllChecksPassing(infoComp, ctx));
        var infoWrapper = CheckHelper.ApplyChecks(new TextBox(), infoComp, ctx);
        var infoPanel = Assert.IsType<StackPanel>(infoWrapper);
        var infoTb = Assert.IsType<TextBlock>(infoPanel.Children[1]);
        Assert.Contains("ValidationInfo", infoTb.Classes);
        Assert.Equal("Format: YYYY-MM-DD", infoTb.Text);
    }
}
