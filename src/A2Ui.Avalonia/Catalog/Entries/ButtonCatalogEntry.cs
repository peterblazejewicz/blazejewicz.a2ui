using Avalonia.Controls;
using Blazejewicz.A2Ui.Core.Bindings;
using Blazejewicz.A2Ui.Core.Components;
using Blazejewicz.A2Ui.Core.Surfaces;

namespace Blazejewicz.A2Ui.Avalonia.Catalog.Entries;

/// <summary>A2UI "Button" → Avalonia Button with variant styling and action handling.</summary>
public sealed class ButtonCatalogEntry : ICatalogEntry
{
    private sealed class ButtonActionState(ButtonComponent component, IRenderContext context)
    {
        public ButtonComponent Component { get; set; } = component;
        public IRenderContext Context { get; set; } = context;
    }

    /// <inheritdoc />
    public string ComponentType => "Button";

    /// <inheritdoc />
    public Control Create(A2UiComponent component, DataModel dataModel, IRenderContext context)
    {
        var typed = (ButtonComponent)component;
        var btn = new Button
        {
            Content = component.Child is not null
                ? context.RenderChild(component.Child)
                : (object?)(context.Resolve(component.Text) ?? context.Resolve(component.Label) ?? string.Empty),
            Tag = new ButtonActionState(typed, context),
        };

        ApplyVariant(btn, typed.Variant);

        // Evaluate check conditions — disable button when any check fails.
        if (component.Checks is { Length: > 0 })
        {
            bool allPass = CheckHelper.AllChecksPassing(component, context);
            btn.IsEnabled = allPass;
            if (!allPass)
            {
                string? failedMessage = CheckHelper.FirstFailingMessage(component, context);
                if (failedMessage is not null)
                {
                    ToolTip.SetTip(btn, failedMessage);
                }
            }
        }

        btn.Click += (_, _) =>
        {
            if (btn.Tag is not ButtonActionState s)
            {
                return;
            }

            if (s.Component.Action?.Event is { } actionEvent)
            {
                object? payload = ResolveContext(actionEvent.Context, s.Context);
                string? userMessage = s.Context.Resolve(actionEvent.UserMessage);
                s.Context.FireUserAction(actionEvent.Name, payload, s.Component.Id, userMessage);
            }
            else if (s.Component.Action?.FunctionCall is { } fnCall)
            {
                s.Context.Resolve(DynamicValue.FromFunctionCall(fnCall));
            }
        };

        return btn;
    }

    /// <inheritdoc />
    public bool Update(Control existing, A2UiComponent component, DataModel dataModel, IRenderContext context)
    {
        var typed = (ButtonComponent)component;
        if (existing is not Button btn)
        {
            return false;
        }

        if (btn.Tag is ButtonActionState state)
        {
            state.Component = typed;
            state.Context = context;
        }
        else
        {
            btn.Tag = new ButtonActionState(typed, context);
        }

        if (component.Child is not null)
        {
            btn.Content = context.RenderChild(component.Child);
        }
        else
        {
            if (btn.Content is Control prevControl)
            {
                RenderContext.DetachFromParent(prevControl);
            }

            btn.Content = (object?)(context.Resolve(component.Text) ?? context.Resolve(component.Label) ?? string.Empty);
        }

        ApplyVariant(btn, typed.Variant);

        if (component.Checks is { Length: > 0 })
        {
            bool allPass = CheckHelper.AllChecksPassing(component, context);
            btn.IsEnabled = allPass;
            string? failedMessage = allPass ? null : CheckHelper.FirstFailingMessage(component, context);
            ToolTip.SetTip(btn, failedMessage);
        }
        else
        {
            btn.IsEnabled = true;
            ToolTip.SetTip(btn, null);
        }

        return true;
    }

    /// <summary>
    /// Resolve the action event context dictionary at invocation time.
    /// Each value is a DynamicValue that may reference the data model.
    /// </summary>
    private static Dictionary<string, string?>? ResolveContext(
        Dictionary<string, DynamicValue>? contextSpec,
        IRenderContext ctx
    )
    {
        if (contextSpec is null || contextSpec.Count == 0)
        {
            return null;
        }

        var resolved = new Dictionary<string, string?>(contextSpec.Count);
        foreach (var (key, dynVal) in contextSpec)
        {
            resolved[key] = ctx.Resolve(dynVal);
        }

        return resolved;
    }

    private static void ApplyVariant(Button btn, string? variant)
    {
        btn.Classes.Clear();
        if (variant is "primary")
        {
            btn.Classes.Add("accent");
        }
        else if (variant is "danger")
        {
            btn.Classes.Add("danger");
        }
        else if (variant is "borderless")
        {
            btn.Classes.Add("borderless");
        }
    }
}
