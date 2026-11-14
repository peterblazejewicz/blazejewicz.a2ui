using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Blazejewicz.A2Ui.AgUi.Protocol.Transport;
using Blazejewicz.A2Ui.Avalonia.Catalog;
using Blazejewicz.A2Ui.Avalonia.Controls;
using Blazejewicz.A2Ui.Core;
using Blazejewicz.A2Ui.Core.Bindings;
using Blazejewicz.A2Ui.Core.Children;
using Blazejewicz.A2Ui.Core.Components;
using Blazejewicz.A2Ui.Core.Functions;
using Blazejewicz.A2Ui.Core.Messages;
using Blazejewicz.A2Ui.Core.Rpc;
using Blazejewicz.A2Ui.Core.Surfaces;
using Blazejewicz.A2Ui.Core.Tabs;
using Xunit;

namespace Blazejewicz.A2Ui.Avalonia.Tests;

public sealed class V10AvaloniaPhaseTests
{
    [AvaloniaFact]
    public void CatalogRegistry_MultiCatalogResolution()
    {
        var primaryCatalog = CatalogRegistry.CreateDefault();

        var customCatalog = new CatalogRegistry
        {
            CatalogId = "https://custom.org/catalog.json",
            ProtocolVersion = "1.0",
        };

        customCatalog.Register("CustomCard", (comp, dm, ctx) => new Border());

        primaryCatalog.RegisterCatalog("https://custom.org/catalog.json", customCatalog);

        // 1. Resolve with explicit component catalogId
        bool resolved = primaryCatalog.TryResolveEntry(
            componentCatalogId: "https://custom.org/catalog.json",
            surfaceDefaultCatalogId: null,
            componentType: "CustomCard",
            out var entry
        );

        Assert.True(resolved);
        Assert.NotNull(entry);

        // 2. Resolve via surface default catalogId
        bool resolvedDefault = primaryCatalog.TryResolveEntry(
            componentCatalogId: null,
            surfaceDefaultCatalogId: "https://custom.org/catalog.json",
            componentType: "CustomCard",
            out var entryDefault
        );

        Assert.True(resolvedDefault);
        Assert.NotNull(entryDefault);

        // 3. Resolve standard basic catalog component through primary fallback
        bool resolvedBasic = primaryCatalog.TryResolveEntry(
            componentCatalogId: null,
            surfaceDefaultCatalogId: null,
            componentType: "Text",
            out var entryBasic
        );

        Assert.True(resolvedBasic);
        Assert.NotNull(entryBasic);
    }

    [AvaloniaFact]
    public void CatalogRegistry_ResolveEntry_EnforcesSupportedCatalogIds()
    {
        var catalog = new CatalogRegistry();

        var surface = new Surface("s1", "https://a2ui.org/catalogs/basic/v1/catalog.json")
        {
            SupportedCatalogIds = ["https://a2ui.org/catalogs/basic/v1/catalog.json"],
        };

        var comp = new TextComponent { Id = "txt1", CatalogId = "https://unauthorized.org/catalog.json" };

        var ex = Assert.Throws<A2UiCatalogException>(() => catalog.ResolveEntry(surface, comp));
        Assert.Contains("not supported", ex.Message);
    }

    [AvaloniaFact]
    public void CatalogRegistry_ResolveEntry_EnforcesProtocolVersionMatching()
    {
        var primaryCatalog = new CatalogRegistry();

        var legacyCatalog = new CatalogRegistry
        {
            CatalogId = "https://legacy.org/catalog.json",
            ProtocolVersion = "0.9",
        };
        legacyCatalog.Register("LegacyComp", (c, d, ctx) => new TextBlock());

        primaryCatalog.RegisterCatalog("https://legacy.org/catalog.json", legacyCatalog, protocolVersion: "0.9");

        var surface = new Surface("s1", "https://legacy.org/catalog.json");

        var comp = new GenericComponent
        {
            Id = "c1",
            ComponentType = "LegacyComp",
            CatalogId = "https://legacy.org/catalog.json",
        };

        var ex = Assert.Throws<A2UiCatalogException>(() => primaryCatalog.ResolveEntry(surface, comp));
        Assert.Contains("Protocol version mismatch", ex.Message);
    }

    [AvaloniaFact]
    public void AtIndex_OutsideCollectionScope_ThrowsEvaluationException()
    {
        var surface = new Surface("s1");
        var renderer = new A2UiRenderer(CatalogRegistry.CreateDefault());

        var comp = new TextComponent
        {
            Id = "root",
            Text = DynamicValue.FromFunctionCall(new FunctionCallValue { Call = "@index" }),
        };
        surface.UpdateComponents([comp]);

        Assert.Throws<A2UiEvaluationException>(() => renderer.Render(surface));
    }

