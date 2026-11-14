using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Blazejewicz.A2Ui.Avalonia.Catalog;
using Blazejewicz.A2Ui.Avalonia.Catalog.Entries;
using Blazejewicz.A2Ui.Core.Bindings;
using Blazejewicz.A2Ui.Core.Components;
using Blazejewicz.A2Ui.Core.Messages;
using Blazejewicz.A2Ui.Core.Surfaces;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Blazejewicz.A2Ui.Avalonia.Tests.Catalog;

public sealed class TextFieldCatalogEntryTests
{
    [AvaloniaFact]
    public void TextFieldCatalogEntry_Create_WithPathBinding_WritesBackToDataModel()
    {
        var entry = new TextFieldCatalogEntry();
        var dm = new DataModel();
        var ctx = new DataModelCapturingRenderContext(dm);

        var component = new TextFieldComponent
        {
            Id = "tf1",
            Label = DynamicValue.FromString("Username"),
            Value = DynamicValue.FromPath("/username"),
        };

        var control = entry.Create(component, dm, ctx);
        Assert.IsType<TextBox>(control);
        var tb = (TextBox)control;

        // Simulate typing -- TextChanged fires automatically
        tb.Text = "peter";

        // Data model should now have the value at /username
        Assert.Equal("peter", dm.Resolve(DynamicValue.FromPath("/username")));
    }

    [AvaloniaFact]
    public void TextFieldCatalogEntry_Create_WithoutPath_DoesNotWriteToDataModel()
    {
        var entry = new TextFieldCatalogEntry();
        var dm = new DataModel();
        var ctx = new DataModelCapturingRenderContext(dm);

        var component = new TextFieldComponent
        {
            Id = "tf2",
            Label = DynamicValue.FromString("Name"),
            Value = DynamicValue.FromString("initial"),
        };

        var control = entry.Create(component, dm, ctx);
        var tb = (TextBox)control;
        tb.Text = "changed";

        // No path binding -> data model should be empty
        Assert.Equal("{}", dm.ToJson());
    }

    [AvaloniaFact]
    public void TextFieldCatalogEntry_Create_ObscuredVariant_SetsPasswordChar()
    {
        var entry = new TextFieldCatalogEntry();
        var dm = new DataModel();
        var ctx = new DataModelCapturingRenderContext(dm);

        var component = new TextFieldComponent
        {
            Id = "pw1",
            Label = DynamicValue.FromString("Password"),
            Variant = "obscured",
        };

        var control = entry.Create(component, dm, ctx);
        var tb = (TextBox)control;
        Assert.NotEqual('\0', tb.PasswordChar);
    }

    [AvaloniaFact]
    public void TextFieldCatalogEntry_Create_FiresValueChangedWithComponentId()
    {
        var entry = new TextFieldCatalogEntry();
        var dm = new DataModel();
        var ctx = new DataModelCapturingRenderContext(dm);

        var component = new TextFieldComponent
        {
            Id = "tf3",
            Label = DynamicValue.FromString("Email"),
            Value = DynamicValue.FromPath("/email"),
        };

        var control = entry.Create(component, dm, ctx);
        var tb = (TextBox)control;
        tb.Text = "test@example.com";

        Assert.Single(ctx.FiredActions, a => a.ComponentId == "tf3");
        Assert.Equal("test@example.com", ctx.FiredActions[0].Payload);
    }

    [AvaloniaFact]
    public void TextFieldCatalogEntry_Update_WhenNotFocused_UpdatesText()
    {
        var entry = new TextFieldCatalogEntry();
        var dm = new DataModel();
        var ctx = new DataModelCapturingRenderContext(dm);

        var comp1 = new TextFieldComponent { Id = "tf4", Value = DynamicValue.FromString("before") };
        var comp2 = new TextFieldComponent { Id = "tf4", Value = DynamicValue.FromString("after") };

        var control = entry.Create(comp1, dm, ctx);
        var tb = (TextBox)control;

        bool updated = entry.Update(control, comp2, dm, ctx);

        Assert.True(updated);
        Assert.Equal("after", tb.Text);
    }

    [AvaloniaFact]
    public void TextFieldCatalogEntry_Update_DoesNotFireValueChanged()
    {
        var entry = new TextFieldCatalogEntry();
        var dm = new DataModel();
        var ctx = new DataModelCapturingRenderContext(dm);

        var comp = new TextFieldComponent { Id = "tf5", Value = DynamicValue.FromPath("/name") };

        var control = entry.Create(comp, dm, ctx);
        ctx.FiredActions.Clear(); // clear any events from Create

        // Programmatic update via Update() should NOT fire valueChanged
        var comp2 = new TextFieldComponent { Id = "tf5", Value = DynamicValue.FromString("server-value") };
        entry.Update(control, comp2, dm, ctx);

        Assert.Empty(ctx.FiredActions);
    }

    [AvaloniaFact]
    public void TextFieldCatalogEntry_Placeholder_PrecedenceOverLabel()
    {
        var entry = new TextFieldCatalogEntry();
        var dm = new DataModel();
        var ctx = new DataModelCapturingRenderContext(dm);

        var comp1 = new TextFieldComponent
        {
            Id = "tf_label_only",
            Label = DynamicValue.FromString("Username Label"),
            Placeholder = null,
        };

        var control1 = (TextBox)entry.Create(comp1, dm, ctx);
        Assert.Equal("Username Label", control1.PlaceholderText);

        var comp2 = new TextFieldComponent
        {
            Id = "tf_both",
            Label = DynamicValue.FromString("Username Label"),
            Placeholder = DynamicValue.FromString("Enter your username"),
        };

        var control2 = (TextBox)entry.Create(comp2, dm, ctx);
        Assert.Equal("Enter your username", control2.PlaceholderText);

        // Update
        entry.Update(control2, comp1, dm, ctx);
        Assert.Equal("Username Label", control2.PlaceholderText);
    }
}

/// <summary>
/// Render context that captures both data model writes and fired actions.
/// </summary>
internal sealed class DataModelCapturingRenderContext(DataModel dm) : IRenderContext
{
    public List<(string EventName, object? Payload, string? ComponentId)> FiredActions { get; } = [];

    public Control? RenderChild(string? childId) => null;

    public IEnumerable<Control> RenderChildren(string parentId) => [];

    public void FireUserAction(string eventName, object? payload = null, string? componentId = null) =>
        this.FiredActions.Add((eventName, payload, componentId));

    public string? Resolve(DynamicValue? value) => dm.Resolve(value);

    public System.Text.Json.JsonElement? ResolveJsonElement(DynamicValue? value) => dm.ResolveJsonElement(value);

    public void UpdateDataModel(string path, string? value)
    {
        var update = new UpdateDataModel
        {
            SurfaceId = "test",
            Path = path,
            Value = value is not null ? System.Text.Json.JsonSerializer.SerializeToElement(value) : null,
        };
        dm.Apply(update);
    }

    public void UpdateDataModel(string path, System.Text.Json.JsonElement? value)
    {
        var update = new UpdateDataModel
        {
            SurfaceId = "test",
            Path = path,
            Value = value,
        };
        dm.Apply(update);
    }

    public double? GetComponentWeight(string componentId) => null;

    public ILogger? Logger => null;

    public CancellationToken SurfaceCancellation => CancellationToken.None;
}
