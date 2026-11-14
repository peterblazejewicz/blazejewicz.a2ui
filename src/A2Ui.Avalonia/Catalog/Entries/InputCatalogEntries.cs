using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Blazejewicz.A2Ui.Core;
using Blazejewicz.A2Ui.Core.Bindings;
using Blazejewicz.A2Ui.Core.Choices;
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
        public bool IsNotifying { get; set; }
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

                s.IsNotifying = true;
                try
                {
                    InputHelper.NotifyValueChanged(s.Context, s.BindingPath, box.Text, s.ComponentId);
                }
                finally
                {
                    s.IsNotifying = false;
                }
            }
        };

        return CheckHelper.ApplyChecks(tb, component, context);
    }

    /// <inheritdoc />
    public bool Update(Control existing, A2UiComponent component, DataModel dataModel, IRenderContext context)
    {
        if (existing is not StackPanel && component.Checks is { Length: > 0 })
        {
            return false;
        }

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

            if (!tb.IsFocused && !state.IsNotifying)
            {
                state.IsUpdating = true;
                try
                {
                    tb.Text = context.Resolve(typed.Value) ?? string.Empty;
                }
                finally
                {
                    state.IsUpdating = false;
                }
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
        public bool IsUpdating { get; set; }
        public bool IsNotifying { get; set; }
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
            if (ReferenceEquals(existing, panel) && component.Checks is { Length: > 0 })
            {
                return false;
            }

            var datePicker = panel.Children.OfType<CalendarDatePicker>().FirstOrDefault();
            var timePicker = panel.Children.OfType<TimePicker>().FirstOrDefault();
            if (datePicker is null || timePicker is null)
            {
                return false;
            }

            var dateState = datePicker.Tag as DateTimeBindingState;
            if (dateState is not null)
            {
                dateState.Context = context;
                dateState.BindingPath = typed.Value.GetBindingPath();
                dateState.ComponentId = component.Id;
            }

            if (
                !datePicker.IsFocused
                && !timePicker.IsFocused
                && dateState?.IsNotifying != true
                && raw is not null
                && DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            )
            {
                if (dateState is not null)
                {
                    dateState.IsUpdating = true;
                }

                try
                {
                    datePicker.SelectedDate = dt.Date;
                    timePicker.SelectedTime = dt.TimeOfDay;
                }
                finally
                {
                    if (dateState is not null)
                    {
                        dateState.IsUpdating = false;
                    }
                }
            }

            datePicker.PlaceholderText = context.Resolve(component.Label) ?? "Select date";

            if (existing is StackPanel compositeWrapper && !ReferenceEquals(compositeWrapper, panel))
            {
                CheckHelper.UpdateChecks(compositeWrapper, component, context);
            }

            return true;
        }

        if (existing is not StackPanel && component.Checks is { Length: > 0 })
        {
            return false;
        }

        // Time only
        if (CheckHelper.FindInner<TimePicker>(existing) is { } tp)
        {
            var tpState = tp.Tag as DateTimeBindingState;
            if (tpState is not null)
            {
                tpState.Context = context;
                tpState.BindingPath = typed.Value.GetBindingPath();
                tpState.ComponentId = component.Id;
            }

            if (
                !tp.IsFocused
                && tpState?.IsNotifying != true
                && raw is not null
                && TimeSpan.TryParse(raw, CultureInfo.InvariantCulture, out var ts)
            )
            {
                if (tpState is not null)
                {
                    tpState.IsUpdating = true;
                }

                try
                {
                    tp.SelectedTime = ts;
                }
                finally
                {
                    if (tpState is not null)
                    {
                        tpState.IsUpdating = false;
                    }
                }
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

        var pickerState = picker.Tag as DateTimeBindingState;
        if (pickerState is not null)
        {
            pickerState.Context = context;
            pickerState.BindingPath = typed.Value.GetBindingPath();
            pickerState.ComponentId = component.Id;
        }

        if (!picker.IsFocused && pickerState?.IsNotifying != true)
        {
            if (pickerState is not null)
            {
                pickerState.IsUpdating = true;
            }

            try
            {
                picker.SelectedDate =
                    raw is not null && DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                        ? date.ToDateTime(TimeOnly.MinValue)
                        : null;
            }
            finally
            {
                if (pickerState is not null)
                {
                    pickerState.IsUpdating = false;
                }
            }
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
            if (picker.Tag is DateTimeBindingState s && !s.IsUpdating)
            {
                s.IsNotifying = true;
                try
                {
                    string? isoDate = picker.SelectedDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    InputHelper.NotifyValueChanged(s.Context, s.BindingPath, isoDate, s.ComponentId);
                }
                finally
                {
                    s.IsNotifying = false;
                }
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

        if (raw is not null && DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            datePicker.SelectedDate = dt.Date;
            timePicker.SelectedTime = dt.TimeOfDay;
        }

        void NotifyCombined()
        {
            if (datePicker.Tag is not DateTimeBindingState s || s.IsUpdating)
            {
                return;
            }

            s.IsNotifying = true;
            try
            {
                var d = datePicker.SelectedDate;
                var t = timePicker.SelectedTime;
                if (d is null && t is null)
                {
                    InputHelper.NotifyValueChanged(s.Context, s.BindingPath, null, s.ComponentId);
                    return;
                }

                var dateVal = d?.Date ?? DateTime.Today;
                var timeVal = t ?? TimeSpan.Zero;
                string iso = new DateTime(dateVal.Ticks + timeVal.Ticks).ToString(
                    "yyyy-MM-ddTHH:mm:ss",
                    CultureInfo.InvariantCulture
                );
                InputHelper.NotifyValueChanged(s.Context, s.BindingPath, iso, s.ComponentId);
            }
            finally
            {
                s.IsNotifying = false;
            }
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

        if (raw is not null && TimeSpan.TryParse(raw, CultureInfo.InvariantCulture, out var ts))
        {
            timePicker.SelectedTime = ts;
        }

        timePicker.SelectedTimeChanged += (_, _) =>
        {
            if (timePicker.Tag is DateTimeBindingState s && !s.IsUpdating)
            {
                s.IsNotifying = true;
                try
                {
                    string? isoTime = timePicker.SelectedTime?.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
                    InputHelper.NotifyValueChanged(s.Context, s.BindingPath, isoTime, s.ComponentId);
                }
                finally
                {
                    s.IsNotifying = false;
                }
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
        public (string? Label, string Value)[] ResolvedOptions { get; set; } = [];
        public bool IsUpdating { get; set; }
        public bool IsNotifying { get; set; }
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
        HashSet<string> currentValues = ResolveCurrentValues(typed.Value, context);

        Control control = isMultiple
            ? CreateMultipleSelection(typed, context, bindingPath, componentId, currentValues)
            : CreateMutuallyExclusive(typed, context, bindingPath, componentId, currentValues);

        return CheckHelper.ApplyChecks(control, component, context);
    }

    /// <inheritdoc />
    public bool Update(Control existing, A2UiComponent component, DataModel dataModel, IRenderContext context)
    {
        if (existing is not StackPanel && component.Checks is { Length: > 0 })
        {
            return false;
        }

        var typed = (ChoicePickerComponent)component;
        bool isMultiple = typed.Variant is "multipleSelection";
        (string? Label, string Value)[] desiredOptions = ResolveOptions(typed.Options, context);
        HashSet<string> currentValues = ResolveCurrentValues(typed.Value, context);

        bool updated = (isMultiple, typed.DisplayStyle is "chips", typed.Filterable is true) switch
        {
            (false, _, true) => UpdateAutoCompleteBox(existing, typed, context, desiredOptions, currentValues),
            (false, _, false) => UpdateComboBox(existing, typed, context, desiredOptions, currentValues),
            (true, true, _) => UpdateChipsPanel(existing, typed, context, desiredOptions, currentValues),
            (true, false, _) => UpdateListBox(existing, typed, context, desiredOptions, currentValues),
        };

        if (!updated)
        {
            return false;
        }

        if (existing is StackPanel wrapper)
        {
            CheckHelper.UpdateChecks(wrapper, component, context);
        }

        return true;
    }

    private static bool UpdateComboBox(
        Control existing,
        ChoicePickerComponent typed,
        IRenderContext context,
        (string? Label, string Value)[] desiredOptions,
        HashSet<string> currentValues
    ) =>
        UpdateChoicePickerControlCore<ComboBox>(
            existing,
            typed,
            context,
            desiredOptions,
            updateMetadata: combo => combo.PlaceholderText = context.Resolve(typed.Label),
            applyGuardedUpdate: combo =>
            {
                if (!ComboItemsMatch(combo, desiredOptions))
                {
                    combo.Items.Clear();
                    foreach ((string? label, string val) in desiredOptions)
                    {
                        combo.Items.Add(new ComboBoxItem { Content = label, Tag = val });
                    }
                }

                int selectedIndex = -1;
                for (int i = 0; i < combo.Items.Count; i++)
                {
                    if (combo.Items[i] is ComboBoxItem item && item.Tag is string val && currentValues.Contains(val))
                    {
                        selectedIndex = i;
                        break;
                    }
                }

                if (combo.SelectedIndex != selectedIndex)
                {
                    combo.SelectedIndex = selectedIndex;
                }
            }
        );

    private static bool UpdateAutoCompleteBox(
        Control existing,
        ChoicePickerComponent typed,
        IRenderContext context,
        (string? Label, string Value)[] desiredOptions,
        HashSet<string> currentValues
    ) =>
        UpdateChoicePickerControlCore<AutoCompleteBox>(
            existing,
            typed,
            context,
            desiredOptions,
            updateMetadata: autoComplete => autoComplete.PlaceholderText = context.Resolve(typed.Label),
            applyGuardedUpdate: autoComplete =>
            {
                if (!AutoCompleteItemsMatch(autoComplete, desiredOptions))
                {
                    autoComplete.ItemsSource = desiredOptions.Select(o => o.Label ?? string.Empty).ToArray();
                }

                if (!autoComplete.IsKeyboardFocusWithin)
                {
                    string? desiredText = null;
                    if (currentValues.Count > 0)
                    {
                        (string? label, string? matchVal) = desiredOptions.FirstOrDefault(
                            o => currentValues.Contains(o.Value)
                        );
                        if (matchVal is not null)
                        {
                            desiredText = label ?? string.Empty;
                        }
                    }

                    if (!string.Equals(autoComplete.Text, desiredText, StringComparison.Ordinal))
                    {
                        autoComplete.Text = desiredText;
                    }
                }
            }
        );

    private static bool UpdateChipsPanel(
        Control existing,
        ChoicePickerComponent typed,
        IRenderContext context,
        (string? Label, string Value)[] desiredOptions,
        HashSet<string> currentValues
    ) =>
        UpdateChoicePickerControlCore<WrapPanel>(
            existing,
            typed,
            context,
            desiredOptions,
            updateMetadata: null,
            applyGuardedUpdate: panel =>
            {
                if (!ChipItemsMatch(panel, desiredOptions))
                {
                    panel.Children.Clear();
                    foreach ((string? label, string val) in desiredOptions)
                    {
                        var toggle = new ToggleButton
                        {
                            Content = label,
                            Tag = val,
                            IsChecked = currentValues.Contains(val),
                            Margin = new Thickness(2),
                        };
                        toggle.Classes.Add("chip");
                        panel.Children.Add(toggle);
                    }
                }
                else
                {
                    foreach (ToggleButton toggle in panel.Children.OfType<ToggleButton>())
                    {
                        bool shouldCheck = toggle.Tag is string val && currentValues.Contains(val);
                        if (toggle.IsChecked != shouldCheck)
                        {
                            toggle.IsChecked = shouldCheck;
                        }
                    }
                }
            }
        );

    private static bool UpdateListBox(
        Control existing,
        ChoicePickerComponent typed,
        IRenderContext context,
        (string? Label, string Value)[] desiredOptions,
        HashSet<string> currentValues
    ) =>
        UpdateChoicePickerControlCore<ListBox>(
            existing,
            typed,
            context,
            desiredOptions,
            updateMetadata: null,
            applyGuardedUpdate: listBox =>
            {
                if (!ListBoxItemsMatch(listBox, desiredOptions))
                {
                    listBox.Items.Clear();
                    foreach ((string? label, string val) in desiredOptions)
                    {
                        var item = new ListBoxItem
                        {
                            Content = new CheckBox
                            {
                                Content = label,
                                IsChecked = currentValues.Contains(val),
                            },
                            Tag = val,
                        };
                        listBox.Items.Add(item);
                    }
                }

                foreach (ListBoxItem lbi in listBox.Items.OfType<ListBoxItem>())
                {
                    bool shouldSelect = lbi.Tag is string val && currentValues.Contains(val);
                    bool isSelected = listBox.SelectedItems!.Contains(lbi);
                    if (shouldSelect && !isSelected)
                    {
                        listBox.SelectedItems.Add(lbi);
                    }
                    else if (!shouldSelect && isSelected)
                    {
                        listBox.SelectedItems.Remove(lbi);
                    }

                    if (lbi.Content is CheckBox cb && cb.IsChecked != shouldSelect)
                    {
                        cb.IsChecked = shouldSelect;
                    }
                }
            }
        );

    private static bool UpdateChoicePickerControlCore<TControl>(
        Control existing,
        ChoicePickerComponent typed,
        IRenderContext context,
        (string? Label, string Value)[] desiredOptions,
        Action<TControl>? updateMetadata,
        Action<TControl> applyGuardedUpdate
    )
        where TControl : Control
    {
        TControl? control = CheckHelper.FindInner<TControl>(existing);
        if (control is null)
        {
            return false;
        }

        ChoicePickerBindingState? state = control.Tag as ChoicePickerBindingState;
        if (state is not null)
        {
            state.Context = context;
            state.BindingPath = typed.Value.GetBindingPath();
            state.ComponentId = typed.Id;
            state.ResolvedOptions = desiredOptions;
        }

        updateMetadata?.Invoke(control);

        // Never mutate picker items or selection while SelectionModel<T>.CommitOperation
        // is still on the call stack inside SelectionChanged -> DataModelChanged -> Refresh().
        if (state?.IsNotifying != true)
        {
            if (state is not null)
            {
                state.IsUpdating = true;
            }

            try
            {
                applyGuardedUpdate(control);
            }
            finally
            {
                if (state is not null)
                {
                    state.IsUpdating = false;
                }
            }
        }

        return true;
    }

    private static bool ComboItemsMatch(ComboBox combo, (string? Label, string Value)[] desiredOptions)
    {
        if (combo.Items.Count != desiredOptions.Length)
        {
            return false;
        }

        for (int i = 0; i < desiredOptions.Length; i++)
        {
            if (
                combo.Items[i] is not ComboBoxItem item
                || !string.Equals(item.Content as string, desiredOptions[i].Label, StringComparison.Ordinal)
                || !string.Equals(item.Tag as string, desiredOptions[i].Value, StringComparison.Ordinal)
            )
            {
                return false;
            }
        }

        return true;
    }

    private static bool AutoCompleteItemsMatch(
        AutoCompleteBox autoComplete,
        (string? Label, string Value)[] desiredOptions
    )
    {
        if (autoComplete.ItemsSource is not string[] existingLabels || existingLabels.Length != desiredOptions.Length)
        {
            return false;
        }

        for (int i = 0; i < desiredOptions.Length; i++)
        {
            if (!string.Equals(existingLabels[i], desiredOptions[i].Label ?? string.Empty, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ChipItemsMatch(WrapPanel panel, (string? Label, string Value)[] desiredOptions)
    {
        if (panel.Children.Count != desiredOptions.Length)
        {
            return false;
        }

        for (int i = 0; i < desiredOptions.Length; i++)
        {
            if (
                panel.Children[i] is not ToggleButton toggle
                || !string.Equals(toggle.Content as string, desiredOptions[i].Label, StringComparison.Ordinal)
                || !string.Equals(toggle.Tag as string, desiredOptions[i].Value, StringComparison.Ordinal)
            )
            {
                return false;
            }
        }

        return true;
    }

    private static bool ListBoxItemsMatch(ListBox listBox, (string? Label, string Value)[] desiredOptions)
    {
        if (listBox.Items.Count != desiredOptions.Length)
        {
            return false;
        }

        for (int i = 0; i < desiredOptions.Length; i++)
        {
            if (
                listBox.Items[i] is not ListBoxItem item
                || !string.Equals(item.Tag as string, desiredOptions[i].Value, StringComparison.Ordinal)
                || item.Content is not CheckBox cb
                || !string.Equals(cb.Content as string, desiredOptions[i].Label, StringComparison.Ordinal)
            )
            {
                return false;
            }
        }

        return true;
    }

    private static (string? Label, string Value)[] ResolveOptions(ChoiceOption[]? options, IRenderContext ctx) =>
        options is { } opts ? [.. opts.Select(o => (ctx.Resolve(o.Label), o.Value))] : [];

    private static void NotifySelectionChanged(ChoicePickerBindingState s, string[] selected)
    {
        if (s.IsUpdating)
        {
            return;
        }

        s.IsNotifying = true;
        try
        {
            JsonElement jsonArray = JsonSerializer.SerializeToElement(
                selected,
                A2UiJsonSerializerContext.Default.StringArray
            );
            string serialized = JsonSerializer.Serialize(
                selected,
                A2UiJsonSerializerContext.Default.StringArray
            );
            InputHelper.NotifyValueChanged(s.Context, s.BindingPath, jsonArray, serialized, s.ComponentId);
        }
        finally
        {
            s.IsNotifying = false;
        }
    }

    private static Control CreateMutuallyExclusive(
        ChoicePickerComponent c,
        IRenderContext ctx,
        string? bindingPath,
        string componentId,
        HashSet<string> currentValues
    )
    {
        (string? Label, string Value)[] resolvedOptions = ResolveOptions(c.Options, ctx);
        var state = new ChoicePickerBindingState(ctx, bindingPath, componentId)
        {
            ResolvedOptions = resolvedOptions,
        };

        if (c.Filterable is true)
        {
            var autoComplete = new AutoCompleteBox
            {
                PlaceholderText = ctx.Resolve(c.Label),
                FilterMode = AutoCompleteFilterMode.ContainsOrdinal,
                Tag = state,
                ItemsSource = resolvedOptions.Select(o => o.Label ?? string.Empty).ToArray(),
            };

            // Set initial selected text
            if (currentValues.Count > 0)
            {
                (string? label, string? matchVal) = resolvedOptions.FirstOrDefault(
                    o => currentValues.Contains(o.Value)
                );
                if (matchVal is not null)
                {
                    autoComplete.Text = label ?? string.Empty;
                }
            }

            autoComplete.SelectionChanged += (_, _) =>
            {
                if (autoComplete.Tag is ChoicePickerBindingState s && !s.IsUpdating)
                {
                    string? selectedLabel = autoComplete.SelectedItem as string;
                    string? selectedValue = s
                        .ResolvedOptions.FirstOrDefault(o => (o.Label ?? string.Empty) == selectedLabel)
                        .Value;
                    if (selectedValue is not null)
                    {
                        NotifySelectionChanged(s, [selectedValue]);
                    }
                }
            };

            return autoComplete;
        }

        var combo = new ComboBox
        {
            PlaceholderText = ctx.Resolve(c.Label),
            Tag = state,
        };

        int selectedIndex = -1;
        for (int i = 0; i < resolvedOptions.Length; i++)
        {
            combo.Items.Add(new ComboBoxItem { Content = resolvedOptions[i].Label, Tag = resolvedOptions[i].Value });
            if (currentValues.Contains(resolvedOptions[i].Value))
            {
                selectedIndex = i;
            }
        }

        if (selectedIndex >= 0)
        {
            combo.SelectedIndex = selectedIndex;
        }

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.Tag is ChoicePickerBindingState s && !s.IsUpdating)
            {
                string? selectedValue = (combo.SelectedItem as ComboBoxItem)?.Tag as string;
                string[] selected = selectedValue is not null ? [selectedValue] : [];
                NotifySelectionChanged(s, selected);
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
        (string? Label, string Value)[] resolvedOptions = ResolveOptions(c.Options, ctx);
        var state = new ChoicePickerBindingState(ctx, bindingPath, componentId)
        {
            ResolvedOptions = resolvedOptions,
        };

        bool useChips = c.DisplayStyle is "chips";

        if (useChips)
        {
            var panel = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Tag = state,
            };

            foreach ((string? label, string val) in resolvedOptions)
            {
                var toggle = new ToggleButton
                {
                    Content = label,
                    Tag = val,
                    IsChecked = currentValues.Contains(val),
                    Margin = new Thickness(2),
                };
                toggle.Classes.Add("chip");
                panel.Children.Add(toggle);
            }

            // Fire on any toggle change
            panel.AddHandler(
                ToggleButton.IsCheckedChangedEvent,
                (_, _) =>
                {
                    if (panel.Tag is ChoicePickerBindingState s && !s.IsUpdating)
                    {
                        string[] selected =
                        [
                            .. panel
                                .Children.OfType<ToggleButton>()
                                .Where(t => t.IsChecked == true)
                                .Select(t => t.Tag as string)
                                .OfType<string>(),
                        ];
                        NotifySelectionChanged(s, selected);
                    }
                },
                RoutingStrategies.Bubble
            );

            return panel;
        }

        // Default: checkbox-style ListBox
        var listBox = new ListBox
        {
            SelectionMode = SelectionMode.Multiple | SelectionMode.Toggle,
            Tag = state,
        };

        foreach ((string? label, string val) in resolvedOptions)
        {
            var item = new ListBoxItem
            {
                Content = new CheckBox
                {
                    Content = label,
                    IsChecked = currentValues.Contains(val),
                },
                Tag = val,
            };
            listBox.Items.Add(item);
        }

        // Apply initial selection to ListBox model BEFORE subscribing to SelectionChanged.
        // Avalonia raises SelectionChanged synchronously on every SelectedItems.Add call;
        // subscribing first would fire a phantom valueChanged per pre-selected option on the
        // initial render, echoing the agent's own state back to it before any user interaction.
        foreach (ListBoxItem item in listBox.Items.OfType<ListBoxItem>())
        {
            if (item.Content is CheckBox cb && cb.IsChecked == true)
            {
                listBox.SelectedItems!.Add(item);
            }
        }

        // SelectionChanged is the source of truth — sync CheckBox visuals from it
        listBox.SelectionChanged += (_, _) =>
        {
            if (listBox.Tag is not ChoicePickerBindingState s || s.IsUpdating)
            {
                return;
            }

            s.IsUpdating = true;
            try
            {
                foreach (ListBoxItem lbi in listBox.Items.OfType<ListBoxItem>())
                {
                    if (lbi.Content is CheckBox innerCb)
                    {
                        innerCb.IsChecked = listBox.SelectedItems!.Contains(lbi);
                    }
                }
            }
            finally
            {
                s.IsUpdating = false;
            }

            string[] selected =
            [
                .. listBox
                    .SelectedItems!.OfType<ListBoxItem>()
                    .Select(item => item.Tag as string)
                    .OfType<string>(),
            ];
            NotifySelectionChanged(s, selected);
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
            foreach (JsonElement el in arr.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String)
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
                string[]? items = JsonSerializer.Deserialize(
                    resolved,
                    A2UiJsonSerializerContext.Default.StringArray
                );
                return items is not null ? [.. items] : [];
            }
            catch (JsonException)
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
        public bool IsNotifying { get; set; }
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

                s.IsNotifying = true;
                try
                {
                    bool checkedVal = box.IsChecked == true;
                    JsonElement jsonBool = JsonSerializer.SerializeToElement(
                        checkedVal,
                        A2UiJsonSerializerContext.Default.Boolean
                    );
                    InputHelper.NotifyValueChanged(
                        s.Context,
                        s.BindingPath,
                        jsonBool,
                        checkedVal ? "true" : "false",
                        s.ComponentId
                    );
                }
                finally
                {
                    s.IsNotifying = false;
                }
            }
        };

        return CheckHelper.ApplyChecks(cb, component, context);
    }

    /// <inheritdoc />
    public bool Update(Control existing, A2UiComponent component, DataModel dataModel, IRenderContext context)
    {
        if (existing is not StackPanel && component.Checks is { Length: > 0 })
        {
            return false;
        }

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

            if (!cb.IsFocused && !state.IsNotifying)
            {
                state.IsUpdating = true;
                try
                {
                    cb.IsChecked = context.Resolve(typed.Value) is "true";
                }
                finally
                {
                    state.IsUpdating = false;
                }
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
        public bool IsUpdating { get; set; }
        public bool IsNotifying { get; set; }
    }

    /// <inheritdoc />
    public string ComponentType => "Slider";

    /// <inheritdoc />
    public Control Create(A2UiComponent component, DataModel dataModel, IRenderContext context)
    {
        var typed = (SliderComponent)component;
        double.TryParse(
            context.Resolve(typed.Value),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out double val
        );

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
                if (slider.Tag is SliderBindingState s && !s.IsUpdating)
                {
                    s.IsNotifying = true;
                    try
                    {
                        JsonElement jsonNum = JsonSerializer.SerializeToElement(
                            slider.Value,
                            A2UiJsonSerializerContext.Default.Double
                        );
                        InputHelper.NotifyValueChanged(
                            s.Context,
                            s.BindingPath,
                            jsonNum,
                            slider.Value.ToString("G", CultureInfo.InvariantCulture),
                            s.ComponentId
                        );
                    }
                    finally
                    {
                        s.IsNotifying = false;
                    }
                }
            },
            RoutingStrategies.Bubble
        );

        return CheckHelper.ApplyChecks(slider, component, context);
    }

    /// <inheritdoc />
    public bool Update(Control existing, A2UiComponent component, DataModel dataModel, IRenderContext context)
    {
        if (existing is not StackPanel && component.Checks is { Length: > 0 })
        {
            return false;
        }

        var typed = (SliderComponent)component;
        Slider? s = CheckHelper.FindInner<Slider>(existing);
        if (s is null)
        {
            return false;
        }

        SliderBindingState? state = s.Tag as SliderBindingState;
        if (state is not null)
        {
            state.Context = context;
            state.BindingPath = typed.Value.GetBindingPath();
            state.ComponentId = component.Id;
        }

        if (state?.IsNotifying != true)
        {
            if (state is not null)
            {
                state.IsUpdating = true;
            }

            try
            {
                ApplySliderRangeAndTicks(s, typed, context);

                if (
                    !s.IsFocused
                    && double.TryParse(
                        context.Resolve(typed.Value),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out double val
                    )
                )
                {
                    s.Value = val;
                }
            }
            finally
            {
                if (state is not null)
                {
                    state.IsUpdating = false;
                }
            }
        }

        if (existing is StackPanel wrapper)
        {
            CheckHelper.UpdateChecks(wrapper, component, context);
        }

        return true;
    }

    private static void ApplySliderRangeAndTicks(Slider slider, SliderComponent typed, IRenderContext context)
    {
        double.TryParse(
            context.Resolve(typed.Min),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out double min
        );
        double max = 100;
        if (context.Resolve(typed.Max) is { } maxStr)
        {
            double.TryParse(maxStr, NumberStyles.Float, CultureInfo.InvariantCulture, out max);
        }

        slider.Minimum = min;
        slider.Maximum = max;

        double? steps = null;
        if (
            context.Resolve(typed.Steps) is { } stepsStr
            && double.TryParse(stepsStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double stepsVal)
        )
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
