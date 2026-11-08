using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Blazejewicz.A2Ui.Core;
using Blazejewicz.A2Ui.Core.Bindings;
using Blazejewicz.A2Ui.Core.Components;
using Blazejewicz.A2Ui.Core.Surfaces;

namespace Blazejewicz.A2Ui.Avalonia.Catalog.Entries;

/// <summary>Extracts a binding path from a <see cref="DynamicValue"/> using pattern matching.</summary>
internal static class DynamicValueExtensions
{
    public static string? GetBindingPath(this DynamicValue? value) =>
        value is DynamicValue.PathValue p ? p.DataPath : null;
}

/// <summary>Standard event name for input value changes.</summary>
internal static class InputEvents
{
    public const string ValueChanged = "valueChanged";
}

/// <summary>A2UI "TextField" → TextBox with two-way data model binding.</summary>
public sealed class TextFieldCatalogEntry : ICatalogEntry
{
    private sealed class TextFieldBindingState(IRenderContext context, string? bindingPath, string componentId)
    {
        public IRenderContext Context { get; set; } = context;
        public string? BindingPath { get; set; } = bindingPath;
        public string ComponentId { get; set; } = componentId;
        public bool IsUpdating { get; set; }
    }

    /// <inheritdoc />
    public string ComponentType => "TextField";

    /// <inheritdoc />
    public Control Create(A2UiComponent component, DataModel dataModel, IRenderContext context)
    {
        var typed = (TextFieldComponent)component;
        var tb = new TextBox
        {
            Text = context.Resolve(typed.Value) ?? string.Empty,
            PlaceholderText = context.Resolve(typed.Placeholder) ?? context.Resolve(typed.Label),
            PasswordChar = typed.Variant is "obscured" ? '\u2022' : '\0',
            Tag = new TextFieldBindingState(context, typed.Value.GetBindingPath(), component.Id),
        };

        tb.PropertyChanged += (sender, args) =>
        {
            if (args.Property != TextBox.TextProperty)
            {
                return;
            }

            if (sender is TextBox box && box.Tag is TextFieldBindingState s)
            {
                if (s.IsUpdating)
                {
                    return;
                }

                InputHelper.NotifyValueChanged(s.Context, s.BindingPath, box.Text, s.ComponentId);
            }
        };

        return CheckHelper.ApplyChecks(tb, component, context);
    }

    /// <inheritdoc />
    public bool Update(Control existing, A2UiComponent component, DataModel dataModel, IRenderContext context)
    {
        var typed = (TextFieldComponent)component;
        // Find the TextBox — either directly cached or inside a check wrapper StackPanel
        TextBox? tb = CheckHelper.FindInner<TextBox>(existing);
        if (tb is null)
        {
            return false;
        }

        if (tb.Tag is TextFieldBindingState state)
        {
            state.Context = context;
            state.BindingPath = typed.Value.GetBindingPath();
            state.ComponentId = component.Id;

            if (!tb.IsFocused)
            {
                state.IsUpdating = true;
                tb.Text = context.Resolve(typed.Value) ?? string.Empty;
                state.IsUpdating = false;
            }
        }
        else if (!tb.IsFocused)
        {
            tb.Text = context.Resolve(typed.Value) ?? string.Empty;
        }

        tb.PlaceholderText = context.Resolve(typed.Placeholder) ?? context.Resolve(typed.Label);
        tb.PasswordChar = typed.Variant is "obscured" ? '\u2022' : '\0';

        // Re-evaluate checks in place (preserves focus, clears stale checks when empty)
        if (existing is StackPanel wrapper)
        {
            CheckHelper.UpdateChecks(wrapper, component, context);
        }

        return true;
    }
}

