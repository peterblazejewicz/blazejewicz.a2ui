using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Blazejewicz.A2Ui.Avalonia.Catalog;
using Blazejewicz.A2Ui.Avalonia.Controls;
using Blazejewicz.A2Ui.Avalonia.Gallery.Models;
using Blazejewicz.A2Ui.Avalonia.Gallery.Services;
using Blazejewicz.A2Ui.Avalonia.Gallery.ViewModels;
using Blazejewicz.A2Ui.Avalonia.Tests.Integration;
using Blazejewicz.A2Ui.Core;
using Blazejewicz.A2Ui.Core.Bindings;
using Blazejewicz.A2Ui.Core.Children;
using Blazejewicz.A2Ui.Core.Choices;
using Blazejewicz.A2Ui.Core.Components;
using Blazejewicz.A2Ui.Core.Surfaces;
using Blazejewicz.A2Ui.Core.Validation;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Blazejewicz.A2Ui.Avalonia.Tests;

public sealed class GalleryParityTests
{
    [AvaloniaFact]
    public async Task GalleryViewModel_ResetThenStepOneSequentially_ReloadsInvitationBuilderWithoutErrorsAsync()
    {
        var manager = new SurfaceManager();
        var loader = new GalleryDataLoader(NullLogger<GalleryDataLoader>.Instance);
        using var vm = new GalleryViewModel(manager, loader, NullLogger<GalleryViewModel>.Instance);

        var surfaceHost = new A2UiSurface();
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(GalleryViewModel.ActiveSurface))
            {
                surfaceHost.Surface = vm.ActiveSurface;
            }
        };
        vm.SurfaceRefreshRequested += (_, _) => surfaceHost.Refresh();

        var window = new Window { Content = surfaceHost };
        window.Show();
        try
        {
            await vm.InitializeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            vm.SelectedItem = Assert.Single(
                vm.DemoItems,
                item => item.Filename == "30_live-invitation-builder.json"
            );

            Assert.Empty(vm.ActionLogs);
            Assert.Equal(3, vm.ProcessedMessageCount);
            Assert.True(vm.HasComponents);
            Assert.False(vm.ShowPlaceholder);

            // Reset session and verify clean slate
            vm.ResetCommand.Execute(null);
            Assert.Equal(0, vm.ProcessedMessageCount);
            Assert.Null(vm.ActiveSurface);
            Assert.False(vm.HasComponents);
            Assert.True(vm.ShowPlaceholder);
            Assert.Empty(manager.GetSurfaces());
            Assert.Empty(vm.ActionLogs);

            // Step 1: createSurface (no inline components yet)
            vm.StepOneCommand.Execute(null);
            Assert.Empty(vm.ActionLogs);
            Assert.Equal(1, vm.ProcessedMessageCount);
            Assert.NotNull(vm.ActiveSurface);
            Assert.Equal("gallery-invitation-builder", vm.ActiveSurface.SurfaceId);
            Assert.False(vm.HasComponents);
            Assert.True(vm.ShowPlaceholder);

            // Step 2: updateComponents -> renders Invitation Builder UI
            vm.StepOneCommand.Execute(null);
            Assert.Empty(vm.ActionLogs);
            Assert.Equal(2, vm.ProcessedMessageCount);
            Assert.True(vm.HasComponents);
            Assert.False(vm.ShowPlaceholder);
            Assert.NotNull(surfaceHost.Content);
            List<TextBlock> step2Texts = GalleryTestHelper.FindAll<TextBlock>((Control)surfaceHost.Content);
            Assert.Contains(step2Texts, tb => tb.Text?.Contains("Invitation Builder") == true);

            // Step 3: updateDataModel -> populates Summer Gala & Alex Johnson
            vm.StepOneCommand.Execute(null);
            Assert.Empty(vm.ActionLogs);
            Assert.Equal(3, vm.ProcessedMessageCount);
            Assert.True(vm.HasComponents);
            Assert.False(vm.ShowPlaceholder);
            Assert.Contains("Summer Gala", vm.CurrentDataModelJson);
            Assert.Contains("Alex Johnson", vm.CurrentDataModelJson);

            List<TextBlock> step3Texts = GalleryTestHelper.FindAll<TextBlock>((Control)surfaceHost.Content);
            Assert.Contains(step3Texts, tb => tb.Text == "Summer Gala");
            Assert.Contains(step3Texts, tb => tb.Text == "Alex Johnson");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task GalleryViewModel_SwitchingBetweenCardsAndRevisiting_DoesNotLeakSurfacesOrLogErrorsAsync()
    {
        var manager = new SurfaceManager();
        var loader = new GalleryDataLoader(NullLogger<GalleryDataLoader>.Instance);
        using var vm = new GalleryViewModel(manager, loader, NullLogger<GalleryViewModel>.Instance);

        await vm.InitializeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.True(vm.DemoItems.Count >= 2);
        DemoItem card0 = vm.DemoItems[0];
        DemoItem card1 = vm.DemoItems[1];

        Assert.Single(manager.GetSurfaces());
        Assert.Equal(card0.Id, manager.GetSurfaces()[0].SurfaceId);
        Assert.Empty(vm.ActionLogs);

        // Switch Card 0 -> Card 1 -> revisit Card 0
        vm.SelectedItem = card1;
        Assert.Single(manager.GetSurfaces());
        Assert.Equal(card1.Id, manager.GetSurfaces()[0].SurfaceId);
        Assert.Empty(vm.ActionLogs);

        vm.SelectedItem = card0;
        Assert.Single(manager.GetSurfaces());
        Assert.Equal(card0.Id, manager.GetSurfaces()[0].SurfaceId);
        Assert.Empty(vm.ActionLogs);

        // Switch protocol version v1.0 -> v0.9.1 -> v1.0
        vm.SelectedProtocolVersion = "v0.9.1";
        Assert.Single(manager.GetSurfaces());
        Assert.Empty(vm.ActionLogs);

        vm.SelectedProtocolVersion = "v1.0";
        Assert.Single(manager.GetSurfaces());
        Assert.Empty(vm.ActionLogs);

        // Also verify inline components on createSurface immediately clear ShowPlaceholder
        vm.ResetCommand.Execute(null);
        Assert.True(vm.ShowPlaceholder);
        manager.Process(
            """
            {
              "version": "1.0",
              "createSurface": {
                "surfaceId": "inline-components-surface",
                "catalogId": "https://a2ui.org/catalogs/basic/v1/catalog.json",
                "components": [
                  { "id": "root", "component": "Text", "text": "Inline Ready" }
                ]
              }
            }
            """
        );
        Assert.True(vm.HasComponents);
        Assert.False(vm.ShowPlaceholder);
    }

    [AvaloniaFact]
    public void A2UiRenderer_RenderRecreatedSurfaceWithSameId_EvictsStaleControlCache()
    {
        using var renderer = new A2UiRenderer(CatalogRegistry.CreateDefault());

        var surface1 = new Surface("s1", A2UiProtocol.BasicCatalogIdV10)
        {
            ProtocolVersion = A2UiProtocol.Version10,
        };
        surface1.DataModel.SetSnapshot(JsonDocument.Parse("""{"name":"First"}""").RootElement);
        surface1.UpdateComponents([
            new TextFieldComponent
            {
                Id = "root",
                Value = DynamicValue.FromPath("/name"),
            },
        ]);

        var firstBox = Assert.IsType<TextBox>(renderer.Render(surface1));
        Assert.Equal("First", firstBox.Text);

        // Recreate a brand-new Surface instance with the same SurfaceId without calling ClearSurface
        var surface2 = new Surface("s1", A2UiProtocol.BasicCatalogIdV10)
        {
            ProtocolVersion = A2UiProtocol.Version10,
        };
        surface2.DataModel.SetSnapshot(JsonDocument.Parse("""{"name":"Second"}""").RootElement);
        surface2.UpdateComponents([
            new TextFieldComponent
            {
                Id = "root",
                Value = DynamicValue.FromPath("/name"),
            },
        ]);

        var secondBox = Assert.IsType<TextBox>(renderer.Render(surface2));
        Assert.NotSame(firstBox, secondBox);
        Assert.Equal("Second", secondBox.Text);

        // Mutating secondBox writes to surface2.DataModel, never surface1.DataModel
        secondBox.Text = "Updated";
        Assert.Equal("Updated", surface2.DataModel.Resolve(DynamicValue.FromPath("/name")));
        Assert.Equal("First", surface1.DataModel.Resolve(DynamicValue.FromPath("/name")));
    }

    [AvaloniaFact]
    public async Task GalleryWindow_SelectingDropdownOnInvitationBuilder_UpdatesLivePreviewWithoutCrashAsync()
    {
        var manager = new SurfaceManager();
        var loader = new GalleryDataLoader(NullLogger<GalleryDataLoader>.Instance);
        using var vm = new GalleryViewModel(manager, loader, NullLogger<GalleryViewModel>.Instance);

        var surfaceHost = new A2UiSurface();
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(GalleryViewModel.ActiveSurface))
            {
                surfaceHost.Surface = vm.ActiveSurface;
            }
        };
        vm.SurfaceRefreshRequested += (_, _) => surfaceHost.Refresh();
        surfaceHost.DataModelChanged += (_, _) =>
        {
            vm.RefreshDataModelJson();
            surfaceHost.Refresh();
        };

        var window = new Window { Content = surfaceHost };
        window.Show();
        try
        {
            await vm.InitializeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            vm.SelectedItem = Assert.Single(
                vm.DemoItems,
                item => item.Filename == "30_live-invitation-builder.json"
            );

            Assert.NotNull(surfaceHost.Content);
            ComboBox combo = Assert.Single(GalleryTestHelper.FindAll<ComboBox>((Control)surfaceHost.Content));
            ComboBoxItem[] initialItems = [.. combo.Items.Cast<ComboBoxItem>()];
            Assert.Equal(3, initialItems.Length);
            Assert.Equal(1, combo.SelectedIndex);

            // Select "Grand Ballroom" (index 0) — triggers SelectionChanged -> DataModelChanged -> synchronous Refresh()
            combo.SelectedIndex = 0;

            Assert.Same(combo, Assert.Single(GalleryTestHelper.FindAll<ComboBox>((Control)surfaceHost.Content)));
            Assert.Equal(initialItems.Length, combo.Items.Count);
            for (int i = 0; i < initialItems.Length; i++)
            {
                Assert.Same(initialItems[i], combo.Items[i]);
            }

            using (JsonDocument doc = JsonDocument.Parse(vm.ActiveSurface!.DataModel.ToJson()))
            {
                JsonElement locationElement = doc.RootElement.GetProperty("event").GetProperty("location");
                Assert.Equal(JsonValueKind.Array, locationElement.ValueKind);
                Assert.Equal("ballroom", locationElement[0].GetString());
            }

            List<TextBlock> textsAfterFirstSelect = GalleryTestHelper.FindAll<TextBlock>((Control)surfaceHost.Content);
            Assert.Contains(textsAfterFirstSelect, tb => tb.Text == "Location: ballroom");

            // Select "Garden Pavillion" (index 2) to verify repeated dropdown changes also preserve items & update live preview
            combo.SelectedIndex = 2;
            for (int i = 0; i < initialItems.Length; i++)
            {
                Assert.Same(initialItems[i], combo.Items[i]);
            }

            List<TextBlock> textsAfterSecondSelect = GalleryTestHelper.FindAll<TextBlock>((Control)surfaceHost.Content);
            Assert.Contains(textsAfterSecondSelect, tb => tb.Text == "Location: garden");

            // Composite DateTimeInput (enableDate + enableTime) must preserve both CalendarDatePicker and TimePicker across refreshes
            Assert.Single(GalleryTestHelper.FindAll<CalendarDatePicker>((Control)surfaceHost.Content));
            Assert.Single(GalleryTestHelper.FindAll<TimePicker>((Control)surfaceHost.Content));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void GalleryWindow_AllChoicePickerVariantsAndTypedInputs_RefreshOnDataModelChangedPreservesControls()
    {
        var surface = new Surface("typed-inputs", A2UiProtocol.BasicCatalogIdV10)
        {
            ProtocolVersion = A2UiProtocol.Version10,
        };
        surface.UpdateComponents([
            new ColumnComponent
            {
                Id = "root",
                Children = ChildList.FromIds(["cp_combo", "cp_auto", "cp_list", "cp_chips", "cb_flag", "sl_num"]),
            },
            new ChoicePickerComponent
            {
                Id = "cp_combo",
                Value = DynamicValue.FromPath("/combo"),
                Options =
                [
                    new ChoiceOption { Label = DynamicValue.FromString("Option A"), Value = "a" },
                    new ChoiceOption { Label = DynamicValue.FromString("Option B"), Value = "b" },
                ],
            },
            new ChoicePickerComponent
            {
                Id = "cp_auto",
                Filterable = true,
                Value = DynamicValue.FromPath("/auto"),
                Options =
                [
                    new ChoiceOption { Label = DynamicValue.FromString("Alpha"), Value = "alpha" },
                    new ChoiceOption { Label = DynamicValue.FromString("Beta"), Value = "beta" },
                ],
            },
            new ChoicePickerComponent
            {
                Id = "cp_list",
                Variant = "multipleSelection",
                Value = DynamicValue.FromPath("/list"),
                Options =
                [
                    new ChoiceOption { Label = DynamicValue.FromString("First"), Value = "x" },
                    new ChoiceOption { Label = DynamicValue.FromString("Second"), Value = "y" },
                ],
            },
            new ChoicePickerComponent
            {
                Id = "cp_chips",
                Variant = "multipleSelection",
                DisplayStyle = "chips",
                Value = DynamicValue.FromPath("/chips"),
                Options =
                [
                    new ChoiceOption { Label = DynamicValue.FromString("Tag 1"), Value = "t1" },
                    new ChoiceOption { Label = DynamicValue.FromString("Tag 2"), Value = "t2" },
                ],
            },
            new CheckBoxComponent
            {
                Id = "cb_flag",
                Label = DynamicValue.FromString("Enabled"),
                Value = DynamicValue.FromPath("/enabled"),
            },
            new SliderComponent
            {
                Id = "sl_num",
                Min = DynamicValue.FromNumber(0),
                Max = DynamicValue.FromNumber(100),
                Value = DynamicValue.FromPath("/score"),
            },
        ]);

        var surfaceHost = new A2UiSurface { Surface = surface };
        surfaceHost.DataModelChanged += (_, _) => surfaceHost.Refresh();

        var window = new Window { Content = surfaceHost };
        window.Show();
        try
        {
            var rootControl = (Control)surfaceHost.Content!;
            ComboBox combo = Assert.Single(GalleryTestHelper.FindAll<ComboBox>(rootControl));
            AutoCompleteBox autoComplete = Assert.Single(GalleryTestHelper.FindAll<AutoCompleteBox>(rootControl));
            ListBox listBox = Assert.Single(GalleryTestHelper.FindAll<ListBox>(rootControl));
            WrapPanel chipsPanel = Assert.Single(GalleryTestHelper.FindAll<WrapPanel>(rootControl));
            CheckBox standaloneCheckBox = Assert.Single(
                GalleryTestHelper.FindAll<CheckBox>(rootControl),
                cb => cb.Content as string == "Enabled"
            );
            Slider slider = Assert.Single(GalleryTestHelper.FindAll<Slider>(rootControl));

            var initialComboItem = (ComboBoxItem)combo.Items[1]!;
            var initialListItem = (ListBoxItem)listBox.Items[0]!;
            var initialChipButton = (ToggleButton)chipsPanel.Children[1];

            combo.SelectedIndex = 1;
            autoComplete.SelectedItem = "Alpha";
            listBox.SelectedItems!.Add(initialListItem);
            initialChipButton.IsChecked = true;
            standaloneCheckBox.IsChecked = true;
            slider.Value = 42.5;
            slider.RaiseEvent(new VectorEventArgs { RoutedEvent = Thumb.DragCompletedEvent });

            // All controls and inner items are preserved across synchronous Refresh() calls
            var refreshedRoot = (Control)surfaceHost.Content!;
            Assert.Same(combo, Assert.Single(GalleryTestHelper.FindAll<ComboBox>(refreshedRoot)));
            Assert.Same(initialComboItem, combo.Items[1]);
            Assert.Same(autoComplete, Assert.Single(GalleryTestHelper.FindAll<AutoCompleteBox>(refreshedRoot)));
            Assert.Same(listBox, Assert.Single(GalleryTestHelper.FindAll<ListBox>(refreshedRoot)));
            Assert.Same(initialListItem, listBox.Items[0]);
            Assert.Same(chipsPanel, Assert.Single(GalleryTestHelper.FindAll<WrapPanel>(refreshedRoot)));
            Assert.Same(initialChipButton, chipsPanel.Children[1]);

            using JsonDocument doc = JsonDocument.Parse(surface.DataModel.ToJson());
            JsonElement root = doc.RootElement;
            Assert.Equal("b", root.GetProperty("combo")[0].GetString());
            Assert.Equal("alpha", root.GetProperty("auto")[0].GetString());
            Assert.Equal("x", root.GetProperty("list")[0].GetString());
            Assert.Equal("t2", root.GetProperty("chips")[0].GetString());
            Assert.True(root.GetProperty("enabled").GetBoolean());
            Assert.Equal(42.5, root.GetProperty("score").GetDouble());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void InputEntries_AddingChecksDynamicallyViaUpdateComponents_WrapsAndDisplaysErrorsAndPreservesCompositeTimePicker()
    {
        var surface = new Surface("dynamic-checks", A2UiProtocol.BasicCatalogIdV10)
        {
            ProtocolVersion = A2UiProtocol.Version10,
        };
        surface.UpdateComponents([
            new ColumnComponent
            {
                Id = "root",
                Children = ChildList.FromIds(["tf1", "dt_comp", "dt_date", "cb1", "sl1"]),
            },
            new TextFieldComponent { Id = "tf1", Value = DynamicValue.FromPath("/tf") },
            new DateTimeInputComponent
            {
                Id = "dt_comp",
                EnableDate = true,
                EnableTime = true,
                Value = DynamicValue.FromPath("/dtComp"),
            },
            new DateTimeInputComponent
            {
                Id = "dt_date",
                EnableDate = true,
                EnableTime = false,
                Value = DynamicValue.FromPath("/dtDate"),
            },
            new CheckBoxComponent { Id = "cb1", Label = DynamicValue.FromString("Accept"), Value = DynamicValue.FromPath("/cb") },
            new SliderComponent { Id = "sl1", Min = DynamicValue.FromNumber(0), Max = DynamicValue.FromNumber(10), Value = DynamicValue.FromPath("/sl") },
        ]);

        var surfaceHost = new A2UiSurface { Surface = surface };
        var window = new Window { Content = surfaceHost };
        window.Show();
        try
        {
            static CheckRule[] FailingRule(string msg) =>
                [new CheckRule { Condition = DynamicValue.FromBool(false), Message = msg }];

            surface.UpdateComponents([
                new TextFieldComponent { Id = "tf1", Value = DynamicValue.FromPath("/tf"), Checks = FailingRule("ERR_TF") },
                new DateTimeInputComponent
                {
                    Id = "dt_comp",
                    EnableDate = true,
                    EnableTime = true,
                    Value = DynamicValue.FromPath("/dtComp"),
                    Checks = FailingRule("ERR_DT_COMP"),
                },
                new DateTimeInputComponent
                {
                    Id = "dt_date",
                    EnableDate = true,
                    EnableTime = false,
                    Value = DynamicValue.FromPath("/dtDate"),
                    Checks = FailingRule("ERR_DT_DATE"),
                },
                new CheckBoxComponent
                {
                    Id = "cb1",
                    Label = DynamicValue.FromString("Accept"),
                    Value = DynamicValue.FromPath("/cb"),
                    Checks = FailingRule("ERR_CB"),
                },
                new SliderComponent
                {
                    Id = "sl1",
                    Min = DynamicValue.FromNumber(0),
                    Max = DynamicValue.FromNumber(10),
                    Value = DynamicValue.FromPath("/sl"),
                    Checks = FailingRule("ERR_SL"),
                },
            ]);

            surfaceHost.Refresh();

            var rootControl = (Control)surfaceHost.Content!;
            List<TextBlock> texts = GalleryTestHelper.FindAll<TextBlock>(rootControl);
            Assert.Contains(texts, tb => tb.Text == "ERR_TF");
            Assert.Contains(texts, tb => tb.Text == "ERR_DT_COMP");
            Assert.Contains(texts, tb => tb.Text == "ERR_DT_DATE");
            Assert.Contains(texts, tb => tb.Text == "ERR_CB");
            Assert.Contains(texts, tb => tb.Text == "ERR_SL");
            Assert.Equal(2, GalleryTestHelper.FindAll<CalendarDatePicker>(rootControl).Count);
            Assert.Single(GalleryTestHelper.FindAll<TimePicker>(rootControl));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task GalleryWindow_SwitchingProtocolVersionDropdownWhileItemSelected_DoesNotThrowAsync()
    {
        var manager = new SurfaceManager();
        var loader = new GalleryDataLoader(NullLogger<GalleryDataLoader>.Instance);
        using var vm = new GalleryViewModel(manager, loader, NullLogger<GalleryViewModel>.Instance);

        var versionCombo = new ComboBox
        {
            [!ItemsControl.ItemsSourceProperty] = new Binding(nameof(GalleryViewModel.AvailableProtocolVersions)),
            [!SelectingItemsControl.SelectedItemProperty] = new Binding(
                nameof(GalleryViewModel.SelectedProtocolVersion)
            )
            {
                Mode = BindingMode.TwoWay,
            },
        };
        var demoListBox = new ListBox
        {
            [!ItemsControl.ItemsSourceProperty] = new Binding(nameof(GalleryViewModel.DemoItems)),
            [!SelectingItemsControl.SelectedItemProperty] = new Binding(nameof(GalleryViewModel.SelectedItem))
            {
                Mode = BindingMode.TwoWay,
            },
        };

        var panel = new StackPanel { DataContext = vm, Children = { versionCombo, demoListBox } };
        var window = new Window { Content = panel };
        window.Show();
        try
        {
            await vm.InitializeAsync().ConfigureAwait(true);
            Assert.NotEmpty(vm.DemoItems);
            Assert.NotNull(demoListBox.SelectedItem);

            // Switch protocol version via ComboBox selection while ListBox has an active selection
            versionCombo.SelectedIndex = 1; // "v0.9.1"
            Assert.Equal("v0.9.1", vm.SelectedProtocolVersion);
            Assert.NotEmpty(vm.DemoItems);
            Assert.NotNull(vm.SelectedItem);
            Assert.Empty(vm.ActionLogs);

            versionCombo.SelectedIndex = 0; // "v1.0"
            Assert.Equal("v1.0", vm.SelectedProtocolVersion);
            Assert.NotEmpty(vm.DemoItems);
            Assert.NotNull(vm.SelectedItem);
            Assert.Empty(vm.ActionLogs);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task GalleryWindow_ProductCardAddToCart_LogsEmptyObjectContextAndSendDataModelAsync()
    {
        var manager = new SurfaceManager();
        var loader = new GalleryDataLoader(NullLogger<GalleryDataLoader>.Instance);
        using var vm = new GalleryViewModel(manager, loader, NullLogger<GalleryViewModel>.Instance);

        var surfaceHost = new A2UiSurface();
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(GalleryViewModel.ActiveSurface))
            {
                surfaceHost.Surface = vm.ActiveSurface;
            }
        };
        vm.SurfaceRefreshRequested += (_, _) => surfaceHost.Refresh();
        surfaceHost.UserActionFired += (_, e) => vm.LogAction(e);

        var window = new Window { Content = surfaceHost };
        window.Show();
        try
        {
            await vm.InitializeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            vm.SelectedItem = Assert.Single(
                vm.DemoItems,
                item => item.Filename == "05_product-card.json"
            );

            Assert.NotNull(vm.ActiveSurface);
            Assert.True(vm.ActiveSurface.SendDataModel);
            Assert.NotNull(surfaceHost.Content);

            Button addToCartButton = Assert.Single(GalleryTestHelper.FindAll<Button>((Control)surfaceHost.Content));
            GalleryTestHelper.ClickButton(addToCartButton);

            string logEntry = Assert.Single(vm.ActionLogs);
            int newlineIndex = logEntry.IndexOf('\n', StringComparison.Ordinal);
            Assert.True(newlineIndex > 0);
            string jsonPart = logEntry[(newlineIndex + 1)..];

            using JsonDocument doc = JsonDocument.Parse(jsonPart);
            JsonElement root = doc.RootElement;
            Assert.Equal("addToCart", root.GetProperty("name").GetString());
            Assert.Equal("gallery-product-card", root.GetProperty("surfaceId").GetString());
            Assert.Equal("add_cart_btn", root.GetProperty("sourceComponentId").GetString());

            JsonElement contextEl = root.GetProperty("context");
            Assert.Equal(JsonValueKind.Object, contextEl.ValueKind);
            Assert.Empty(contextEl.EnumerateObject());

            JsonElement dmEl = root.GetProperty("a2uiRendererDataModel");
            Assert.Equal(JsonValueKind.Object, dmEl.ValueKind);
            Assert.Equal("Wireless Headphones Pro", dmEl.GetProperty("name").GetString());
            Assert.Equal(199.99, dmEl.GetProperty("price").GetDouble());
        }
        finally
        {
            window.Close();
        }
    }
}
