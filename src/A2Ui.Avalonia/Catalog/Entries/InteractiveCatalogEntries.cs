using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Blazejewicz.A2Ui.Core;
using Blazejewicz.A2Ui.Core.Components;
using Blazejewicz.A2Ui.Core.Surfaces;

namespace Blazejewicz.A2Ui.Avalonia.Catalog.Entries;

/// <summary>A2UI "List" → ScrollViewer + StackPanel (scrollable list).</summary>
public sealed class ListCatalogEntry : ICatalogEntry
{
    /// <inheritdoc />
    public string ComponentType => "List";

    /// <inheritdoc />
    public Control Create(A2UiComponent component, DataModel dataModel, IRenderContext context)
    {
        var typed = (ListComponent)component;
        var orientation = typed.Direction == "horizontal" ? Orientation.Horizontal : Orientation.Vertical;

        var panel = new StackPanel { Orientation = orientation, Spacing = 4 };
        PopulateChildren(panel, typed, orientation, context);

        return new ScrollViewer { Content = panel };
    }

    /// <inheritdoc />
    public bool Update(Control existing, A2UiComponent component, DataModel dataModel, IRenderContext context)
    {
        var typed = (ListComponent)component;
        if (existing is not ScrollViewer sv || sv.Content is not StackPanel panel)
        {
            return false;
        }

        var newOrientation = typed.Direction == "horizontal" ? Orientation.Horizontal : Orientation.Vertical;
        if (panel.Orientation != newOrientation)
        {
            return false;
        }

        panel.Children.Clear();
        PopulateChildren(panel, typed, newOrientation, context);

        return true;
    }

    private static void PopulateChildren(
        StackPanel panel,
        ListComponent typed,
        Orientation orientation,
        IRenderContext context
    )
    {
        bool isStyled = orientation == Orientation.Vertical && typed.ListStyle is "ordered" or "unordered";
        int index = 0;
        foreach (var child in context.RenderChildren(typed.Id))
        {
            if (isStyled)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                string markerText = typed.ListStyle == "ordered" ? $"{index + 1}." : "•";
                var marker = new TextBlock { Text = markerText, VerticalAlignment = VerticalAlignment.Top };
                row.Children.Add(marker);
                row.Children.Add(child);
                panel.Children.Add(row);
            }
            else
            {
                panel.Children.Add(child);
            }
            index++;
        }
    }
}

/// <summary>A2UI "Tabs" → TabControl.</summary>
public sealed class TabsCatalogEntry : ICatalogEntry
{
    /// <inheritdoc />
    public string ComponentType => "Tabs";

    /// <inheritdoc />
    public Control Create(A2UiComponent component, DataModel dataModel, IRenderContext context)
    {
        var typed = (TabsComponent)component;
        var tc = new TabControl();

        if (typed.Tabs is { } tabs)
        {
            foreach (var tab in tabs)
            {
                tc.Items.Add(
                    new TabItem { Header = context.Resolve(tab.Title), Content = context.RenderChild(tab.Child) }
                );
            }
        }

        return tc;
    }

    /// <inheritdoc />
    public bool Update(Control existing, A2UiComponent component, DataModel dataModel, IRenderContext context)
    {
        var typed = (TabsComponent)component;
        if (existing is not TabControl tc)
        {
            return false;
        }

        if (typed.Tabs is not { } tabs || tabs.Length != tc.Items.Count)
        {
            return false;
        }

        for (int i = 0; i < tabs.Length; i++)
        {
            if (tc.Items[i] is not TabItem tabItem)
            {
                return false;
            }

            tabItem.Header = context.Resolve(tabs[i].Title);
            tabItem.Content = context.RenderChild(tabs[i].Child);
        }

        return true;
    }
}

/// <summary>
/// A2UI "Modal" → Panel with trigger control + Avalonia Popup overlay.
/// Clicking the trigger opens a centered popup; close button or light-dismiss closes it.
/// </summary>
public sealed class ModalCatalogEntry : ICatalogEntry
{
    /// <inheritdoc />
    public string ComponentType => "Modal";