/// <summary>
/// A2UI "DateTimeInput" → date picker, time picker, or both.
/// Respects <see cref="DateTimeInputComponent.EnableDate"/> (default true) and
/// <see cref="DateTimeInputComponent.EnableTime"/> (default false) to choose the control layout:
/// <list type="bullet">
///   <item>Date only → <see cref="CalendarDatePicker"/>, output <c>yyyy-MM-dd</c></item>
///   <item>Date + time → <see cref="StackPanel"/> with CalendarDatePicker + TimePicker, output <c>yyyy-MM-ddTHH:mm:ss</c></item>
///   <item>Time only → <see cref="TimePicker"/>, output <c>HH:mm:ss</c></item>
/// </list>
/// </summary>
public sealed class DateTimeInputCatalogEntry : ICatalogEntry
{
    private const string DateTimeTag = "__dateTime";

    private sealed class DateTimeBindingState(IRenderContext context, string? bindingPath, string componentId)
    {
        public IRenderContext Context { get; set; } = context;
        public string? BindingPath { get; set; } = bindingPath;
        public string ComponentId { get; set; } = componentId;
    }

    /// <inheritdoc />
    public string ComponentType => "DateTimeInput";

    /// <inheritdoc />
    public Control Create(A2UiComponent component, DataModel dataModel, IRenderContext context)
    {
        var typed = (DateTimeInputComponent)component;
        bool enableDate = typed.EnableDate ?? true;
        bool enableTime = typed.EnableTime ?? false;
        string? bindingPath = typed.Value.GetBindingPath();
        string componentId = component.Id;
        var raw = context.Resolve(typed.Value);

        if (enableDate && enableTime)
        {
            return CheckHelper.ApplyChecks(
                CreateDateTimePicker(raw, bindingPath, componentId, context, component),
                component,
                context
            );
        }

        if (!enableDate && enableTime)
        {
            return CheckHelper.ApplyChecks(
                CreateTimePicker(raw, bindingPath, componentId, context),
                component,
                context
            );
        }

        // Default: date only
        return CheckHelper.ApplyChecks(
            CreateDatePicker(raw, bindingPath, componentId, context, component),
            component,
            context
        );
    }

    /// <inheritdoc />
    public bool Update(Control existing, A2UiComponent component, DataModel dataModel, IRenderContext context)
    {
        var typed = (DateTimeInputComponent)component;
        bool enableDate = typed.EnableDate ?? true;
        bool enableTime = typed.EnableTime ?? false;
        var raw = context.Resolve(typed.Value);

        bool isComposite = CheckHelper.FindInner<StackPanel>(existing, DateTimeTag) is not null;
        bool isTimeOnly = !isComposite && CheckHelper.FindInner<TimePicker>(existing) is not null;
        bool isDateOnly = !isComposite && !isTimeOnly && CheckHelper.FindInner<CalendarDatePicker>(existing) is not null;

        // If the mode changed, signal recreate
        if (enableDate && enableTime && !isComposite)
        {
            return false;
        }

        if (!enableDate && enableTime && !isTimeOnly)
        {
            return false;
        }

        if (enableDate && !enableTime && !isDateOnly)
        {
            return false;
        }

        // Date + time composite panel
        if (CheckHelper.FindInner<StackPanel>(existing, DateTimeTag) is { } panel)
        {
            var datePicker = panel.Children.OfType<CalendarDatePicker>().FirstOrDefault();
            var timePicker = panel.Children.OfType<TimePicker>().FirstOrDefault();
            if (datePicker is null || timePicker is null)
            {
                return false;
            }

            if (datePicker.Tag is DateTimeBindingState s)
            {
                s.Context = context;
                s.BindingPath = typed.Value.GetBindingPath();
                s.ComponentId = component.Id;
            }

            if (!datePicker.IsFocused && raw is not null && DateTime.TryParse(raw, out var dt))
            {
                datePicker.SelectedDate = dt.Date;
                timePicker.SelectedTime = dt.TimeOfDay;
            }

            datePicker.PlaceholderText = context.Resolve(component.Label) ?? "Select date";

            if (existing is StackPanel compositeWrapper)
            {
                CheckHelper.UpdateChecks(compositeWrapper, component, context);
            }

            return true;
        }

        // Time only
        if (CheckHelper.FindInner<TimePicker>(existing) is { } tp)
        {
            if (tp.Tag is DateTimeBindingState s)
            {
                s.Context = context;
                s.BindingPath = typed.Value.GetBindingPath();
                s.ComponentId = component.Id;
            }

            if (raw is not null && TimeSpan.TryParse(raw, out var ts))
            {
                tp.SelectedTime = ts;
            }

            if (existing is StackPanel tpWrapper)
            {
                CheckHelper.UpdateChecks(tpWrapper, component, context);
            }

            return true;
        }

        // Date only (original)
        CalendarDatePicker? picker = CheckHelper.FindInner<CalendarDatePicker>(existing);
        if (picker is null)
        {
            return false;
        }

        if (picker.Tag is DateTimeBindingState dateState)
        {
            dateState.Context = context;
            dateState.BindingPath = typed.Value.GetBindingPath();
            dateState.ComponentId = component.Id;
        }

        if (!picker.IsFocused)
        {
            picker.SelectedDate =
                raw is not null && DateOnly.TryParse(raw, out var date) ? date.ToDateTime(TimeOnly.MinValue) : null;
        }

        picker.PlaceholderText = context.Resolve(component.Label) ?? "Select date";

        if (existing is StackPanel wrapper)
        {
            CheckHelper.UpdateChecks(wrapper, component, context);
        }

        return true;
    }