    [AvaloniaFact]
    public void AtIndex_InsideListTemplate_ExpandsWithSequentialIndices()
    {
        var surface = new Surface("s1");
        surface.DataModel.SetSnapshot(
            JsonSerializer.SerializeToElement(new { items = new[] { "item_0", "item_1", "item_2" } })
        );

        var listComp = new ListComponent { Id = "root", Children = ChildList.FromTemplate("item_template", "/items") };

        var itemTemplateComp = new TextComponent
        {
            Id = "item_template",
            Text = DynamicValue.FromFunctionCall(
                new FunctionCallValue
                {
                    Call = "@index",
                    Args = new Dictionary<string, JsonElement> { ["offset"] = JsonSerializer.SerializeToElement(1) },
                }
            ),
        };

        surface.UpdateComponents([listComp, itemTemplateComp]);

        var renderer = new A2UiRenderer(CatalogRegistry.CreateDefault());
        var control = renderer.Render(surface);

        Assert.NotNull(control);
        var sv = Assert.IsType<ScrollViewer>(control);
        var panel = Assert.IsType<StackPanel>(sv.Content);
        Assert.Equal(3, panel.Children.Count);

        var first = Assert.IsType<TextBlock>(panel.Children[0]);
        var second = Assert.IsType<TextBlock>(panel.Children[1]);
        var third = Assert.IsType<TextBlock>(panel.Children[2]);

        Assert.Equal("1", first.Text);
        Assert.Equal("2", second.Text);
        Assert.Equal("3", third.Text);
    }

    [AvaloniaFact]
    public void UnknownCatalogId_OnV10Surface_ThrowsCatalogException_DoesNotFallback()
    {
        var catalog = CatalogRegistry.CreateDefault();
        var surface = new Surface("s1", "https://untrusted.example.com/catalog.json")
        {
            ProtocolVersion = A2UiProtocol.Version10,
        };
        var comp = new TextComponent { Id = "root", Text = DynamicValue.FromString("test") };
        surface.UpdateComponents([comp]);

        Assert.False(catalog.TryResolveEntry(surface, comp, out _));
        Assert.Throws<A2UiCatalogException>(() => catalog.ResolveEntry(surface, comp));

        var renderer = new A2UiRenderer(catalog);
        Assert.Throws<A2UiCatalogException>(() => renderer.Render(surface));
    }

    [AvaloniaFact]
    public void UnknownCatalogId_OnV10Component_ThrowsCatalogException_DoesNotFallback()
    {
        var catalog = CatalogRegistry.CreateDefault();
        var surface = new Surface("s1")
        {
            ProtocolVersion = A2UiProtocol.Version10,
        };
        var comp = new TextComponent
        {
            Id = "root",
            CatalogId = "https://untrusted.example.com/catalog.json",
            Text = DynamicValue.FromString("test"),
        };
        surface.UpdateComponents([comp]);

        Assert.False(catalog.TryResolveEntry(surface, comp, out _));
        Assert.Throws<A2UiCatalogException>(() => catalog.ResolveEntry(surface, comp));

        var renderer = new A2UiRenderer(catalog);
        Assert.Throws<A2UiCatalogException>(() => renderer.Render(surface));
    }

    [AvaloniaFact]
    public void RpcHandler_AgentFunctionCall_ResolvesDynamicValue()
    {
        var rpcHandler = new RpcHandler();
        rpcHandler.RegisterFunction(new FunctionDefinition
        {
            Name = "getGreeting",
            AllowedCallers = AllowedCallers.RendererOrAgent,
            RequiresUserActivation = false,
        });

        rpcHandler.MessageOutbound += msg =>
        {
            if (msg.CallAgentFunction is { } call)
            {
                rpcHandler.HandleAgentFunctionResponse(new FunctionResponse
                {
                    FunctionCallId = call.FunctionCallId,
                    Value = JsonSerializer.SerializeToElement("Hello from Agent RPC!"),
                });
            }
        };

        var catalog = CatalogRegistry.CreateDefault();
        var renderer = new A2UiRenderer(catalog, rpcHandler: rpcHandler);
        var surface = new Surface("s1", A2UiProtocol.BasicCatalogIdV10) { ProtocolVersion = A2UiProtocol.Version10 };
        surface.UpdateComponents([
            new TextComponent
            {
                Id = "root",
                Text = DynamicValue.FromFunctionCall(new FunctionCallValue { Call = "getGreeting" }),
            },
        ]);

        var control = renderer.Render(surface);
        var tb = Assert.IsType<TextBlock>(control);
        Assert.Equal("Hello from Agent RPC!", tb.Text);
    }

