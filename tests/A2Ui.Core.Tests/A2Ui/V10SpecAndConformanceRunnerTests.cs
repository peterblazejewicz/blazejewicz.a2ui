using System;
using System.IO;
using System.Text.Json;
using Blazejewicz.A2Ui.Core.Bindings;
using Blazejewicz.A2Ui.Core.Components;
using Blazejewicz.A2Ui.Core.Messages;
using Blazejewicz.A2Ui.Core.Surfaces;
using Blazejewicz.A2Ui.Core.Versioning;

namespace Blazejewicz.A2Ui.Core.Tests.A2Ui;

public sealed class V10SpecAndConformanceRunnerTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "A2Ui.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root containing A2Ui.slnx.");
    }

    public static TheoryData<string, bool, string> DynamicValueValidationCases() =>
        LoadSpecJsonTestCases("dynamic_value_validation.json");

    public static TheoryData<string, bool, string> InitialStateValidationCases() =>
        LoadSpecJsonTestCases("initial_state_validation.json");

    private static TheoryData<string, bool, string> LoadSpecJsonTestCases(string fileName)
    {
        var data = new TheoryData<string, bool, string>();
        string path = Path.Combine(FindRepoRoot(), "specification", "v1_0", "test", "cases", fileName);
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));

        foreach (JsonElement test in doc.RootElement.GetProperty("tests").EnumerateArray())
        {
            string description = test.GetProperty("description").GetString()!;
            bool valid = test.GetProperty("valid").GetBoolean();
            string rawData = test.GetProperty("data").GetRawText();
            data.Add(description, valid, rawData);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(DynamicValueValidationCases))]
    public void SpecCases_DynamicValueValidation_MatchesExpectedOutcome(
        string description,
        bool expectedValid,
        string payloadJson
    )
    {
        _ = description;
        var adapter = new V10VersionAdapter();

        if (expectedValid)
        {
            InternalOperation op = adapter.AdaptAgentToRenderer(payloadJson);
            Assert.NotNull(op);
        }
        else
        {
            Assert.Throws<A2UiMessageValidationException>(() => adapter.AdaptAgentToRenderer(payloadJson));
        }
    }

    [Theory]
    [MemberData(nameof(InitialStateValidationCases))]
    public void SpecCases_InitialStateValidation_MatchesExpectedOutcome(
        string description,
        bool expectedValid,
        string payloadJson
    )
    {
        _ = description;
        var adapter = new V10VersionAdapter();

        if (expectedValid)
        {
            InternalOperation op = adapter.AdaptAgentToRenderer(payloadJson);
            Assert.NotNull(op);
        }
        else
        {
            Assert.Throws<A2UiMessageValidationException>(() => adapter.AdaptAgentToRenderer(payloadJson));
        }
    }

    [Fact]
    public void ConformanceYaml_ReservedKeys_AllCasesPass()
    {
        string yamlPath = Path.Combine(FindRepoRoot(), "conformance", "core", "reserved_keys.yaml");
        Assert.True(File.Exists(yamlPath), $"Missing conformance file: {yamlPath}");

        var sm = new SurfaceManager();
        var adapter = new V10VersionAdapter();

        // Case 1: test_reserved_at_path_valid
        const string AtPathValid = """
            {
              "version": "1.0",
              "createSurface": {
                "surfaceId": "s1",
                "catalogId": "basic",
                "components": [
                  { "id": "root", "component": "Text", "text": { "@path": "/user/displayName" } }
                ]
              }
            }
            """;
        Assert.NotNull(adapter.AdaptAgentToRenderer(AtPathValid));

        // Case 2: test_reserved_at_call_valid
        const string AtCallValid = """
            {
              "version": "1.0",
              "createSurface": {
                "surfaceId": "s1",
                "catalogId": "basic",
                "components": [
                  {
                    "id": "root",
                    "component": "Text",
                    "text": { "@call": "formatString", "args": { "value": "Hello world" } }
                  }
                ]
              }
            }
            """;
        Assert.NotNull(adapter.AdaptAgentToRenderer(AtCallValid));

        // Case 3: test_reserved_at_call_index
        const string AtCallIndex = """
            {
              "version": "1.0",
              "createSurface": {
                "surfaceId": "s1",
                "catalogId": "basic",
                "components": [
                  {
                    "id": "root",
                    "component": "List",
                    "children": { "componentId": "itemTemplate", "path": "/items" }
                  },
                  {
                    "id": "itemTemplate",
                    "component": "Text",
                    "text": { "@call": "@index", "args": { "offset": 1 } }
                  }
                ]
              }
            }
            """;
        Assert.NotNull(adapter.AdaptAgentToRenderer(AtCallIndex));

        // Case 4: test_plain_object_with_literal_path_and_call
        const string PlainObjLiteralPathCall = """
            {
              "version": "1.0",
              "createSurface": {
                "surfaceId": "s1",
                "catalogId": "custom",
                "components": [
                  {
                    "id": "root",
                    "component": "CustomCard",
                    "details": { "path": "/var/log/system.log", "call": "audit" }
                  }
                ]
              }
            }
            """;
        Assert.NotNull(adapter.AdaptAgentToRenderer(PlainObjLiteralPathCall));

        // Case 5: test_plain_object_escaped_doubled_at_key & Case 8: test_escaped_doubled_at_unescaping
        const string DoubledAtEscaped = """
            {
              "version": "1.0",
              "createSurface": {
                "surfaceId": "s_doubled",
                "catalogId": "custom",
                "components": [
                  {
                    "id": "root",
                    "component": "CustomCard",
                    "details": { "@@path": "/static/file", "@@type": "Resource" }
                  }
                ]
              }
            }
            """;
        sm.Process(DoubledAtEscaped);
        Surface? surface = sm.GetSurface("s_doubled");
        Assert.NotNull(surface);
        var customCard = Assert.IsType<GenericComponent>(surface.Components["root"]);
        using (IDisposable scope = DynamicValueConverter.EnterVersionScope(A2UiProtocol.Version10))
        {
            DynamicValue? detailsDv = customCard.Properties["details"].Deserialize<DynamicValue>();
            Assert.NotNull(detailsDv);
            Assert.True(detailsDv.IsObject);
            Assert.Equal("/static/file", detailsDv.AsObject!.Value["@path"]);
            Assert.Equal("Resource", detailsDv.AsObject.Value["@type"]);
        }

        // Case 6: test_disallowed_unknown_single_at_key
        const string DisallowedSingleAt = """
            {
              "version": "1.0",
              "createSurface": {
                "surfaceId": "s1",
                "catalogId": "custom",
                "components": [
                  {
                    "id": "root",
                    "component": "CustomCard",
                    "details": { "@if": true, "then": "active" }
                  }
                ]
              }
            }
            """;
        Assert.Throws<A2UiMessageValidationException>(() => adapter.AdaptAgentToRenderer(DisallowedSingleAt));

        // Case 7: test_disallowed_bare_at_key
        const string DisallowedBareAt = """
            {
              "version": "1.0",
              "createSurface": {
                "surfaceId": "s1",
                "catalogId": "custom",
                "components": [
                  {
                    "id": "root",
                    "component": "CustomCard",
                    "details": { "@": "reserved" }
                  }
                ]
              }
            }
            """;
        Assert.Throws<A2UiMessageValidationException>(() => adapter.AdaptAgentToRenderer(DisallowedBareAt));
    }

    [Fact]
    public void ConformanceYaml_DataDeletion_AllCasesPass()
    {
        string yamlPath = Path.Combine(FindRepoRoot(), "conformance", "core", "data_deletion.yaml");
        Assert.True(File.Exists(yamlPath), $"Missing conformance file: {yamlPath}");

        var sm = new SurfaceManager();
        sm.Process(
            """
            {
              "version": "1.0",
              "createSurface": {
                "surfaceId": "s1",
                "catalogId": "basic",
                "dataModel": {
                  "user": { "profile": { "name": "Alice", "bio": "Software Engineer" } },
                  "items": ["a", "b", "c"],
                  "transientData": "remove"
                }
              }
            }
            """
        );

        // Delete nested property
        sm.Process(
            """
            {
              "version": "1.0",
              "updateDataModel": {
                "surfaceId": "s1",
                "path": "/user/profile/bio",
                "value": null
              }
            }
            """
        );

        Surface surface = sm.GetSurface("s1")!;
        Assert.Equal("Alice", surface.DataModel.Resolve(DynamicValue.FromPath("/user/profile/name")));
        Assert.Null(surface.DataModel.Resolve(DynamicValue.FromPath("/user/profile/bio")));

        // Delete top-level property
        sm.Process(
            """
            {
              "version": "1.0",
              "updateDataModel": {
                "surfaceId": "s1",
                "path": "/transientData",
                "value": null
              }
            }
            """
        );
        Assert.Null(surface.DataModel.Resolve(DynamicValue.FromPath("/transientData")));

        // Array element null assignment preserves array length
        sm.Process(
            """
            {
              "version": "1.0",
              "updateDataModel": {
                "surfaceId": "s1",
                "path": "/items/1",
                "value": null
              }
            }
            """
        );
        Assert.Equal("a", surface.DataModel.Resolve(DynamicValue.FromPath("/items/0")));
        Assert.Null(surface.DataModel.Resolve(DynamicValue.FromPath("/items/1")));
        Assert.Equal("c", surface.DataModel.Resolve(DynamicValue.FromPath("/items/2")));
    }

    [Fact]
    public void SurfaceManager_Process_AgentToRendererMessage_PreservesPathValueBinding()
    {
        var sm = new SurfaceManager();
        var msg = new AgentToRendererMessage
        {
            Version = A2UiProtocol.Version10,
            CreateSurface = new CreateSurface
            {
                SurfaceId = "s_typed",
                CatalogId = A2UiProtocol.BasicCatalogIdV10,
                Components =
                [
                    new TextComponent
                    {
                        Id = "root",
                        Text = DynamicValue.FromPath("/user/name"),
                    },
                ],
                DataModel = JsonDocument.Parse("""{"user": {"name": "Bob"}}""").RootElement.Clone(),
            },
        };

        sm.Process(msg);

        Surface? surface = sm.GetSurface("s_typed");
        Assert.NotNull(surface);
        var textComp = Assert.IsType<TextComponent>(surface.Components["root"]);
        Assert.NotNull(textComp.Text);
        Assert.True(textComp.Text.IsPath);
        Assert.Equal("/user/name", textComp.Text.AsPath!.DataPath);
        Assert.Equal("Bob", surface.DataModel.Resolve(textComp.Text));
    }
}
