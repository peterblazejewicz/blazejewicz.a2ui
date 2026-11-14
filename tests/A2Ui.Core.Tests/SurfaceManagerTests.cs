using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Blazejewicz.A2Ui.Core.Bindings;
using Blazejewicz.A2Ui.Core.Children;
using Blazejewicz.A2Ui.Core.Components;
using Blazejewicz.A2Ui.Core.Messages;
using Blazejewicz.A2Ui.Core.Surfaces;
using Blazejewicz.A2Ui.Core.Versioning;

namespace Blazejewicz.A2Ui.Core.Tests;

public sealed class SurfaceManagerTests
{
    [Fact]
    public void Process_CreateSurface_RaisesEvent()
    {
        var sm = new SurfaceManager();
        Surface? created = null;
        sm.SurfaceCreated += (_, e) => created = e.Surface;

        sm.Process(
            new A2UiMessage
            {
                Version = "v0.9",
                CreateSurface = new CreateSurface
                {
                    SurfaceId = "main",
                    CatalogId = "https://a2ui.org/specification/v0_9/basic_catalog.json",
                },
            }
        );

        Assert.NotNull(created);
        Assert.Equal("main", created!.SurfaceId);
        Assert.Equal("https://a2ui.org/specification/v0_9/basic_catalog.json", created.CatalogId);
    }

    [Fact]
    public void Process_CreateSurface_StoresThemeAndSendDataModel()
    {
        var sm = new SurfaceManager();
        Surface? created = null;
        sm.SurfaceCreated += (_, e) => created = e.Surface;

        sm.Process(
            new A2UiMessage
            {
                Version = "v0.9",
                CreateSurface = new CreateSurface
                {
                    SurfaceId = "themed",
                    CatalogId = "test",
                    Theme = JsonSerializer.SerializeToElement(new { primaryColor = "#FF0000" }),
                    SendDataModel = true,
                },
            }
        );

        Assert.NotNull(created!.Theme);
        Assert.True(created.SendDataModel);
    }

    [Fact]
    public void Process_DuplicateCreate_IgnoresSecond()
    {
        var sm = new SurfaceManager(options: new SurfaceManagerOptions { StrictMode = false });
        int createCount = 0;
        sm.SurfaceCreated += (_, _) => createCount++;

        var msg = new A2UiMessage
        {
            Version = "v0.9",
            CreateSurface = new CreateSurface { SurfaceId = "s1", CatalogId = "c" },
        };

        sm.Process(msg);
        sm.Process(msg);

        Assert.Equal(1, createCount);
    }

    [Fact]
    public void Process_DeleteSurface_RemovesSurface()
    {
        var sm = new SurfaceManager();
        Surface? deleted = null;
        sm.SurfaceDeleted += (_, e) => deleted = e.Surface;

        sm.Process(
            new A2UiMessage
            {
                Version = "v0.9",
                CreateSurface = new CreateSurface { SurfaceId = "s1", CatalogId = "c" },
            }
        );
        sm.Process(
            new A2UiMessage
            {
                Version = "v0.9",
                DeleteSurface = new DeleteSurface { SurfaceId = "s1" },
            }
        );

        Assert.NotNull(deleted);
        Assert.Equal("s1", deleted!.SurfaceId);
        Assert.Null(sm.GetSurface("s1"));
    }

    [Fact]
    public void Process_DeleteNonexistent_NoOp()
    {
        var sm = new SurfaceManager(options: new SurfaceManagerOptions { StrictMode = false });
        bool deleteFired = false;
        sm.SurfaceDeleted += (_, _) => deleteFired = true;

        sm.Process(
            new A2UiMessage
            {
                Version = "v0.9",
                DeleteSurface = new DeleteSurface { SurfaceId = "ghost" },
            }
        );

        Assert.False(deleteFired);
    }