    [AvaloniaFact]
    public void RpcHandler_AgentOnlyFunction_RefusedByRenderer()
    {
        var rpcHandler = new RpcHandler();
        rpcHandler.RegisterFunction(new FunctionDefinition
        {
            Name = "agentSecret",
            AllowedCallers = AllowedCallers.AgentOnly,
            RequiresUserActivation = false,
        });

        var catalog = CatalogRegistry.CreateDefault();
        var renderer = new A2UiRenderer(catalog, rpcHandler: rpcHandler);
        var surface = new Surface("s1") { ProtocolVersion = A2UiProtocol.Version10 };
        surface.UpdateComponents([
            new TextComponent
            {
                Id = "root",
                Text = DynamicValue.FromFunctionCall(new FunctionCallValue { Call = "agentSecret" }),
            },
        ]);

        var ex = Assert.Throws<A2UiEvaluationException>(() => renderer.Render(surface));
        Assert.Contains("allowedCallers is agentOnly", ex.Message);
    }

    [AvaloniaFact]
    public async Task RpcHandler_AsyncAgentFunctionCall_ResolvesNonBlockingAndUpdatesOnCompletionAsync()
    {
        using var rpcHandler = new RpcHandler();
        rpcHandler.RegisterFunction(new FunctionDefinition
        {
            Name = "fetchRemoteGreeting",
            CatalogId = "store_catalog",
            AllowedCallers = AllowedCallers.RendererOrAgent,
            RequiresUserActivation = false,
        });

        var emittedCalls = new List<CallAgentFunction>();
        rpcHandler.MessageOutbound += msg =>
        {
            if (msg.CallAgentFunction is { } call)
            {
                emittedCalls.Add(call);
            }
        };

        var catalog = CatalogRegistry.CreateDefault();
        using var renderer = new A2UiRenderer(catalog, rpcHandler: rpcHandler);
        var settledTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        renderer.DataModelChanged += (_, _) => settledTcs.TrySetResult();

        var surface = new Surface("s1") { ProtocolVersion = A2UiProtocol.Version10 };
        surface.UpdateComponents([
            new TextComponent
            {
                Id = "root",
                Text = DynamicValue.FromFunctionCall(
                    new FunctionCallValue { Call = "fetchRemoteGreeting", CatalogId = "store_catalog" }
                ),
            },
        ]);

        // Initial render does not block UI thread; text starts empty while RPC is in-flight
        var control = renderer.Render(surface);
        var tb = Assert.IsType<TextBlock>(control);
        Assert.Equal(string.Empty, tb.Text);
        Assert.Single(emittedCalls);

        // Second render while in-flight deduplicates and does not emit another RPC call
        _ = renderer.Render(surface);
        Assert.Single(emittedCalls);

        // Deliver async response and await UI dispatcher settlement
        rpcHandler.HandleAgentFunctionResponse(new FunctionResponse
        {
            FunctionCallId = emittedCalls[0].FunctionCallId,
            Value = JsonSerializer.SerializeToElement("Hello from async Agent!"),
        });

        await settledTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.Equal("Hello from async Agent!", tb.Text);
    }