    private static CalendarDatePicker CreateDatePicker(
        string? raw,
        string? bindingPath,
        string componentId,
        IRenderContext ctx,
        A2UiComponent c
    )
    {
        var picker = new CalendarDatePicker
        {
            PlaceholderText = ctx.Resolve(c.Label) ?? "Select date",
            Tag = new DateTimeBindingState(ctx, bindingPath, componentId),
        };

        if (raw is not null && DateOnly.TryParse(raw, out var date))
        {
            picker.SelectedDate = date.ToDateTime(TimeOnly.MinValue);
        }

        picker.SelectedDateChanged += (_, _) =>
        {
            if (picker.Tag is DateTimeBindingState s)
            {
                string? isoDate = picker.SelectedDate?.ToString("yyyy-MM-dd");
                InputHelper.NotifyValueChanged(s.Context, s.BindingPath, isoDate, s.ComponentId);
            }
        };

        return picker;
    }

    private static StackPanel CreateDateTimePicker(
        string? raw,
        string? bindingPath,
        string componentId,
        IRenderContext ctx,
        A2UiComponent c
    )
    {
        var datePicker = new CalendarDatePicker
        {
            PlaceholderText = ctx.Resolve(c.Label) ?? "Select date",
            Tag = new DateTimeBindingState(ctx, bindingPath, componentId),
        };
        var timePicker = new TimePicker { ClockIdentifier = "24HourClock" };

        if (raw is not null && DateTime.TryParse(raw, out var dt))
        {
            datePicker.SelectedDate = dt.Date;
            timePicker.SelectedTime = dt.TimeOfDay;
        }

        void NotifyCombined()
        {
            if (datePicker.Tag is not DateTimeBindingState s)
            {
                return;
            }

            var d = datePicker.SelectedDate;
            var t = timePicker.SelectedTime;
            if (d is null && t is null)
            {
                InputHelper.NotifyValueChanged(s.Context, s.BindingPath, null, s.ComponentId);
                return;
            }

            var dateVal = d?.Date ?? DateTime.Today;
            var timeVal = t ?? TimeSpan.Zero;
            string iso = new DateTime(dateVal.Ticks + timeVal.Ticks).ToString("yyyy-MM-ddTHH:mm:ss");
            InputHelper.NotifyValueChanged(s.Context, s.BindingPath, iso, s.ComponentId);
        }

        datePicker.SelectedDateChanged += (_, _) => NotifyCombined();
        timePicker.SelectedTimeChanged += (_, _) => NotifyCombined();

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Tag = DateTimeTag,
            Children = { datePicker, timePicker },
        };
    }

    private static TimePicker CreateTimePicker(string? raw, string? bindingPath, string componentId, IRenderContext ctx)
    {
        var timePicker = new TimePicker
        {
            ClockIdentifier = "24HourClock",
            Tag = new DateTimeBindingState(ctx, bindingPath, componentId),
        };

        if (raw is not null && TimeSpan.TryParse(raw, out var ts))
        {
            timePicker.SelectedTime = ts;
        }

        timePicker.SelectedTimeChanged += (_, _) =>
        {
            if (timePicker.Tag is DateTimeBindingState s)
            {
                string? isoTime = timePicker.SelectedTime?.ToString(@"hh\:mm\:ss");
                InputHelper.NotifyValueChanged(s.Context, s.BindingPath, isoTime, s.ComponentId);
            }
        };

        return timePicker;
    }
}