    [Fact]
    public void Process_UpdateComponents_StoresComponents()
    {
        var sm = new SurfaceManager();
        ComponentsUpdatedEventArgs? updatedArgs = null;
        sm.ComponentsUpdated += (_, e) => updatedArgs = e;

        sm.Process(
            new A2UiMessage
            {
                Version = "v0.9",
                CreateSurface = new CreateSurface { SurfaceId = "s1", CatalogId = "c" },
            }
        );
        sm.Process(
            new A2UiMessage
            {
                Version = "v0.9",
                UpdateComponents = new UpdateComponents
                {
                    SurfaceId = "s1",
                    Components =
                    [
                        new ColumnComponent { Id = "root" },
                        new TextComponent { Id = "t1", Parent = "root" },
                    ],
                },
            }
        );

        Assert.NotNull(updatedArgs);
        var surface = sm.GetSurface("s1");
        Assert.Equal(2, surface!.Components.Count);
        Assert.Equal("Text", surface.Components["t1"].Component);
    }

    [Fact]
    public void Process_UpdateComponents_MissingSurface_NoOp()
    {
        var sm = new SurfaceManager(options: new SurfaceManagerOptions { StrictMode = false });
        bool fired = false;
        sm.ComponentsUpdated += (_, _) => fired = true;

        sm.Process(
            new A2UiMessage
            {
                Version = "v0.9",
                UpdateComponents = new UpdateComponents
                {
                    SurfaceId = "ghost",
                    Components = [new TextComponent { Id = "t1" }],
                },
            }
        );

        Assert.False(fired);
    }

    [Fact]
    public void Process_UpdateDataModel_AppliesUpdate()
    {
        var sm = new SurfaceManager();
        bool fired = false;
        sm.DataModelUpdated += (_, _) => fired = true;

        sm.Process(
            new A2UiMessage
            {
                Version = "v0.9",
                CreateSurface = new CreateSurface { SurfaceId = "s1", CatalogId = "c" },
            }
        );
        sm.Process(
            new A2UiMessage
            {
                Version = "v0.9",
                UpdateDataModel = new UpdateDataModel
                {
                    SurfaceId = "s1",
                    Path = "/user/name",
                    Value = JsonSerializer.SerializeToElement("Alice"),
                },
            }
        );

        Assert.True(fired);
        var surface = sm.GetSurface("s1")!;
        Assert.Equal("Alice", surface.DataModel.Resolve(DynamicValue.FromPath("/user/name")));
    }

    [Fact]
    public void Process_UpdateDataModel_MissingSurface_NoOp()
    {
        var sm = new SurfaceManager(options: new SurfaceManagerOptions { StrictMode = false });
        bool fired = false;
        sm.DataModelUpdated += (_, _) => fired = true;

        sm.Process(
            new A2UiMessage
            {
                Version = "v0.9",
                UpdateDataModel = new UpdateDataModel
                {
                    SurfaceId = "ghost",
                    Path = "/x",
                    Value = JsonSerializer.SerializeToElement(1),
                },
            }
        );

        Assert.False(fired);
    }

    [Fact]
    public void Process_EmptyMessage_ThrowsValidation()
    {
        var sm = new SurfaceManager();

        var act = () => sm.Process(new A2UiMessage());

        Assert.Throws<A2UiMessageValidationException>(act);
    }

    [Fact]
    public void GetSurface_ExistingSurface_ReturnsSurface()
    {
        var sm = new SurfaceManager();
        sm.Process(
            new A2UiMessage
            {
                Version = "v0.9",
                CreateSurface = new CreateSurface { SurfaceId = "s1", CatalogId = "c" },
            }
        );

        Assert.NotNull(sm.GetSurface("s1"));
        Assert.Null(sm.GetSurface("nope"));
    }

    [Fact]
    public void Surface_GetRootComponents_ReturnsNullParentComponents()
    {
        var sm = new SurfaceManager();
        sm.Process(
            new A2UiMessage
            {
                Version = "v0.9",
                CreateSurface = new CreateSurface { SurfaceId = "s1", CatalogId = "c" },
            }
        );
        sm.Process(
            new A2UiMessage
            {
                Version = "v0.9",
                UpdateComponents = new UpdateComponents
                {
                    SurfaceId = "s1",
                    Components =
                    [
                        new ColumnComponent { Id = "root" },
                        new TextComponent { Id = "child1", Parent = "root" },
                        new TextComponent { Id = "child2", Parent = "root" },
                    ],
                },
            }
        );

        var surface = sm.GetSurface("s1")!;
        var roots = surface.GetRootComponents().ToList();
        var single = Assert.Single(roots);
        Assert.Equal("root", single.Id);
    }