    [AvaloniaFact]
    public void A2UiSurface_RefreshAfterDataModelUpdate_PreservesChildControlsWithoutDetachingAndUpdatesBindings()
    {
        var surface = new Surface("s_focus") { ProtocolVersion = A2UiProtocol.Version10 };
        surface.DataModel.SetSnapshot(
            JsonDocument.Parse("""{"name":"Ada","status":"Draft"}""").RootElement
        );
        surface.UpdateComponents([
            new ColumnComponent
            {
                Id = "root",
                Children = ChildList.FromIds(["row1", "statusText"]),
            },
            new RowComponent
            {
                Id = "row1",
                Children = ChildList.FromIds(["nameInput", "greetingText"]),
            },
            new TextFieldComponent
            {
                Id = "nameInput",
                Value = DynamicValue.FromPath("/name"),
            },
            new TextComponent
            {
                Id = "greetingText",
                Text = DynamicValue.FromPath("/name"),
            },
            new TextComponent
            {
                Id = "statusText",
                Text = DynamicValue.FromPath("/status"),
            },
        ]);

        var host = new A2UiSurface { Surface = surface };
        var window = new Window { Content = host };
        window.Show();
        try
        {
            var rootColumn = Assert.IsType<StackPanel>(host.Content);
            var rowPanel = Assert.IsType<StackPanel>(rootColumn.Children[0]);
            var statusBlock = Assert.IsType<TextBlock>(rootColumn.Children[1]);
            var textBox = Assert.IsType<TextBox>(rowPanel.Children[0]);
            var greetingBlock = Assert.IsType<TextBlock>(rowPanel.Children[1]);

            Assert.Equal("Ada", textBox.Text);
            Assert.Equal("Ada", greetingBlock.Text);
            Assert.Equal("Draft", statusBlock.Text);

            textBox.Focus();
            textBox.CaretIndex = 2;

            int detachCount = 0;
            rootColumn.DetachedFromVisualTree += (_, _) => detachCount++;
            rowPanel.DetachedFromVisualTree += (_, _) => detachCount++;
            textBox.DetachedFromVisualTree += (_, _) => detachCount++;
            greetingBlock.DetachedFromVisualTree += (_, _) => detachCount++;
            statusBlock.DetachedFromVisualTree += (_, _) => detachCount++;

            // Simulate data-model-only update and surface Refresh()
            surface.DataModel.Apply("/name", JsonSerializer.SerializeToElement("Ada Lovelace"));
            surface.DataModel.Apply("/status", JsonSerializer.SerializeToElement("Saved"));
            host.Refresh();

            Assert.Equal(0, detachCount);
            Assert.Same(rootColumn, host.Content);
            Assert.Same(rowPanel, rootColumn.Children[0]);
            Assert.Same(statusBlock, rootColumn.Children[1]);
            Assert.Same(textBox, rowPanel.Children[0]);
            Assert.Same(greetingBlock, rowPanel.Children[1]);
            Assert.Same(rowPanel, textBox.Parent);
            Assert.Same(rowPanel, greetingBlock.Parent);
            Assert.Equal("Ada Lovelace", greetingBlock.Text);
            Assert.Equal("Saved", statusBlock.Text);
            Assert.True(textBox.IsFocused);
            Assert.Equal(2, textBox.CaretIndex);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("v0.9.1")]
    [InlineData("1.0")]
    public void A2UiSurface_RefreshWithNestedModalListAndTabs_DoesNotThrowAcrossMultipleUpdates(string protocolVersion)
    {
        var surface = new Surface("s_modal_refresh") { ProtocolVersion = protocolVersion };
        surface.DataModel.SetSnapshot(
            JsonDocument.Parse("""{"title":"Round 1","modalBody":"Details 1","tabLabel":"Tab A"}""").RootElement
        );
        surface.UpdateComponents([
            new ColumnComponent
            {
                Id = "root",
                Children = ChildList.FromIds(["modal1", "list1", "tabs1"]),
            },
            new ModalComponent
            {
                Id = "modal1",
                Trigger = "modalTrigger",
                Content = "modalContent",
            },
            new ButtonComponent
            {
                Id = "modalTrigger",
                Text = DynamicValue.FromPath("/title"),
            },
            new TextComponent
            {
                Id = "modalContent",
                Text = DynamicValue.FromPath("/modalBody"),
            },
            new ListComponent
            {
                Id = "list1",
                Children = ChildList.FromIds(["listItem1"]),
            },
            new TextComponent
            {
                Id = "listItem1",
                Text = DynamicValue.FromPath("/title"),
            },
            new TabsComponent
            {
                Id = "tabs1",
                Tabs =
                [
                    new TabDefinition
                    {
                        Title = DynamicValue.FromPath("/tabLabel"),
                        Child = "tabChild1",
                    },
                ],
            },
            new TextComponent
            {
                Id = "tabChild1",
                Text = DynamicValue.FromPath("/modalBody"),
            },
        ]);

        var host = new A2UiSurface { Surface = surface };
        var window = new Window { Content = host };
        window.Show();
        try
        {
            var rootColumn = Assert.IsType<StackPanel>(host.Content);
            var listViewer = Assert.IsType<ScrollViewer>(rootColumn.Children[1]);
            var listPanel = Assert.IsType<StackPanel>(listViewer.Content);
            var listItemBlock = Assert.IsType<TextBlock>(listPanel.Children[0]);
            var tabControl = Assert.IsType<TabControl>(rootColumn.Children[2]);
            var tabItem = Assert.IsType<TabItem>(tabControl.Items[0]);
            var tabChildBlock = Assert.IsType<TextBlock>(tabItem.Content);

            int listAndTabDetachCount = 0;
            listItemBlock.DetachedFromVisualTree += (_, _) => listAndTabDetachCount++;
            tabChildBlock.DetachedFromVisualTree += (_, _) => listAndTabDetachCount++;

            // Refresh #1 after data model mutation
            surface.DataModel.Apply("/title", JsonSerializer.SerializeToElement("Round 2"));
            surface.DataModel.Apply("/modalBody", JsonSerializer.SerializeToElement("Details 2"));
            surface.DataModel.Apply("/tabLabel", JsonSerializer.SerializeToElement("Tab B"));
            host.Refresh();

            Assert.Equal("Round 2", listItemBlock.Text);
            Assert.Equal("Tab B", tabItem.Header);
            Assert.Equal("Details 2", tabChildBlock.Text);
            Assert.Equal(0, listAndTabDetachCount);

            // Open modal (evaluates lazy modal content in v1.0) and refresh again
            var modalContainer = Assert.IsType<Panel>(rootColumn.Children[0]);
            var triggerBtn = Assert.IsType<Button>(modalContainer.Children[0]);
            triggerBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, triggerBtn));

            surface.DataModel.Apply("/title", JsonSerializer.SerializeToElement("Round 3"));
            surface.DataModel.Apply("/modalBody", JsonSerializer.SerializeToElement("Details 3"));
            host.Refresh();

            var refreshedModalContainer = Assert.IsType<Panel>(rootColumn.Children[0]);
            var refreshedTriggerBtn = Assert.IsType<Button>(refreshedModalContainer.Children[0]);
            Assert.Same(triggerBtn, refreshedTriggerBtn);
            Assert.Equal("Round 3", refreshedTriggerBtn.Content);
            Assert.Equal("Round 3", listItemBlock.Text);
            Assert.Equal("Details 3", tabChildBlock.Text);
            Assert.Equal(0, listAndTabDetachCount);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void A2UiSurface_UpdateWithDuplicateChildIdsInSameLengthPanel_DoesNotThrow()
    {
        var surface = new Surface("s1", "https://a2ui.org/specification/v1_0/standard_catalog.json")
        {
            ProtocolVersion = A2UiProtocol.Version10,
        };

        surface.UpdateComponents([
            new ColumnComponent
            {
                Id = "root",
                Children = ChildList.FromIds("childA", "childB"),
            },
            new TextComponent { Id = "childA", Text = DynamicValue.FromString("A") },
            new TextComponent { Id = "childB", Text = DynamicValue.FromString("B") },
            new TextComponent { Id = "childNew", Text = DynamicValue.FromString("New") },
        ]);

        var host = new A2UiSurface { Surface = surface };
        var window = new Window { Content = host };
        window.Show();
        try
        {
            surface.UpdateComponents([
                new ColumnComponent
                {
                    Id = "root",
                    Children = ChildList.FromIds("childNew", "childNew"),
                },
            ]);

            host.Refresh();

            var rootColumn = Assert.IsType<StackPanel>(host.Content);
            Assert.Single(rootColumn.Children);
            var textBlock = Assert.IsType<TextBlock>(rootColumn.Children[0]);
            Assert.Equal("New", textBlock.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void LibraryAssembliesAndDefaultStyles_MatchPackageIdsAndLoadCleanly()
    {
        Assert.Equal("Blazejewicz.A2Ui.Core", typeof(SurfaceManager).Assembly.GetName().Name);
        Assert.Equal("Blazejewicz.A2Ui.AgUi.Protocol", typeof(SseEventParser).Assembly.GetName().Name);
        Assert.Equal("Blazejewicz.A2Ui.Avalonia", typeof(A2UiSurface).Assembly.GetName().Name);

        var surfaceControl = new A2UiSurface();
        var styleInclude = Assert.Single(surfaceControl.Styles.OfType<StyleInclude>());
        Assert.Equal(
            new Uri("avares://Blazejewicz.A2Ui.Avalonia/Themes/A2UiDefaultStyles.axaml"),
            styleInclude.Source
        );

        var window = new Window { Content = surfaceControl };
        window.Show();
        try
        {
            var loadedStyles = Assert.IsType<Styles>(styleInclude.Loaded);
            Assert.NotEmpty(loadedStyles);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void RpcHandler_AgentFunctionCall_DefaultsCatalogIdAndResolvesNativeTypedArgs()
    {
        using var rpcHandler = new RpcHandler();
        CallAgentFunction? capturedCall = null;
        rpcHandler.MessageOutbound += msg =>
        {
            if (msg.CallAgentFunction is { } call)
            {
                capturedCall = call;
                rpcHandler.HandleAgentFunctionResponse(new FunctionResponse
                {
                    FunctionCallId = call.FunctionCallId,
                    Value = JsonSerializer.SerializeToElement("Converted"),
                });
            }
        };

        using var renderer = new A2UiRenderer(CatalogRegistry.CreateDefault(), FunctionRegistry.CreateDefault(), rpcHandler: rpcHandler);
        var surface = new Surface("s1", A2UiProtocol.BasicCatalogIdV10) { ProtocolVersion = A2UiProtocol.Version10 };
        surface.DataModel.SetSnapshot(JsonDocument.Parse("""{"currency":"EUR","nested":true}""").RootElement);
        surface.UpdateComponents([
            new TextComponent
            {
                Id = "root",
                Text = DynamicValue.FromFunctionCall(
                    new FunctionCallValue
                    {
                        Call = "convertCurrency",
                        Args = new Dictionary<string, JsonElement>
                        {
                            ["currency"] = JsonDocument.Parse("""{"@path":"/currency"}""").RootElement.Clone(),
                            ["amount"] = JsonDocument.Parse("100").RootElement.Clone(),
                            ["enabled"] = JsonDocument.Parse("""{"@path":"/nested"}""").RootElement.Clone(),
                            ["tags"] = JsonDocument.Parse("""["a",{"@path":"/currency"}]""").RootElement.Clone(),
                        },
                    }
                ),
            },
        ]);

        var control = renderer.Render(surface);
        var tb = Assert.IsType<TextBlock>(control);
        Assert.Equal("Converted", tb.Text);

        Assert.NotNull(capturedCall);
        Assert.Equal(A2UiProtocol.BasicCatalogIdV10, capturedCall.CallFunction.CatalogId);
        Assert.NotNull(capturedCall.CallFunction.Args);
        Assert.Equal(JsonValueKind.String, capturedCall.CallFunction.Args["currency"].ValueKind);
        Assert.Equal("EUR", capturedCall.CallFunction.Args["currency"].GetString());
        Assert.Equal(JsonValueKind.Number, capturedCall.CallFunction.Args["amount"].ValueKind);
        Assert.Equal(100, capturedCall.CallFunction.Args["amount"].GetInt32());
        Assert.Equal(JsonValueKind.True, capturedCall.CallFunction.Args["enabled"].ValueKind);
        Assert.Equal(JsonValueKind.Array, capturedCall.CallFunction.Args["tags"].ValueKind);
        Assert.Equal("EUR", capturedCall.CallFunction.Args["tags"][1].GetString());
    }

    [AvaloniaFact]
    public void RpcHandler_MissingCatalogId_OrUnregisteredCatalog_ThrowsCatalogErrorWithoutDispatch()
    {
        using var rpcHandler = new RpcHandler();
        rpcHandler.RegisterFunction(new FunctionDefinition
        {
            Name = "getGreeting",
            AllowedCallers = AllowedCallers.RendererOrAgent,
        });

        var outbound = new List<CallAgentFunction>();
        rpcHandler.MessageOutbound += msg =>
        {
            if (msg.CallAgentFunction is { } call)
            {
                outbound.Add(call);
            }
        };

        using var renderer = new A2UiRenderer(CatalogRegistry.CreateDefault(), rpcHandler: rpcHandler);

        // Neither call.catalogId nor surface.catalogId present
        var noCatalogSurface = new Surface("s_no_cat") { ProtocolVersion = A2UiProtocol.Version10 };
        noCatalogSurface.UpdateComponents([
            new TextComponent
            {
                Id = "root",
                Text = DynamicValue.FromFunctionCall(new FunctionCallValue { Call = "getGreeting" }),
            },
        ]);

        var missingCatEx = Assert.Throws<A2UiEvaluationException>(() => renderer.Render(noCatalogSurface));
        Assert.Equal("CATALOG_ERROR", missingCatEx.ErrorCode);
        Assert.Empty(outbound);

        // Explicit unknown catalogId differing from surface.CatalogId
        var knownSurface = new Surface("s_known_cat", A2UiProtocol.BasicCatalogIdV10)
        {
            ProtocolVersion = A2UiProtocol.Version10,
        };
        knownSurface.UpdateComponents([
            new TextComponent
            {
                Id = "root",
                Text = DynamicValue.FromFunctionCall(
                    new FunctionCallValue { Call = "unregisteredOp", CatalogId = "unknown/catalog" }
                ),
            },
        ]);

        var unknownCatEx = Assert.Throws<A2UiEvaluationException>(() => renderer.Render(knownSurface));
        Assert.Equal("CATALOG_ERROR", unknownCatEx.ErrorCode);
        Assert.Empty(outbound);
    }

    [AvaloniaFact]
    public void ResolveFunction_UnknownSystemFunction_ThrowsExpressionErrorWithoutRpcDispatch()
    {
        using var rpcHandler = new RpcHandler();
        var outbound = new List<CallAgentFunction>();
        rpcHandler.MessageOutbound += msg =>
        {
            if (msg.CallAgentFunction is { } call)
            {
                outbound.Add(call);
            }
        };

        using var renderer = new A2UiRenderer(
            CatalogRegistry.CreateDefault(),
            FunctionRegistry.CreateDefault(),
            rpcHandler: rpcHandler
        );
        var surface = new Surface("s1", A2UiProtocol.BasicCatalogIdV10) { ProtocolVersion = A2UiProtocol.Version10 };
        surface.UpdateComponents([
            new TextComponent
            {
                Id = "root",
                Text = DynamicValue.FromFunctionCall(new FunctionCallValue { Call = "@unknown" }),
            },
        ]);

        var ex = Assert.Throws<A2UiEvaluationException>(() => renderer.Render(surface));
        Assert.Equal("EXPRESSION_ERROR", ex.ErrorCode);
        Assert.Empty(outbound);
    }

    [AvaloniaFact]
    public void FormatString_TemplatePartUnknownFunction_NeverRoutesToRpcAndEmitsExpressionError()
    {
        using var rpcHandler = new RpcHandler();
        rpcHandler.RegisterFunction(new FunctionDefinition
        {
            Name = "fetchRate",
            CatalogId = A2UiProtocol.BasicCatalogIdV10,
            AllowedCallers = AllowedCallers.RendererOrAgent,
        });

        var outbound = new List<CallAgentFunction>();
        rpcHandler.MessageOutbound += msg =>
        {
            if (msg.CallAgentFunction is { } call)
            {
                outbound.Add(call);
            }
        };

        using var renderer = new A2UiRenderer(
            CatalogRegistry.CreateDefault(),
            FunctionRegistry.CreateDefault(),
            rpcHandler: rpcHandler
        );
        var errors = new List<RendererToAgentMessage>();
        renderer.ErrorReported += (_, e) => errors.Add(e);

        var surface = new Surface("s1", A2UiProtocol.BasicCatalogIdV10) { ProtocolVersion = A2UiProtocol.Version10 };
        surface.UpdateComponents([
            new ColumnComponent
            {
                Id = "root",
                Children = ChildList.FromIds("direct_call", "template_call"),
            },
            new TextComponent
            {
                Id = "direct_call",
                Text = DynamicValue.FromFunctionCall(
                    new FunctionCallValue
                    {
                        Call = "fetchRate",
                        Args = new Dictionary<string, JsonElement>
                        {
                            ["id"] = JsonSerializer.SerializeToElement(7),
                        },
                    }
                ),
            },
            new TextComponent
            {
                Id = "template_call",
                Text = DynamicValue.FromFunctionCall(
                    new FunctionCallValue
                    {
                        Call = "formatString",
                        Args = new Dictionary<string, JsonElement>
                        {
                            ["value"] = JsonSerializer.SerializeToElement("Rate: ${fetchRate(id: 7)}"),
                        },
                    }
                ),
            },
        ]);

        // Settle direct fetchRate call first so rpcCache holds a value for fetchRate(id: 7)
        var panel = Assert.IsType<StackPanel>(renderer.Render(surface));
        Assert.Single(outbound);
        Assert.Equal("fetchRate", outbound[0].CallFunction.Call);

        rpcHandler.HandleAgentFunctionResponse(new FunctionResponse
        {
            FunctionCallId = outbound[0].FunctionCallId,
            Value = JsonSerializer.SerializeToElement(1.08),
        });

        // Wait for cache settlement via render pass & verify template part never reads cache or routes
        errors.Clear();
        outbound.Clear();
        panel = Assert.IsType<StackPanel>(renderer.Render(surface));

        var templateTb = Assert.IsType<TextBlock>(panel.Children[1]);
        Assert.Equal("Rate: ", templateTb.Text);
        Assert.Empty(outbound);

        RendererToAgentMessage errorMsg = Assert.Single(errors);
        errorMsg.Validate();
        Assert.NotNull(errorMsg.Error);
        Assert.Equal("EXPRESSION_ERROR", errorMsg.Error.Code);
        Assert.Equal("s1", errorMsg.Error.SurfaceId);
        Assert.Null(errorMsg.Error.FunctionCallId);
        Assert.Contains("fetchRate", errorMsg.Error.Message);
    }

    [AvaloniaFact]
    public async Task RpcHandler_SiblingAgentArgs_AllDispatchBeforeAnswerAndFailedSiblingPreventsEnclosingCallAsync()
    {
        using var rpcHandler = new RpcHandler();
        var outbound = new List<CallAgentFunction>();
        rpcHandler.MessageOutbound += msg =>
        {
            if (msg.CallAgentFunction is { } call)
            {
                outbound.Add(call);
            }
        };

        using var renderer = new A2UiRenderer(
            CatalogRegistry.CreateDefault(),
            FunctionRegistry.CreateDefault(),
            rpcHandler: rpcHandler
        );
        var errors = new List<RendererToAgentMessage>();
        var errorTcs = new TaskCompletionSource<RendererToAgentMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        renderer.ErrorReported += (_, e) =>
        {
            errors.Add(e);
            errorTcs.TrySetResult(e);
        };

        var settledCount = 0;
        var secondSettleTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        renderer.DataModelChanged += (_, _) =>
        {
            if (Interlocked.Increment(ref settledCount) >= 2)
            {
                secondSettleTcs.TrySetResult();
            }
        };

        var surface = new Surface("s1", A2UiProtocol.BasicCatalogIdV10) { ProtocolVersion = A2UiProtocol.Version10 };
        surface.UpdateComponents([
            new TextComponent
            {
                Id = "root",
                Text = DynamicValue.FromFunctionCall(
                    new FunctionCallValue
                    {
                        Call = "outerAgent",
                        Args = new Dictionary<string, JsonElement>
                        {
                            ["rate"] = JsonDocument.Parse("""{"@call":"fetchFxRate","args":{"base":"EUR"}}""").RootElement.Clone(),
                            ["stock"] = JsonDocument.Parse("""{"@call":"lookupSku","args":{"sku":"A-1"}}""").RootElement.Clone(),
                        },
                    }
                ),
            },
        ]);

        var control = renderer.Render(surface);
        var tb = Assert.IsType<TextBlock>(control);
        Assert.Equal(string.Empty, tb.Text);

        // Both sibling calls fired together; outerAgent MUST NOT be dispatched while siblings are pending
        Assert.Equal(2, outbound.Count);
        Assert.Contains(outbound, c => c.CallFunction.Call == "fetchFxRate");
        Assert.Contains(outbound, c => c.CallFunction.Call == "lookupSku");
        Assert.DoesNotContain(outbound, c => c.CallFunction.Call == "outerAgent");

        // Fail first sibling
        CallAgentFunction fxCall = outbound.Single(c => c.CallFunction.Call == "fetchFxRate");
        CallAgentFunction skuCall = outbound.Single(c => c.CallFunction.Call == "lookupSku");
        rpcHandler.HandleAgentFunctionResponse(new FunctionResponse
        {
            FunctionCallId = fxCall.FunctionCallId,
            Error = new FunctionError { Code = "EXECUTION_ERROR", Message = "Sibling lookup failed" },
        });

        RendererToAgentMessage firstError = await errorTcs.Task
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        firstError.Validate();
        Assert.NotNull(firstError.Error);
        Assert.Equal("EXPRESSION_ERROR", firstError.Error.Code);
        Assert.Equal(fxCall.FunctionCallId, firstError.Error.FunctionCallId);
        Assert.Null(firstError.Error.SurfaceId);
        Assert.Contains("Sibling lookup failed", firstError.Error.Message);

        // Succeed second sibling
        rpcHandler.HandleAgentFunctionResponse(new FunctionResponse
        {
            FunctionCallId = skuCall.FunctionCallId,
            Value = JsonSerializer.SerializeToElement(3),
        });

        await secondSettleTcs.Task
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        // outerAgent must still never have been dispatched, and the failure reported only once
        Assert.DoesNotContain(outbound, c => c.CallFunction.Call == "outerAgent");
        Assert.Single(errors);
        Assert.Equal(string.Empty, tb.Text);
    }

    [AvaloniaFact]
    public async Task RpcHandler_AsyncAgentError_ReportsSingleExpressionErrorWithFunctionCallIdAsync()
    {
        using var rpcHandler = new RpcHandler();
        var outbound = new List<CallAgentFunction>();
        rpcHandler.MessageOutbound += msg =>
        {
            if (msg.CallAgentFunction is { } call)
            {
                outbound.Add(call);
            }
        };

        using var renderer = new A2UiRenderer(CatalogRegistry.CreateDefault(), rpcHandler: rpcHandler);
        var errors = new List<RendererToAgentMessage>();
        renderer.ErrorReported += (_, e) => errors.Add(e);

        var settledTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        renderer.DataModelChanged += (_, _) => settledTcs.TrySetResult();

        var surface = new Surface("s1", A2UiProtocol.BasicCatalogIdV10) { ProtocolVersion = A2UiProtocol.Version10 };
        surface.UpdateComponents([
            new TextComponent
            {
                Id = "root",
                Text = DynamicValue.FromFunctionCall(new FunctionCallValue { Call = "fetchPrice" }),
            },
        ]);

        _ = renderer.Render(surface);
        CallAgentFunction call = Assert.Single(outbound);

        rpcHandler.HandleAgentFunctionResponse(new FunctionResponse
        {
            FunctionCallId = call.FunctionCallId,
            Error = new FunctionError { Code = "EXECUTION_ERROR", Message = "Upstream timeout" },
        });

        await settledTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ConfigureAwait(true);

        RendererToAgentMessage errorMsg = Assert.Single(errors);
        errorMsg.Validate();
        Assert.NotNull(errorMsg.Error);
        Assert.Equal("EXPRESSION_ERROR", errorMsg.Error.Code);
        Assert.Equal(call.FunctionCallId, errorMsg.Error.FunctionCallId);
        Assert.Null(errorMsg.Error.SurfaceId);
        Assert.Contains("Upstream timeout", errorMsg.Error.Message);
    }
}
