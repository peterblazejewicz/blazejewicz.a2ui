using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Blazejewicz.A2Ui.Avalonia.Catalog.Entries;
using Blazejewicz.A2Ui.Core.Bindings;
using Blazejewicz.A2Ui.Core.Choices;
using Blazejewicz.A2Ui.Core.Components;
using Blazejewicz.A2Ui.Core.Messages;
using Blazejewicz.A2Ui.Core.Surfaces;
using Xunit;

namespace Blazejewicz.A2Ui.Avalonia.Tests.Catalog;

public sealed class ChoicePickerCatalogEntryTests
{
    private static (DataModel dm, MockRenderContext ctx) Setup()
    {
        var dm = new DataModel();
        return (dm, new MockRenderContext(dm));
    }

    [AvaloniaFact]
    public void Create_MutuallyExclusive_CreatesComboBox()
    {
        var (dm, ctx) = Setup();
        var entry = new ChoicePickerCatalogEntry();
        var component = new ChoicePickerComponent
        {
            Id = "cp1",
            Options =
            [
                new ChoiceOption { Label = DynamicValue.FromString("Red"), Value = "red" },
                new ChoiceOption { Label = DynamicValue.FromString("Blue"), Value = "blue" },
            ],
        };

        var control = entry.Create(component, dm, ctx);

        Assert.IsType<ComboBox>(control);
        var combo = (ComboBox)control;
        Assert.Equal(2, combo.Items.Count);
    }

    [AvaloniaFact]
    public void Create_MutuallyExclusive_PreSelectsCurrentValue()
    {
        var (dm, ctx) = Setup();
        var entry = new ChoicePickerCatalogEntry();
        var component = new ChoicePickerComponent
        {
            Id = "cp2",
            Options =
            [
                new ChoiceOption { Label = DynamicValue.FromString("Red"), Value = "red" },
                new ChoiceOption { Label = DynamicValue.FromString("Blue"), Value = "blue" },
                new ChoiceOption { Label = DynamicValue.FromString("Green"), Value = "green" },
            ],
            Value = DynamicValue.FromString("blue"),
        };

        var control = entry.Create(component, dm, ctx);

        Assert.IsType<ComboBox>(control);
        var combo = (ComboBox)control;
        Assert.Equal(1, combo.SelectedIndex);
    }

    [AvaloniaFact]
    public void Create_Filterable_CreatesAutoCompleteBox()
    {
        var (dm, ctx) = Setup();
        var entry = new ChoicePickerCatalogEntry();
        var component = new ChoicePickerComponent
        {
            Id = "cp3",
            Filterable = true,
            Options =
            [
                new ChoiceOption { Label = DynamicValue.FromString("Red"), Value = "red" },
                new ChoiceOption { Label = DynamicValue.FromString("Blue"), Value = "blue" },
            ],
        };

        var control = entry.Create(component, dm, ctx);

        Assert.IsType<AutoCompleteBox>(control);
    }

    [AvaloniaFact]
    public void Create_MultipleSelection_CreatesListBox()
    {
        var (dm, ctx) = Setup();
        var entry = new ChoicePickerCatalogEntry();
        var component = new ChoicePickerComponent
        {
            Id = "cp4",
            Variant = "multipleSelection",
            Options =
            [
                new ChoiceOption { Label = DynamicValue.FromString("Red"), Value = "red" },
                new ChoiceOption { Label = DynamicValue.FromString("Blue"), Value = "blue" },
            ],
        };

        var control = entry.Create(component, dm, ctx);

        Assert.IsType<ListBox>(control);
        var listBox = (ListBox)control;
        Assert.Equal(2, listBox.Items.Count);

        // Each item should be a ListBoxItem with a CheckBox as Content
        var firstItem = listBox.Items[0] as ListBoxItem;
        Assert.NotNull(firstItem);
        Assert.IsType<CheckBox>(firstItem.Content);
    }

    [AvaloniaFact]
    public void Create_MultipleSelection_PreSelected_DoesNotFirePhantomValueChanged()
    {
        // Arrange — pre-load the bound path with two selected values.
        var dm = new DataModel();
        dm.Apply(
            new UpdateDataModel
            {
                SurfaceId = "test",
                Path = "/selected",
                Value = JsonSerializer.SerializeToElement(new[] { "red", "blue" }),
            }
        );
        var ctx = new MockRenderContext(dm);
        var entry = new ChoicePickerCatalogEntry();
        var component = new ChoicePickerComponent
        {
            Id = "cp_phantom",
            Variant = "multipleSelection",
            Value = DynamicValue.FromPath("/selected"),
            Options =
            [
                new ChoiceOption { Label = DynamicValue.FromString("Red"), Value = "red" },
                new ChoiceOption { Label = DynamicValue.FromString("Blue"), Value = "blue" },
                new ChoiceOption { Label = DynamicValue.FromString("Green"), Value = "green" },
            ],
        };

        // Act
        var control = entry.Create(component, dm, ctx);

        // Assert — the initial selection application must not synthesize user actions upstream.
        Assert.IsType<ListBox>(control);
        var listBox = (ListBox)control;
        Assert.Equal(2, listBox.SelectedItems!.Count);
        Assert.Empty(ctx.FiredActions);
    }

