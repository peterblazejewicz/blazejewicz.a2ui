using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Blazejewicz.A2Ui.Avalonia.Catalog;
using Blazejewicz.A2Ui.Core;
using Blazejewicz.A2Ui.Core.Bindings;
using Blazejewicz.A2Ui.Core.Children;
using Blazejewicz.A2Ui.Core.Components;
using Blazejewicz.A2Ui.Core.Messages;
using Blazejewicz.A2Ui.Core.Rpc;
using Blazejewicz.A2Ui.Core.Surfaces;
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
        var surface = new Surface("s1") { ProtocolVersion = A2UiProtocol.Version10 };
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
}