    [Fact]
    public void Surface_GetRootComponents_V09ForwardRef_FindsRootById()
    {
        var sm = new SurfaceManager();
        sm.Process(
            new A2UiMessage
            {
                Version = "v0.9",
                CreateSurface = new CreateSurface { SurfaceId = "s1", CatalogId = "c" },
            }
        );
        sm.Process(
            new A2UiMessage
            {
                Version = "v0.9",
                UpdateComponents = new UpdateComponents
                {
                    SurfaceId = "s1",
                    Components =
                    [
                        new ColumnComponent { Id = "root", Children = ChildList.FromIds("t1", "t2") },
                        new TextComponent { Id = "t1" },
                        new TextComponent { Id = "t2" },
                    ],
                },
            }
        );

        var roots = sm.GetSurface("s1")!.GetRootComponents().ToList();
        var single = Assert.Single(roots);
        Assert.Equal("root", single.Id);
    }

    [Fact]
    public void Clear_RemovesAllSurfaces_FiresDeletedForEach()
    {
        var sm = new SurfaceManager();
        var deletedIds = new List<string>();
        sm.SurfaceDeleted += (_, e) => deletedIds.Add(e.Surface.SurfaceId);

        foreach (var id in new[] { "s1", "s2", "s3" })
        {
            sm.Process(
                new A2UiMessage
                {
                    Version = "v0.9",
                    CreateSurface = new CreateSurface { SurfaceId = id, CatalogId = "c" },
                }
            );
        }

        sm.Clear();

        Assert.Equal(3, deletedIds.Count);
        Assert.Contains("s1", deletedIds);
        Assert.Contains("s2", deletedIds);
        Assert.Contains("s3", deletedIds);
        Assert.Null(sm.GetSurface("s1"));
        Assert.Null(sm.GetSurface("s2"));
        Assert.Null(sm.GetSurface("s3"));
    }

    [Fact]
    public void Clear_EmptyManager_NoEventsRaised()
    {
        var sm = new SurfaceManager();
        bool fired = false;
        sm.SurfaceDeleted += (_, _) => fired = true;

        sm.Clear();

        Assert.False(fired);
    }

    [Fact]
    public void Clear_AllowsRecreateAfterClear()
    {
        var sm = new SurfaceManager();
        sm.Process(
            new A2UiMessage
            {
                Version = "v0.9",
                CreateSurface = new CreateSurface { SurfaceId = "s1", CatalogId = "c" },
            }
        );

        sm.Clear();

        Surface? recreated = null;
        sm.SurfaceCreated += (_, e) => recreated = e.Surface;
        sm.Process(
            new A2UiMessage
            {
                Version = "v0.9",
                CreateSurface = new CreateSurface { SurfaceId = "s1", CatalogId = "c" },
            }
        );

        Assert.NotNull(recreated);
        Assert.Equal("s1", recreated!.SurfaceId);
    }