/// <summary>
/// A2UI "ChoicePicker" → variant-aware control.
/// - mutuallyExclusive (default): ComboBox, or AutoCompleteBox when filterable.
/// - multipleSelection: ListBox with CheckBox items (checkbox style) or
///   WrapPanel with ToggleButton items (chips style).
/// Value is always a DynamicStringList (array of selected values).
/// </summary>
public sealed class ChoicePickerCatalogEntry : ICatalogEntry
{
    private sealed class ChoicePickerBindingState(IRenderContext context, string? bindingPath, string componentId)
    {
        public IRenderContext Context { get; set; } = context;
        public string? BindingPath { get; set; } = bindingPath;
        public string ComponentId { get; set; } = componentId;
    }

    /// <inheritdoc />
    public string ComponentType => "ChoicePicker";

    /// <inheritdoc />
    public Control Create(A2UiComponent component, DataModel dataModel, IRenderContext context)
    {
        var typed = (ChoicePickerComponent)component;
        string? bindingPath = typed.Value.GetBindingPath();
        string componentId = component.Id;
        bool isMultiple = typed.Variant is "multipleSelection";
        var currentValues = ResolveCurrentValues(typed.Value, context);

        Control control = isMultiple
            ? CreateMultipleSelection(typed, context, bindingPath, componentId, currentValues)
            : CreateMutuallyExclusive(typed, context, bindingPath, componentId, currentValues);

        return CheckHelper.ApplyChecks(control, component, context);
    }