    /// <inheritdoc />
    public Control Create(A2UiComponent component, DataModel dataModel, IRenderContext context)
    {
        var typed = (ModalComponent)component;
        var container = new Panel();

        // Render the trigger control (shown permanently)
        Control? triggerControl = null;
        if (typed.Trigger is not null)
        {
            triggerControl = context.RenderChild(typed.Trigger);
            if (triggerControl is not null)
            {
                container.Children.Add(triggerControl);
            }
        }

        // Build the Popup overlay for the content
        if (typed.Content is not null)
        {
            var popup = new Popup
            {
                IsLightDismissEnabled = true,
                Placement = PlacementMode.Center,
                PlacementTarget = triggerControl ?? container,
            };

            // Close button positioned at the top-right of the popup content
            var closeBtn = new Button { Content = "×", HorizontalAlignment = HorizontalAlignment.Right };
            closeBtn.Classes.Add("ModalClose");

            // Stack: close button above the rendered content
            var contentColumn = new StackPanel { Spacing = 8 };
            contentColumn.Children.Add(closeBtn);

            bool isLazy = context is RenderContext rc && rc.Surface.ProtocolVersion == A2UiProtocol.Version10;
            Control? renderedContent = null;
            if (!isLazy)
            {
                renderedContent = context.RenderChild(typed.Content);
                if (renderedContent is not null)
                {
                    contentColumn.Children.Add(renderedContent);
                }
            }

            // White rounded card wrapping the content
            var border = new Border
            {
                Background = new SolidColorBrush(Colors.White),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                MinWidth = 300,
                MaxWidth = 600,
                Child = contentColumn,
            };
            popup.Child = border;

            bool IsInsidePopup(Visual visual)
            {
                if (ReferenceEquals(visual, border) || ReferenceEquals(visual, popup))
                {
                    return true;
                }

                foreach (var ancestor in visual.GetVisualAncestors())
                {
                    if (ReferenceEquals(ancestor, border) || ReferenceEquals(ancestor, popup))
                    {
                        return true;
                    }
                }

                return false;
            }

            bool CanOpen(Visual? source)
            {
                if (triggerControl?.IsEffectivelyEnabled == false)
                {
                    return false;
                }

                if (source is Control { IsEffectivelyEnabled: false })
                {
                    return false;
                }

                if (typed.Trigger is not null && !context.AreComponentChecksPassing(typed.Trigger))
                {
                    return false;
                }

                return true;
            }

            void OpenModal()
            {
                if (renderedContent is null)
                {
                    renderedContent = context.RenderChild(typed.Content);
                    if (renderedContent is not null)
                    {
                        contentColumn.Children.Add(renderedContent);
                    }
                }
                else if (!contentColumn.Children.Contains(renderedContent))
                {
                    contentColumn.Children.Add(renderedContent);
                }

                popup.IsOpen = true;
            }

            // Wire trigger Button.ClickEvent bubble handler
            container.AddHandler(
                Button.ClickEvent,
                (sender, e) =>
                {
                    if (e.Source is Visual visual && IsInsidePopup(visual))
                    {
                        return;
                    }

                    if (!CanOpen(e.Source as Visual))
                    {
                        return;
                    }

                    e.Handled = true;
                    OpenModal();
                },
                RoutingStrategies.Bubble
            );

            // Close button closes popup
            closeBtn.Click += (_, e) =>
            {
                popup.IsOpen = false;
                e.Handled = true;
            };

            // Fallback Tapped + KeyDown if trigger contains no Button
            bool hasButton = HasButton(triggerControl);
            if (!hasButton && triggerControl is not null)
            {
                triggerControl.Focusable = true;

                triggerControl.Tapped += (sender, e) =>
                {
                    if (e.Source is Visual visual && IsInsidePopup(visual))
                    {
                        return;
                    }

                    if (!CanOpen(e.Source as Visual))
                    {
                        return;
                    }

                    e.Handled = true;
                    OpenModal();
                };

                triggerControl.KeyDown += (sender, e) =>
                {
                    if (e.Key is Key.Space or Key.Enter)
                    {
                        if (e.Source is Visual visual && IsInsidePopup(visual))
                        {
                            return;
                        }

                        if (!CanOpen(e.Source as Visual))
                        {
                            return;
                        }

                        e.Handled = true;
                        OpenModal();
                    }
                };
            }

            // Center popup on the top-level window once attached to visual tree
            container.AttachedToVisualTree += (_, _) =>
            {
                var topLevel = TopLevel.GetTopLevel(container);
                if (topLevel is Control topControl)
                {
                    popup.PlacementTarget = topControl;
                }
            };

            // Clean up popup on detach
            container.DetachedFromVisualTree += (_, _) => popup.IsOpen = false;

            container.Children.Add(popup);
        }

        return container;
    }

    private static bool HasButton(Control? control)
    {
        if (control is null)
        {
            return false;
        }

        if (control is Button)
        {
            return true;
        }

        if (control is Panel p)
        {
            foreach (var child in p.Children)
            {
                if (HasButton(child))
                {
                    return true;
                }
            }
        }
        else if (control is ContentControl cc && cc.Content is Control ccChild)
        {
            return HasButton(ccChild);
        }
        else if (control is Decorator d && d.Child is not null)
        {
            return HasButton(d.Child);
        }

        return false;
    }

    /// <inheritdoc />
    public bool Update(Control existing, A2UiComponent component, DataModel dataModel, IRenderContext context) => false;
}