    [Fact]
    public void Surface_GetRootComponents_NoExplicitRoot_ExcludesReferencedChildren()
    {
        var sm = new SurfaceManager(options: new SurfaceManagerOptions { StrictMode = false });
        sm.Process(
            new A2UiMessage
            {
                Version = "v0.9",
                CreateSurface = new CreateSurface { SurfaceId = "s1", CatalogId = "c" },
            }
        );
        sm.Process(
            new A2UiMessage
            {
                Version = "v0.9",
                UpdateComponents = new UpdateComponents
                {
                    SurfaceId = "s1",
                    Components =
                    [
                        new ColumnComponent { Id = "col1", Children = ChildList.FromIds("t1", "t2") },
                        new TextComponent { Id = "t1" },
                        new TextComponent { Id = "t2" },
                    ],
                },
            }
        );

        var roots = sm.GetSurface("s1")!.GetRootComponents().ToList();
        var single = Assert.Single(roots);
        Assert.Equal("col1", single.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Process_ReusesSurfaceIdAfterDelete_Succeeds(bool strictMode)
    {
        var sm = new SurfaceManager(options: new SurfaceManagerOptions { StrictMode = strictMode });

        // 1. createSurface("s1")
        sm.Process(
            new A2UiMessage
            {
                Version = A2UiProtocol.Version091,
                CreateSurface = new CreateSurface { SurfaceId = "s1", CatalogId = A2UiProtocol.BasicCatalogIdV091 },
            }
        );
        Assert.NotNull(sm.GetSurface("s1"));

        // 2. deleteSurface("s1")
        sm.Process(
            new A2UiMessage
            {
                Version = A2UiProtocol.Version091,
                DeleteSurface = new DeleteSurface { SurfaceId = "s1" },
            }
        );
        Assert.Null(sm.GetSurface("s1"));

        // 3. createSurface("s1") again (reuse surfaceId per v0.9.1 rule)
        sm.Process(
            new A2UiMessage
            {
                Version = A2UiProtocol.Version091,
                CreateSurface = new CreateSurface { SurfaceId = "s1", CatalogId = A2UiProtocol.BasicCatalogIdV091 },
            }
        );
        Assert.NotNull(sm.GetSurface("s1"));

        // 4. updateComponents("s1")
        sm.Process(
            new A2UiMessage
            {
                Version = A2UiProtocol.Version091,
                UpdateComponents = new UpdateComponents
                {
                    SurfaceId = "s1",
                    Components = [new TextComponent { Id = "root", Text = DynamicValue.FromString("Hello") }],
                },
            }
        );
        Assert.Contains("root", sm.GetSurface("s1")!.Components.Keys);
    }

    [Fact]
    public void SurfaceManager_Process_V10AllowsRecreateAfterDeleteSurface()
    {
        var sm = new SurfaceManager(options: new SurfaceManagerOptions { StrictMode = false });
        var reported = new List<RendererToAgentMessage>();
        sm.ErrorReported += (_, err) => reported.Add(err);

        sm.Process(
            """{"version":"1.0","createSurface":{"surfaceId":"gallery-invitation-builder","catalogId":"catalog_a"}}"""
        );
        Assert.Equal("catalog_a", sm.GetSurface("gallery-invitation-builder")!.CatalogId);

        // Duplicate createSurface while still active MUST fail with INTEGRITY_ERROR even when StrictMode = false
        var dupEx = Assert.Throws<A2UiMessageValidationException>(() =>
            sm.Process(
                """{"version":"1.0","createSurface":{"surfaceId":"gallery-invitation-builder","catalogId":"catalog_b"}}"""
            )
        );
        Assert.Contains("surface already exists", dupEx.Message);
        Assert.DoesNotContain("retired", dupEx.Message);
        Assert.Single(reported);
        Assert.Equal("INTEGRITY_ERROR", reported[0].Error!.Code);

        // Deleting and recreating with a new catalogId MUST succeed cleanly per v1.0 spec
        reported.Clear();
        sm.Process("""{"version":"1.0","deleteSurface":{"surfaceId":"gallery-invitation-builder"}}""");
        Assert.Null(sm.GetSurface("gallery-invitation-builder"));

        sm.Process(
            $$"""{"version":"1.0","createSurface":{"surfaceId":"gallery-invitation-builder","catalogId":"{{A2UiProtocol.BasicCatalogIdV10}}" } }"""
        );
        sm.Process(
            """
            {
                "version": "1.0",
                "updateComponents": {
                    "surfaceId": "gallery-invitation-builder",
                    "components": [
                        { "id": "root", "component": "Text", "text": { "@path": "/event/name" } }
                    ]
                }
            }
            """
        );
        sm.Process(
            """
            {
                "version": "1.0",
                "updateDataModel": {
                    "surfaceId": "gallery-invitation-builder",
                    "value": { "event": { "name": "Summer Gala" } }
                }
            }
            """
        );

        Assert.Empty(reported);
        Surface? recreated = sm.GetSurface("gallery-invitation-builder");
        Assert.NotNull(recreated);
        Assert.Equal(A2UiProtocol.BasicCatalogIdV10, recreated.CatalogId);
        Assert.Equal("Summer Gala", recreated.DataModel.Resolve(DynamicValue.FromPath("/event/name")));
    }

    [Fact]
    public void Process_V09VersionAdapter_PreservesWireProtocolVersionRegardlessOfTheme()
    {
        var sm = new SurfaceManager();
        sm.Process(
            new A2UiMessage
            {
                Version = "v0.9.1",
                CreateSurface = new CreateSurface
                {
                    SurfaceId = "s091_themed",
                    CatalogId = "basic",
                    Theme = JsonSerializer.SerializeToElement(new { primaryColor = "#123456" }),
                },
            }
        );
        sm.Process(
            new A2UiMessage
            {
                Version = "v0.9",
                CreateSurface = new CreateSurface
                {
                    SurfaceId = "s09_unthemed",
                    CatalogId = "basic",
                },
            }
        );

        Assert.Equal("v0.9.1", sm.GetSurface("s091_themed")!.ProtocolVersion);
        Assert.Equal("v0.9", sm.GetSurface("s09_unthemed")!.ProtocolVersion);
    }

    [Fact]
    public void Process_AgentToRendererMessage_UpdateDataModelNullValue_DeletesAtPath()
    {
        var sm = new SurfaceManager();
        sm.Process(
            new AgentToRendererMessage
            {
                Version = A2UiProtocol.Version10,
                CreateSurface = new CreateSurface
                {
                    SurfaceId = "s_null_del",
                    CatalogId = "basic",
                    DataModel = JsonDocument.Parse("""{"user":{"name":"Alice","bio":"Dev"}}""").RootElement.Clone(),
                },
            }
        );

        sm.Process(
            new AgentToRendererMessage
            {
                Version = A2UiProtocol.Version10,
                UpdateDataModel = new UpdateDataModel
                {
                    SurfaceId = "s_null_del",
                    Path = "/user/bio",
                    Value = null,
                },
            }
        );

        var surface = sm.GetSurface("s_null_del")!;
        Assert.Equal("Alice", surface.DataModel.Resolve(DynamicValue.FromPath("/user/name")));
        Assert.Null(surface.DataModel.Resolve(DynamicValue.FromPath("/user/bio")));
    }

    [Fact]
    public void Process_RegisteredCatalogIds_UnknownCatalog_EmitsCatalogNotFoundAndThrows()
    {
        var sm = new SurfaceManager(
            options: new SurfaceManagerOptions
            {
                RegisteredCatalogIds = new HashSet<string> { A2UiProtocol.BasicCatalogIdV10 },
            }
        );
        var reported = new List<RendererToAgentMessage>();
        sm.ErrorReported += (_, err) => reported.Add(err);

        Assert.Throws<A2UiMessageValidationException>(() =>
            sm.Process("""{"version":"1.0","createSurface":{"surfaceId":"s_cat","catalogId":"unknown_cat"}}""")
        );
        Assert.Single(reported);
        Assert.Equal("CATALOG_NOT_FOUND", reported[0].Error!.Code);
    }

    [Fact]
    public void Process_ReportErrorsOnly_EmitsErrorWithoutThrowing()
    {
        var sm = new SurfaceManager(options: new SurfaceManagerOptions { ReportErrorsOnly = true });
        var reported = new List<RendererToAgentMessage>();
        sm.ErrorReported += (_, err) => reported.Add(err);

        sm.Process("""{"version":"1.0","updateDataModel":{"surfaceId":"missing","path":"/a","value":1}}""");

        Assert.Single(reported);
        Assert.Equal("INTEGRITY_ERROR", reported[0].Error!.Code);
    }

    [Fact]
    public async Task ParseAndAdapt_StringAndUtf8Span_WorksConcurrentlyOffThreadAndAppliesCleanlyAsync()
    {
        var utf8Payloads = new byte[16][];
        var stringPayloads = new string[16];
        for (int i = 0; i < 16; i++)
        {
            if (i % 2 == 0)
            {
                string json = $$"""
                    {
                        "version": "1.0",
                        "createSurface": {
                            "surfaceId": "s_{{i}}",
                            "catalogId": "{{A2UiProtocol.BasicCatalogIdV10}}",
                            "dataModel": { "title": "Title {{i}}" },
                            "components": [
                                { "id": "root", "component": "Text", "text": { "@path": "/title" } }
                            ]
                        }
                    }
                    """;
                stringPayloads[i] = json;
                utf8Payloads[i] = Encoding.UTF8.GetBytes(json);
            }
            else
            {
                string json = $$"""
                    {
                        "version": "v0.9.1",
                        "createSurface": {
                            "surfaceId": "s_{{i}}",
                            "catalogId": "{{A2UiProtocol.BasicCatalogIdV091}}",
                            "dataModel": { "title": "Legacy {{i}}" },
                            "components": [
                                { "id": "root", "component": "Text", "text": { "path": "/title" } }
                            ]
                        }
                    }
                    """;
                stringPayloads[i] = json;
                utf8Payloads[i] = Encoding.UTF8.GetBytes(json);
            }
        }

        InternalOperation[] operations = await Task.WhenAll(
                Enumerable
                    .Range(0, 16)
                    .Select(idx =>
                        Task.Run(
                            () =>
                                idx < 8
                                    ? SurfaceManager.ParseAndAdapt(stringPayloads[idx])
                                    : SurfaceManager.ParseAndAdapt(utf8Payloads[idx].AsSpan()),
                            TestContext.Current.CancellationToken
                        )
                    )
            )
            .ConfigureAwait(true);

        var sm = new SurfaceManager();
        foreach (var op in operations)
        {
            sm.Process(op);
        }

        for (int i = 0; i < 16; i++)
        {
            Surface? surface = sm.GetSurface($"s_{i}");
            Assert.NotNull(surface);
            string expected = i % 2 == 0 ? $"Title {i}" : $"Legacy {i}";
            var rootText = Assert.IsType<TextComponent>(surface.Components["root"]);
            Assert.Equal(expected, surface.DataModel.Resolve(rootText.Text));
        }

        Assert.ThrowsAny<JsonException>(() =>
            SurfaceManager.ParseAndAdapt("""{"version":"1.0","deleteSurface":{"surfaceId":"x"}} {"extra":1}"""u8)
        );
    }

    [Theory]
    [InlineData(A2UiProtocol.Version09)]
    [InlineData(A2UiProtocol.Version091)]
    public void Process_DuplicateCreate_PreservesV09ProtocolVersionOnErrorReported(string version)
    {
        var sm = new SurfaceManager();
        var reported = new List<RendererToAgentMessage>();
        sm.ErrorReported += (_, err) => reported.Add(err);

        string json = $$"""
            {
                "version": "{{version}}",
                "createSurface": {
                    "surfaceId": "s1",
                    "catalogId": "{{A2UiProtocol.BasicCatalogIdV091}}"
                }
            }
            """;

        sm.Process(json);
        Assert.Throws<A2UiMessageValidationException>(() => sm.Process(json));

        RendererToAgentMessage errorMsg = Assert.Single(reported);
        Assert.Equal(version, errorMsg.Version);
        Assert.NotNull(errorMsg.Error);
        Assert.Equal("INTEGRITY_ERROR", errorMsg.Error.Code);
        Assert.Equal("s1", errorMsg.Error.SurfaceId);
    }

    [Theory]
    [InlineData(A2UiProtocol.Version09)]
    [InlineData(A2UiProtocol.Version091)]
    public void Process_UpdateDataModelUnknownSurface_PreservesV09ProtocolVersionOnErrorReported(string version)
    {
        string json = $$"""
            {
                "version": "{{version}}",
                "updateDataModel": {
                    "surfaceId": "unknown-s",
                    "path": "/foo",
                    "value": "bar"
                }
            }
            """;

        var adapter = new V09VersionAdapter();
        InternalOperation adaptedOp = adapter.AdaptAgentToRenderer(json);
        var udOp = Assert.IsType<UpdateDataModelOp>(adaptedOp);
        Assert.Equal(version, udOp.ProtocolVersion);

        var strictSm = new SurfaceManager();
        var strictReported = new List<RendererToAgentMessage>();
        strictSm.ErrorReported += (_, err) => strictReported.Add(err);

        Assert.Throws<A2UiMessageValidationException>(() => strictSm.Process(json));

        RendererToAgentMessage strictError = Assert.Single(strictReported);
        Assert.Equal(version, strictError.Version);
        Assert.Equal("INTEGRITY_ERROR", strictError.Error!.Code);
        Assert.Equal("unknown-s", strictError.Error.SurfaceId);

        var reportOnlySm = new SurfaceManager(options: new SurfaceManagerOptions { ReportErrorsOnly = true });
        var messageReported = new List<RendererToAgentMessage>();
        reportOnlySm.ErrorReported += (_, err) => messageReported.Add(err);

        reportOnlySm.Process(
            new A2UiMessage
            {
                Version = version,
                UpdateDataModel = new UpdateDataModel
                {
                    SurfaceId = "unknown-s",
                    Path = "/foo",
                    Value = JsonSerializer.SerializeToElement("bar"),
                },
            }
        );

        RendererToAgentMessage msgError = Assert.Single(messageReported);
        Assert.Equal(version, msgError.Version);
        Assert.Equal("INTEGRITY_ERROR", msgError.Error!.Code);
        Assert.Equal("unknown-s", msgError.Error.SurfaceId);
    }

    [Theory]
    [InlineData(A2UiProtocol.Version09)]
    [InlineData(A2UiProtocol.Version091)]
    public void Process_UpdateComponentsMissingRoot_PreservesV09ProtocolVersionOnErrorReported(string version)
    {
        var sm = new SurfaceManager();
        var reported = new List<RendererToAgentMessage>();
        sm.ErrorReported += (_, err) => reported.Add(err);

        sm.Process(
            new A2UiMessage
            {
                Version = version,
                CreateSurface = new CreateSurface { SurfaceId = "s1", CatalogId = "basic" },
            }
        );

        string updateJson = $$"""
            {
                "version": "{{version}}",
                "updateComponents": {
                    "surfaceId": "s1",
                    "components": [
                        { "id": "c1", "component": "Text", "text": "No root" }
                    ]
                }
            }
            """;

        var adapter = new V09VersionAdapter();
        var ucOp = Assert.IsType<UpdateComponentsOp>(adapter.AdaptAgentToRenderer(updateJson));
        Assert.Equal(version, ucOp.ProtocolVersion);

        Assert.Throws<A2UiMessageValidationException>(() => sm.Process(updateJson));

        RendererToAgentMessage errorMsg = Assert.Single(reported);
        Assert.Equal(version, errorMsg.Version);
        Assert.NotNull(errorMsg.Error);
        Assert.Equal("INTEGRITY_ERROR", errorMsg.Error.Code);
        Assert.Equal("s1", errorMsg.Error.SurfaceId);
    }

    [Theory]
    [InlineData(A2UiProtocol.Version10)]
    [InlineData(A2UiProtocol.Version09)]
    [InlineData(A2UiProtocol.Version091)]
    public void Process_DeleteUnknownSurface_ReportErrorsOnly_IsNoOpWithoutErrorReported(string version)
    {
        var sm = new SurfaceManager(
            options: new SurfaceManagerOptions { StrictMode = true, ReportErrorsOnly = true }
        );
        var reported = new List<RendererToAgentMessage>();
        bool deletedFired = false;
        sm.ErrorReported += (_, err) => reported.Add(err);
        sm.SurfaceDeleted += (_, _) => deletedFired = true;

        sm.Process(
            $$"""{"version":"{{version}}","deleteSurface":{"surfaceId":"unknown-surface"} }"""
        );

        Assert.Empty(reported);
        Assert.False(deletedFired);
    }

    [Theory]
    [InlineData(A2UiProtocol.Version10)]
    [InlineData(A2UiProtocol.Version09)]
    [InlineData(A2UiProtocol.Version091)]
    public void Process_DeleteUnknownSurface_StrictMode_EmitsIntegrityErrorAndThrows(string version)
    {
        var sm = new SurfaceManager(
            options: new SurfaceManagerOptions { StrictMode = true, ReportErrorsOnly = false }
        );
        var reported = new List<RendererToAgentMessage>();
        sm.ErrorReported += (_, err) => reported.Add(err);

        Assert.Throws<A2UiMessageValidationException>(() =>
            sm.Process(
                $$"""{"version":"{{version}}","deleteSurface":{"surfaceId":"unknown-surface"} }"""
            )
        );

        RendererToAgentMessage errorMsg = Assert.Single(reported);
        Assert.Equal(version, errorMsg.Version);
        Assert.NotNull(errorMsg.Error);
        Assert.Equal("INTEGRITY_ERROR", errorMsg.Error.Code);
        Assert.Equal("unknown-surface", errorMsg.Error.SurfaceId);
    }
}