    /// <inheritdoc />
    public bool Update(Control existing, A2UiComponent component, DataModel dataModel, IRenderContext context)
    {
        var typed = (ChoicePickerComponent)component;
        // Only support in-place update for the simple ComboBox case (mutuallyExclusive, non-filterable).
        // Multi-select and filterable variants require full re-create.
        ComboBox? combo = CheckHelper.FindInner<ComboBox>(existing);
        if (combo is null)
        {
            return false;
        }

        if (combo.Tag is ChoicePickerBindingState state)
        {
            state.Context = context;
            state.BindingPath = typed.Value.GetBindingPath();
            state.ComponentId = component.Id;
        }

        combo.PlaceholderText = context.Resolve(component.Label);

        // Synchronize combo.Items with typed.Options
        combo.Items.Clear();
        if (typed.Options is { } options)
        {
            foreach (var opt in options)
            {
                combo.Items.Add(
                    new ComboBoxItem { Content = context.Resolve(opt.Label), Tag = opt.Value }
                );
            }
        }

        // Update selected index to match current data model value
        var currentValues = ResolveCurrentValues(typed.Value, context);
        int selectedIndex = -1;
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is ComboBoxItem item && item.Tag is string val && currentValues.Contains(val))
            {
                selectedIndex = i;
                break;
            }
        }
        combo.SelectedIndex = selectedIndex;

        if (existing is StackPanel wrapper)
        {
            CheckHelper.UpdateChecks(wrapper, component, context);
        }

        return true;
    }

    private static Control CreateMutuallyExclusive(
        ChoicePickerComponent c,
        IRenderContext ctx,
        string? bindingPath,
        string componentId,
        HashSet<string> currentValues
    )
    {
        if (c.Filterable is true)
        {
            var autoComplete = new AutoCompleteBox
            {
                PlaceholderText = ctx.Resolve(c.Label),
                FilterMode = AutoCompleteFilterMode.ContainsOrdinal,
            };

            // Resolve labels once and pair each with its option value so we can map
            // the user's selected label (AutoCompleteBox exposes SelectedItem as the
            // string we stored) back to its stable option value.
            (string ResolvedLabel, string Value)[] resolvedOptions = c.Options is { } opts
                ? [.. opts.Select(o => (ctx.Resolve(o.Label) ?? string.Empty, o.Value))]
                : [];

            autoComplete.ItemsSource = resolvedOptions.Select(o => o.ResolvedLabel).ToArray();

            // Set initial selected text
            if (currentValues.Count > 0)
            {
                var match = resolvedOptions.FirstOrDefault(o => currentValues.Contains(o.Value));
                if (match.Value is not null)
                {
                    autoComplete.Text = match.ResolvedLabel;
                }
            }

            autoComplete.SelectionChanged += (_, _) =>
            {
                string? selectedLabel = autoComplete.SelectedItem as string;
                string? selectedValue = resolvedOptions.FirstOrDefault(o => o.ResolvedLabel == selectedLabel).Value;
                if (selectedValue is not null)
                {
                    InputHelper.NotifyValueChanged(ctx, bindingPath, selectedValue, componentId);
                }
            };

            return autoComplete;
        }

        var combo = new ComboBox { PlaceholderText = ctx.Resolve(c.Label) };
        combo.Tag = new ChoicePickerBindingState(ctx, bindingPath, componentId);

        if (c.Options is { } comboOpts)
        {
            int selectedIndex = -1;
            for (int i = 0; i < comboOpts.Length; i++)
            {
                combo.Items.Add(
                    new ComboBoxItem { Content = ctx.Resolve(comboOpts[i].Label), Tag = comboOpts[i].Value }
                );
                if (currentValues.Contains(comboOpts[i].Value))
                {
                    selectedIndex = i;
                }
            }
            if (selectedIndex >= 0)
            {
                combo.SelectedIndex = selectedIndex;
            }
        }

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.Tag is ChoicePickerBindingState s)
            {
                string? selectedValue = (combo.SelectedItem as ComboBoxItem)?.Tag as string;
                InputHelper.NotifyValueChanged(s.Context, s.BindingPath, selectedValue, s.ComponentId);
            }
        };

        return combo;
    }

    private static Control CreateMultipleSelection(
        ChoicePickerComponent c,
        IRenderContext ctx,
        string? bindingPath,
        string componentId,
        HashSet<string> currentValues
    )
    {
        bool useChips = c.DisplayStyle is "chips";

        if (useChips)
        {
            var panel = new WrapPanel { Orientation = Orientation.Horizontal };
            if (c.Options is { } opts)
            {
                foreach (var opt in opts)
                {
                    var toggle = new ToggleButton
                    {
                        Content = ctx.Resolve(opt.Label),
                        Tag = opt.Value,
                        IsChecked = currentValues.Contains(opt.Value),
                        Margin = new Thickness(2),
                    };
                    toggle.Classes.Add("chip");
                    panel.Children.Add(toggle);
                }
            }

            // Fire on any toggle change
            panel.AddHandler(
                ToggleButton.IsCheckedChangedEvent,
                (_, _) =>
                {
                    var selected = panel
                        .Children.OfType<ToggleButton>()
                        .Where(t => t.IsChecked == true)
                        .Select(t => t.Tag as string)
                        .OfType<string>()
                        .ToArray();
                    string serialized = System.Text.Json.JsonSerializer.Serialize(
                        selected,
                        A2UiJsonSerializerContext.Default.StringArray
                    );
                    InputHelper.NotifyValueChanged(ctx, bindingPath, serialized, componentId);
                },
                RoutingStrategies.Bubble
            );

            return panel;
        }

        // Default: checkbox-style ListBox
        var listBox = new ListBox { SelectionMode = SelectionMode.Multiple | SelectionMode.Toggle };

        if (c.Options is { } listOpts)
        {
            foreach (var opt in listOpts)
            {
                var item = new ListBoxItem
                {
                    Content = new CheckBox
                    {
                        Content = ctx.Resolve(opt.Label),
                        IsChecked = currentValues.Contains(opt.Value),
                    },
                    Tag = opt.Value,
                };
                listBox.Items.Add(item);
            }
        }

        // Apply initial selection to ListBox model BEFORE subscribing to SelectionChanged.
        // Avalonia raises SelectionChanged synchronously on every SelectedItems.Add call;
        // subscribing first would fire a phantom valueChanged per pre-selected option on the
        // initial render, echoing the agent's own state back to it before any user interaction.
        foreach (var item in listBox.Items.OfType<ListBoxItem>())
        {
            if (item.Content is CheckBox cb && cb.IsChecked == true)
            {
                listBox.SelectedItems!.Add(item);
            }
        }

        // SelectionChanged is the source of truth — sync CheckBox visuals from it
        listBox.SelectionChanged += (_, _) =>
        {
            // Sync CheckBox visuals to match actual selection state
            foreach (var lbi in listBox.Items.OfType<ListBoxItem>())
            {
                if (lbi.Content is CheckBox innerCb)
                {
                    innerCb.IsChecked = listBox.SelectedItems!.Contains(lbi);
                }
            }

            var selected = listBox
                .SelectedItems!.OfType<ListBoxItem>()
                .Select(item => item.Tag as string)
                .OfType<string>()
                .ToArray();
            string serialized = System.Text.Json.JsonSerializer.Serialize(
                selected,
                A2UiJsonSerializerContext.Default.StringArray
            );
            InputHelper.NotifyValueChanged(ctx, bindingPath, serialized, componentId);
        };

        return listBox;
    }

    /// <summary>
    /// Resolve the current selected values from the data model.
    /// The value is a DynamicStringList — it may be a JSON array literal or a bound path.
    /// </summary>
    private static HashSet<string> ResolveCurrentValues(DynamicValue? value, IRenderContext ctx)
    {
        if (value is null)
        {
            return [];
        }

        // If it's an array literal, extract string values
        if (value is DynamicValue.ArrayValue { Value: var arr })
        {
            var set = new HashSet<string>();
            foreach (var el in arr.EnumerateArray())
            {
                if (el.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    set.Add(el.GetString()!);
                }
            }
            return set;
        }

        // If bound to a path, resolve and try to parse as JSON array
        string? resolved = ctx.Resolve(value);
        if (resolved is null)
        {
            return [];
        }

        // Could be a JSON array string like ["a","b"] or a single value
        if (resolved.StartsWith('['))
        {
            try
            {
                var items = System.Text.Json.JsonSerializer.Deserialize(
                    resolved,
                    A2UiJsonSerializerContext.Default.StringArray
                );
                return items is not null ? [.. items] : [];
            }
            catch (System.Text.Json.JsonException)
            {
                return [resolved];
            }
        }

        return [resolved];
    }
}