    [AvaloniaFact]
    public void Create_MultipleSelection_Chips_CreatesWrapPanel()
    {
        var (dm, ctx) = Setup();
        var entry = new ChoicePickerCatalogEntry();
        var component = new ChoicePickerComponent
        {
            Id = "cp5",
            Variant = "multipleSelection",
            DisplayStyle = "chips",
            Options =
            [
                new ChoiceOption { Label = DynamicValue.FromString("Red"), Value = "red" },
                new ChoiceOption { Label = DynamicValue.FromString("Blue"), Value = "blue" },
                new ChoiceOption { Label = DynamicValue.FromString("Green"), Value = "green" },
            ],
        };

        var control = entry.Create(component, dm, ctx);

        Assert.IsType<WrapPanel>(control);
        var panel = (WrapPanel)control;
        Assert.Equal(3, panel.Children.Count);
        Assert.All(panel.Children, child => Assert.IsType<ToggleButton>(child));
        Assert.Equal(Orientation.Horizontal, panel.Orientation);
    }

    [AvaloniaFact]
    public void Update_AllFourVariants_OptionsUnchanged_PreservesItemsAndUpdatesSelectionSilently()
    {
        var (dm, ctx) = Setup();
        var entry = new ChoicePickerCatalogEntry();
        ChoiceOption[] options =
        [
            new ChoiceOption { Label = DynamicValue.FromString("Red"), Value = "red" },
            new ChoiceOption { Label = DynamicValue.FromString("Blue"), Value = "blue" },
        ];

        // 1. ComboBox
        var comboComp = new ChoicePickerComponent
        {
            Id = "cp_combo",
            Value = DynamicValue.FromPath("/color"),
            Options = options,
        };
        var combo = Assert.IsType<ComboBox>(entry.Create(comboComp, dm, ctx));
        object firstComboItem = combo.Items[0]!;
        dm.Apply("/color", JsonSerializer.SerializeToElement(new[] { "blue" }));
        Assert.True(entry.Update(combo, comboComp, dm, ctx));
        Assert.Same(firstComboItem, combo.Items[0]);
        Assert.Equal(1, combo.SelectedIndex);
        Assert.Empty(ctx.FiredActions);

        // 2. AutoCompleteBox
        var autoComp = new ChoicePickerComponent
        {
            Id = "cp_auto",
            Filterable = true,
            Value = DynamicValue.FromPath("/color"),
            Options = options,
        };
        var autoComplete = Assert.IsType<AutoCompleteBox>(entry.Create(autoComp, dm, ctx));
        object? initialSource = autoComplete.ItemsSource;
        dm.Apply("/color", JsonSerializer.SerializeToElement(new[] { "red" }));
        Assert.True(entry.Update(autoComplete, autoComp, dm, ctx));
        Assert.Same(initialSource, autoComplete.ItemsSource);
        Assert.Equal("Red", autoComplete.Text);
        Assert.Empty(ctx.FiredActions);

        // 3. ListBox (multipleSelection)
        var listComp = new ChoicePickerComponent
        {
            Id = "cp_list",
            Variant = "multipleSelection",
            Value = DynamicValue.FromPath("/multi"),
            Options = options,
        };
        var listBox = Assert.IsType<ListBox>(entry.Create(listComp, dm, ctx));
        object firstListItem = listBox.Items[0]!;
        dm.Apply("/multi", JsonSerializer.SerializeToElement(new[] { "red", "blue" }));
        Assert.True(entry.Update(listBox, listComp, dm, ctx));
        Assert.Same(firstListItem, listBox.Items[0]);
        Assert.Equal(2, listBox.SelectedItems!.Count);
        Assert.Empty(ctx.FiredActions);

        // 4. WrapPanel (multipleSelection + chips)
        var chipsComp = new ChoicePickerComponent
        {
            Id = "cp_chips",
            Variant = "multipleSelection",
            DisplayStyle = "chips",
            Value = DynamicValue.FromPath("/multi"),
            Options = options,
        };
        var chipsPanel = Assert.IsType<WrapPanel>(entry.Create(chipsComp, dm, ctx));
        Control firstChip = chipsPanel.Children[0];
        dm.Apply("/multi", JsonSerializer.SerializeToElement(new[] { "blue" }));
        Assert.True(entry.Update(chipsPanel, chipsComp, dm, ctx));
        Assert.Same(firstChip, chipsPanel.Children[0]);
        Assert.False(((ToggleButton)chipsPanel.Children[0]).IsChecked);
        Assert.True(((ToggleButton)chipsPanel.Children[1]).IsChecked);
        Assert.Empty(ctx.FiredActions);
    }

    [AvaloniaFact]
    public void Update_WhenOptionsChangeOrVariantChanges_RebuildsItemsOrReturnsFalse()
    {
        var (dm, ctx) = Setup();
        var entry = new ChoicePickerCatalogEntry();
        var initialComp = new ChoicePickerComponent
        {
            Id = "cp_mut",
            Options =
            [
                new ChoiceOption { Label = DynamicValue.FromString("Red"), Value = "red" },
            ],
        };

        var combo = Assert.IsType<ComboBox>(entry.Create(initialComp, dm, ctx));
        Assert.Single(combo.Items);

        var updatedComp = new ChoicePickerComponent
        {
            Id = "cp_mut",
            Options =
            [
                new ChoiceOption { Label = DynamicValue.FromString("Red"), Value = "red" },
                new ChoiceOption { Label = DynamicValue.FromString("Green"), Value = "green" },
            ],
        };
        Assert.True(entry.Update(combo, updatedComp, dm, ctx));
        Assert.Equal(2, combo.Items.Count);

        // Changing variant from ComboBox to multipleSelection ListBox returns false for full recreate
        var multiComp = new ChoicePickerComponent
        {
            Id = "cp_mut",
            Variant = "multipleSelection",
            Options = updatedComp.Options,
        };
        Assert.False(entry.Update(combo, multiComp, dm, ctx));
    }
}