/// <summary>A2UI "CheckBox" → CheckBox control.</summary>
public sealed class CheckBoxCatalogEntry : ICatalogEntry
{
    private sealed class CheckBoxBindingState(IRenderContext context, string? bindingPath, string componentId)
    {
        public IRenderContext Context { get; set; } = context;
        public string? BindingPath { get; set; } = bindingPath;
        public string ComponentId { get; set; } = componentId;
        public bool IsUpdating { get; set; }
    }

    /// <inheritdoc />
    public string ComponentType => "CheckBox";

    /// <inheritdoc />
    public Control Create(A2UiComponent component, DataModel dataModel, IRenderContext context)
    {
        var typed = (CheckBoxComponent)component;
        bool isChecked = context.Resolve(typed.Value) is "true";
        var cb = new CheckBox
        {
            Content = context.Resolve(component.Label),
            IsChecked = isChecked,
            Tag = new CheckBoxBindingState(context, typed.Value.GetBindingPath(), component.Id),
        };

        cb.IsCheckedChanged += (sender, _) =>
        {
            if (sender is CheckBox box && box.Tag is CheckBoxBindingState s)
            {
                if (s.IsUpdating)
                {
                    return;
                }

                InputHelper.NotifyValueChanged(s.Context, s.BindingPath, cb.IsChecked == true ? "true" : "false", s.ComponentId);
            }
        };

        return CheckHelper.ApplyChecks(cb, component, context);
    }

    /// <inheritdoc />
    public bool Update(Control existing, A2UiComponent component, DataModel dataModel, IRenderContext context)
    {
        var typed = (CheckBoxComponent)component;
        CheckBox? cb = CheckHelper.FindInner<CheckBox>(existing);
        if (cb is null)
        {
            return false;
        }

        if (cb.Tag is CheckBoxBindingState state)
        {
            state.Context = context;
            state.BindingPath = typed.Value.GetBindingPath();
            state.ComponentId = component.Id;

            if (!cb.IsFocused)
            {
                state.IsUpdating = true;
                cb.IsChecked = context.Resolve(typed.Value) is "true";
                state.IsUpdating = false;
            }
        }
        else if (!cb.IsFocused)
        {
            cb.IsChecked = context.Resolve(typed.Value) is "true";
        }

        cb.Content = context.Resolve(component.Label);

        if (existing is StackPanel wrapper)
        {
            CheckHelper.UpdateChecks(wrapper, component, context);
        }

        return true;
    }
}

/// <summary>A2UI "Slider" → Slider control.</summary>
public sealed class SliderCatalogEntry : ICatalogEntry
{
    private sealed class SliderBindingState(IRenderContext context, string? bindingPath, string componentId)
    {
        public IRenderContext Context { get; set; } = context;
        public string? BindingPath { get; set; } = bindingPath;
        public string ComponentId { get; set; } = componentId;
    }

    /// <inheritdoc />
    public string ComponentType => "Slider";

    /// <inheritdoc />
    public Control Create(A2UiComponent component, DataModel dataModel, IRenderContext context)
    {
        var typed = (SliderComponent)component;
        double.TryParse(context.Resolve(typed.Value), out double val);

        var slider = new Slider
        {
            Value = val,
            Tag = new SliderBindingState(context, typed.Value.GetBindingPath(), component.Id),
        };
        ApplySliderRangeAndTicks(slider, typed, context);

        // Fire on thumb drag complete, not on every pixel move
        slider.AddHandler(
            Thumb.DragCompletedEvent,
            (_, _) =>
            {
                if (slider.Tag is SliderBindingState s)
                {
                    InputHelper.NotifyValueChanged(
                        s.Context,
                        s.BindingPath,
                        slider.Value.ToString("G"),
                        s.ComponentId
                    );
                }
            },
            RoutingStrategies.Bubble
        );

        return CheckHelper.ApplyChecks(slider, component, context);
    }

    /// <inheritdoc />
    public bool Update(Control existing, A2UiComponent component, DataModel dataModel, IRenderContext context)
    {
        var typed = (SliderComponent)component;
        Slider? s = CheckHelper.FindInner<Slider>(existing);
        if (s is null)
        {
            return false;
        }

        if (s.Tag is SliderBindingState state)
        {
            state.Context = context;
            state.BindingPath = typed.Value.GetBindingPath();
            state.ComponentId = component.Id;
        }

        ApplySliderRangeAndTicks(s, typed, context);

        if (!s.IsFocused && double.TryParse(context.Resolve(typed.Value), out double val))
        {
            s.Value = val;
        }

        if (existing is StackPanel wrapper)
        {
            CheckHelper.UpdateChecks(wrapper, component, context);
        }

        return true;
    }

    private static void ApplySliderRangeAndTicks(Slider slider, SliderComponent typed, IRenderContext context)
    {
        double.TryParse(context.Resolve(typed.Min), out double min);
        double max = 100;
        if (context.Resolve(typed.Max) is { } maxStr)
        {
            double.TryParse(maxStr, out max);
        }

        slider.Minimum = min;
        slider.Maximum = max;

        double? steps = null;
        if (context.Resolve(typed.Steps) is { } stepsStr && double.TryParse(stepsStr, out double stepsVal))
        {
            steps = stepsVal;
        }

        if (steps is > 0 && max > min)
        {
            slider.TickFrequency = (max - min) / steps.Value;
            slider.IsSnapToTickEnabled = true;
        }
        else
        {
            slider.IsSnapToTickEnabled = false;
        }
    }
}
