using System;
using System.Collections.Generic;
using System.Text.Json;
using Blazejewicz.A2Ui.Core.Bindings;
using Blazejewicz.A2Ui.Core.Messages;
using Blazejewicz.A2Ui.Core.Surfaces;

namespace Blazejewicz.A2Ui.Core.Tests;

public sealed class DataModelTests
{
    [Fact]
    public void Apply_UpdateDataModel_SetsNestedPath()
    {
        var dm = new DataModel();
        dm.Apply(
            new UpdateDataModel
            {
                SurfaceId = "main",
                Path = "/reservation/date",
                Value = JsonSerializer.SerializeToElement("2025-12-15"),
            }
        );

        Assert.Equal("2025-12-15", dm.Resolve(DynamicValue.FromPath("/reservation/date")));
    }

    [Fact]
    public void Apply_NullPath_ReplacesEntireModel()
    {
        var dm = new DataModel();
        dm.Apply(
            new UpdateDataModel
            {
                SurfaceId = "main",
                Path = "/name",
                Value = JsonSerializer.SerializeToElement("old"),
            }
        );

        dm.Apply(
            new UpdateDataModel
            {
                SurfaceId = "main",
                Path = null,
                Value = JsonSerializer.SerializeToElement(new { name = "replaced" }),
            }
        );

        Assert.Equal("replaced", dm.Resolve(DynamicValue.FromPath("/name")));
    }

    [Fact]
    public void Apply_RootPath_ReplacesEntireModel()
    {
        var dm = new DataModel();
        dm.Apply(
            new UpdateDataModel
            {
                SurfaceId = "main",
                Path = "/",
                Value = JsonSerializer.SerializeToElement(new { key = "val" }),
            }
        );

        Assert.Equal("val", dm.Resolve(DynamicValue.FromPath("/key")));
    }

    [Fact]
    public void Apply_NullValue_DeletesAtPath()
    {
        var dm = new DataModel();
        dm.Apply(
            new UpdateDataModel
            {
                SurfaceId = "main",
                Path = "/toDelete",
                Value = JsonSerializer.SerializeToElement("exists"),
            }
        );
        Assert.Equal("exists", dm.Resolve(DynamicValue.FromPath("/toDelete")));

        dm.Apply(
            new UpdateDataModel
            {
                SurfaceId = "main",
                Path = "/toDelete",
                Value = null,
            }
        );

        Assert.Null(dm.Resolve(DynamicValue.FromPath("/toDelete")));
    }

    [Fact]
    public void Apply_OverwriteExistingKey_ReplacesValue()
    {
        var dm = new DataModel();
        dm.Apply(
            new UpdateDataModel
            {
                SurfaceId = "s",
                Path = "/count",
                Value = JsonSerializer.SerializeToElement(1),
            }
        );
        dm.Apply(
            new UpdateDataModel
            {
                SurfaceId = "s",
                Path = "/count",
                Value = JsonSerializer.SerializeToElement(42),
            }
        );

        Assert.Equal("42", dm.Resolve(DynamicValue.FromPath("/count")));
    }

    [Fact]
    public void Resolve_LiteralValue_ReturnsLiteral()
    {
        var dm = new DataModel();
        Assert.Equal("Hello", dm.Resolve(DynamicValue.FromString("Hello")));
    }

    [Fact]
    public void Resolve_UnknownPath_ReturnsNull()
    {
        var dm = new DataModel();
        Assert.Null(dm.Resolve(DynamicValue.FromPath("/nonexistent/path")));
    }

    [Fact]
    public void Resolve_Null_ReturnsNull()
    {
        var dm = new DataModel();
        Assert.Null(dm.Resolve(null));
    }

    [Fact]
    public void Resolve_NumberLiteral_ReturnsStringRepresentation()
    {
        var dm = new DataModel();
        Assert.Equal("42.5", dm.Resolve(DynamicValue.FromNumber(42.5)));
    }

    [Fact]
    public void Resolve_BoolLiteral_ReturnsTrueOrFalse()
    {
        var dm = new DataModel();
        Assert.Equal("true", dm.Resolve(DynamicValue.FromBool(true)));
        Assert.Equal("false", dm.Resolve(DynamicValue.FromBool(false)));
    }

    [Fact]
    public void Resolve_NumericNodeInModel_ReturnsString()
    {
        var dm = new DataModel();
        dm.Apply(
            new UpdateDataModel
            {
                SurfaceId = "s",
                Path = "/count",
                Value = JsonSerializer.SerializeToElement(99),
            }
        );

        Assert.Equal("99", dm.Resolve(DynamicValue.FromPath("/count")));
    }

    [Fact]
    public void Resolve_BoolNodeInModel_ReturnsString()
    {
        var dm = new DataModel();
        dm.Apply(
            new UpdateDataModel
            {
                SurfaceId = "s",
                Path = "/active",
                Value = JsonSerializer.SerializeToElement(true),
            }
        );

        Assert.Equal("true", dm.Resolve(DynamicValue.FromPath("/active")));
    }

    [Fact]
    public void Resolve_ArrayIndex_ReturnsElement()
    {
        var dm = new DataModel();
        dm.Apply(
            new UpdateDataModel
            {
                SurfaceId = "s",
                Path = "/items",
                Value = JsonSerializer.SerializeToElement(new[] { "alpha", "beta", "gamma" }),
            }
        );

        Assert.Equal("beta", dm.Resolve(DynamicValue.FromPath("/items/1")));
    }

    [Fact]
    public void SetSnapshot_ReplacesEntireModel()
    {
        var dm = new DataModel();
        dm.Apply(
            new UpdateDataModel
            {
                SurfaceId = "s",
                Path = "/old",
                Value = JsonSerializer.SerializeToElement("data"),
            }
        );

        dm.SetSnapshot(JsonSerializer.SerializeToElement(new { fresh = "start" }));

        Assert.Null(dm.Resolve(DynamicValue.FromPath("/old")));
        Assert.Equal("start", dm.Resolve(DynamicValue.FromPath("/fresh")));
    }

    [Fact]
    public void Resolve_FunctionCall_ReturnsNull()
    {
        var dm = new DataModel();
        var fc = new DynamicValue.FunctionValue(new FunctionCallValue { Call = "formatDate" });

        Assert.Null(dm.Resolve(fc));
    }

    [Fact]
    public void Apply_ArrayIndexPath_CreatesArrayAndSetsElement()
    {
        var dm = new DataModel();
        dm.Apply(
            new UpdateDataModel
            {
                SurfaceId = "s",
                Path = "/items/0/name",
                Value = JsonSerializer.SerializeToElement("Alice"),
            }
        );

        Assert.Equal("Alice", dm.Resolve(DynamicValue.FromPath("/items/0/name")));
    }

    [Fact]
    public void Apply_ArrayIndexPath_MultipleElements()
    {
        var dm = new DataModel();
        dm.Apply(
            new UpdateDataModel
            {
                SurfaceId = "s",
                Path = "/items/0",
                Value = JsonSerializer.SerializeToElement(new { name = "Alice" }),
            }
        );
        dm.Apply(
            new UpdateDataModel
            {
                SurfaceId = "s",
                Path = "/items/1",
                Value = JsonSerializer.SerializeToElement(new { name = "Bob" }),
            }
        );

        Assert.Equal("Alice", dm.Resolve(DynamicValue.FromPath("/items/0/name")));
        Assert.Equal("Bob", dm.Resolve(DynamicValue.FromPath("/items/1/name")));
    }

    [Fact]
    public void Apply_ArrayIndexPath_PreservesExistingArray()
    {
        var dm = new DataModel();
        dm.SetSnapshot(JsonSerializer.SerializeToElement(new { items = new[] { new { name = "original" } } }));

        dm.Apply(
            new UpdateDataModel
            {
                SurfaceId = "s",
                Path = "/items/0/name",
                Value = JsonSerializer.SerializeToElement("updated"),
            }
        );

        Assert.Equal("updated", dm.Resolve(DynamicValue.FromPath("/items/0/name")));
    }

    [Fact]
    public void Apply_DeleteAtArrayPath_NullifiesElement()
    {
        var dm = new DataModel();
        dm.SetSnapshot(JsonSerializer.SerializeToElement(new { items = new[] { "alpha", "beta", "gamma" } }));

        dm.Apply(
            new UpdateDataModel
            {
                SurfaceId = "s",
                Path = "/items/1",
                Value = null,
            }
        );

        Assert.Null(dm.Resolve(DynamicValue.FromPath("/items/1")));
        Assert.Equal("gamma", dm.Resolve(DynamicValue.FromPath("/items/2")));
    }

    [Fact]
    public void Apply_PreservesExistingObjectWhenNextSegmentIsNumeric()
    {
        var dm = new DataModel();
        dm.SetSnapshot(
            JsonSerializer.SerializeToElement(new { totals = new Dictionary<string, int> { ["2025"] = 100 } })
        );

        dm.Apply(
            new UpdateDataModel
            {
                SurfaceId = "s",
                Path = "/totals/2026",
                Value = JsonSerializer.SerializeToElement(200),
            }
        );

        // Should preserve the object (not create an array) since "totals" already exists as object
        Assert.Equal("100", dm.Resolve(DynamicValue.FromPath("/totals/2025")));
        Assert.Equal("200", dm.Resolve(DynamicValue.FromPath("/totals/2026")));
    }

    [Fact]
    public void GetArrayLength_ReturnsCount_WhenPathIsArray()
    {
        var dm = new DataModel();
        dm.Apply(
            new UpdateDataModel
            {
                SurfaceId = "s",
                Path = "/",
                Value = JsonSerializer.SerializeToElement(new { items = new[] { 1, 2, 3 } }),
            }
        );
        Assert.Equal(3, dm.GetArrayLength("/items"));
    }

    [Fact]
    public void GetArrayLength_ReturnsNegativeOne_WhenPathIsObject()
    {
        var dm = new DataModel();
        dm.Apply(
            new UpdateDataModel
            {
                SurfaceId = "s",
                Path = "/",
                Value = JsonSerializer.SerializeToElement(new { data = new { key = "val" } }),
            }
        );
        Assert.Equal(-1, dm.GetArrayLength("/data"));
    }

    [Fact]
    public void GetArrayLength_ReturnsNegativeOne_WhenPathDoesNotExist()
    {
        var dm = new DataModel();
        Assert.Equal(-1, dm.GetArrayLength("/nonexistent"));
    }

    [Fact]
    public void GetArrayLength_ReturnsZero_WhenArrayIsEmpty()
    {
        var dm = new DataModel();
        dm.Apply(
            new UpdateDataModel
            {
                SurfaceId = "s",
                Path = "/",
                Value = JsonSerializer.SerializeToElement(new { items = Array.Empty<int>() }),
            }
        );
        Assert.Equal(0, dm.GetArrayLength("/items"));
    }

    [Fact]
    public void ToJson_EmptyModel_ReturnsEmptyObject()
    {
        var dm = new DataModel();
        Assert.Equal("{}", dm.ToJson());
    }

    [Fact]
    public void ToJson_AfterApply_ReflectsState()
    {
        var dm = new DataModel();
        dm.Apply(
            new UpdateDataModel
            {
                SurfaceId = "s",
                Path = "/name",
                Value = JsonSerializer.SerializeToElement("Alice"),
            }
        );

        var json = dm.ToJson();
        Assert.Contains("\"name\"", json);
        Assert.Contains("\"Alice\"", json);
    }

    [Fact]
    public void ToJson_Indented_ProducesFormattedOutput()
    {
        var dm = new DataModel();
        dm.Apply(
            new UpdateDataModel
            {
                SurfaceId = "s",
                Path = "/key",
                Value = JsonSerializer.SerializeToElement("val"),
            }
        );

        var json = dm.ToJson(indented: true);
        Assert.Contains("\n", json);
        Assert.Contains("  ", json);
    }

    // --- RFC 6901 JSON Pointer escaping tests ---

    [Fact]
    public void Apply_PathWithTilde1Escape_ResolvesSlashInKey()
    {
        var dm = new DataModel();
        // ~1 should unescape to / in the key name
        dm.Apply(
            new UpdateDataModel
            {
                SurfaceId = "s",
                Path = "/a~1b",
                Value = JsonSerializer.SerializeToElement("slash-key"),
            }
        );

        Assert.Equal("slash-key", dm.Resolve(DynamicValue.FromPath("/a~1b")));
    }

    [Fact]
    public void Apply_PathWithTilde0Escape_ResolvesTildeInKey()
    {
        var dm = new DataModel();
        // ~0 should unescape to ~ in the key name
        dm.Apply(
            new UpdateDataModel
            {
                SurfaceId = "s",
                Path = "/a~0b",
                Value = JsonSerializer.SerializeToElement("tilde-key"),
            }
        );

        Assert.Equal("tilde-key", dm.Resolve(DynamicValue.FromPath("/a~0b")));
    }

    [Fact]
    public void Apply_PathWithBothEscapes_ResolvesCorrectly()
    {
        var dm = new DataModel();
        // ~01 should become ~1 (tilde then 1), not /
        dm.Apply(
            new UpdateDataModel
            {
                SurfaceId = "s",
                Path = "/~01",
                Value = JsonSerializer.SerializeToElement("both"),
            }
        );

        Assert.Equal("both", dm.Resolve(DynamicValue.FromPath("/~01")));
    }

    [Fact]
    public void Apply_DeletePathWithEscapedSegments_RemovesKey()
    {
        var dm = new DataModel();
        dm.Apply(
            new UpdateDataModel
            {
                SurfaceId = "s",
                Path = "/a~1b",
                Value = JsonSerializer.SerializeToElement("to-delete"),
            }
        );

        dm.Apply(
            new UpdateDataModel
            {
                SurfaceId = "s",
                Path = "/a~1b",
                Value = null,
            }
        );

        Assert.Null(dm.Resolve(DynamicValue.FromPath("/a~1b")));
    }

    // --- RFC 6901 strict escape validation tests ---

    [Fact]
    public void Apply_PathWithInvalidEscape_ThrowsFormatException()
    {
        var dm = new DataModel();

        var ex = Assert.Throws<FormatException>(() =>
            dm.Apply(
                new UpdateDataModel
                {
                    SurfaceId = "s",
                    Path = "/~2invalid",
                    Value = JsonSerializer.SerializeToElement("nope"),
                }
            )
        );

        Assert.Contains("~2", ex.Message);
    }

    [Fact]
    public void Apply_PathWithTrailingTilde_ThrowsFormatException()
    {
        var dm = new DataModel();

        var ex = Assert.Throws<FormatException>(() =>
            dm.Apply(
                new UpdateDataModel
                {
                    SurfaceId = "s",
                    Path = "/trailing~",
                    Value = JsonSerializer.SerializeToElement("nope"),
                }
            )
        );

        Assert.Contains("trailing '~'", ex.Message);
    }

    [Fact]
    public void Resolve_PathWithInvalidEscape_ThrowsFormatException()
    {
        var dm = new DataModel();

        Assert.Throws<FormatException>(() => dm.Resolve(DynamicValue.FromPath("/~a")));
    }

    [Fact]
    public void Apply_TraverseThroughScalar_ThrowsJsonException()
    {
        // Seed: /user is a string primitive.
        var dm = new DataModel();
        dm.Apply(
            new UpdateDataModel
            {
                SurfaceId = "s",
                Path = "/user",
                Value = JsonSerializer.SerializeToElement("Alice"),
            }
        );

        // Attempting to write /user/name throws rather than overwriting "Alice" with a new object.
        var ex = Assert.Throws<JsonException>(() =>
            dm.Apply(
                new UpdateDataModel
                {
                    SurfaceId = "s",
                    Path = "/user/name",
                    Value = JsonSerializer.SerializeToElement("Bob"),
                }
            )
        );
        Assert.Contains("primitive", ex.Message);
        // Original scalar preserved (exception fired before any overwrite).
        Assert.Equal("Alice", dm.Resolve(DynamicValue.FromPath("/user")));
    }

    [Fact]
    public void SetSnapshot_NonObjectJsonElement_ThrowsJsonException()
    {
        var dm = new DataModel();
        // Seed some data that a silent wipe would erase.
        dm.Apply(
            new UpdateDataModel
            {
                SurfaceId = "s",
                Path = "/keep",
                Value = JsonSerializer.SerializeToElement("me"),
            }
        );

        // Arrays, numbers, strings, booleans, and null all fail fast.
        Assert.Throws<JsonException>(() => dm.SetSnapshot(JsonSerializer.SerializeToElement(new[] { 1, 2, 3 })));
        Assert.Throws<JsonException>(() => dm.SetSnapshot(JsonSerializer.SerializeToElement(42)));
        Assert.Throws<JsonException>(() => dm.SetSnapshot(JsonSerializer.SerializeToElement("bare-string")));

        // Previous state is preserved (exception fired before any mutation).
        Assert.Equal("me", dm.Resolve(DynamicValue.FromPath("/keep")));
    }

    [Fact]
    public void Apply_LeafWriteOnArrayWithNonNumericSegment_ThrowsJsonException()
    {
        // Seed: /items is a JsonArray.
        var dm = new DataModel();
        dm.Apply(
            new UpdateDataModel
            {
                SurfaceId = "s",
                Path = "/items",
                Value = JsonSerializer.SerializeToElement(new[] { "a", "b" }),
            }
        );

        // Leaf-writing a non-numeric key onto a JsonArray throws JsonException.
        var ex = Assert.Throws<JsonException>(() =>
            dm.Apply(
                new UpdateDataModel
                {
                    SurfaceId = "s",
                    Path = "/items/newKey",
                    Value = JsonSerializer.SerializeToElement("c"),
                }
            )
        );
        Assert.Contains("newKey", ex.Message);
    }

    // ── Scope-aware resolution (A2UI v0.9 template Child Scope) ─────────

    private static DataModel BuildTemplateFixture()
    {
        var dm = new DataModel();
        dm.Apply(
            new UpdateDataModel
            {
                SurfaceId = "s",
                Path = null,
                Value = JsonSerializer.SerializeToElement(
                    new
                    {
                        items = new object[]
                        {
                            new { name = "alpha", tags = new[] { "x", "y" } },
                            new { name = "beta", tags = new[] { "z" } },
                        },
                        header = "global",
                    }
                ),
            }
        );
        return dm;
    }

    [Fact]
    public void ResolveWithBasePath_RelativePath_PrependsBaseAndResolves()
    {
        var dm = BuildTemplateFixture();

        var resolved = dm.Resolve(DynamicValue.FromPath("name"), basePath: "/items/0");

        Assert.Equal("alpha", resolved);
    }

    [Fact]
    public void ResolveWithBasePath_AbsolutePath_IgnoresBase()
    {
        var dm = BuildTemplateFixture();

        var resolved = dm.Resolve(DynamicValue.FromPath("/header"), basePath: "/items/0");

        Assert.Equal("global", resolved);
    }

    [Fact]
    public void ResolveWithBasePath_NullBase_BehavesLikeNoScopeResolve()
    {
        var dm = BuildTemplateFixture();

        Assert.Equal("global", dm.Resolve(DynamicValue.FromPath("/header"), basePath: null));
        Assert.Equal(dm.Resolve(DynamicValue.FromPath("/header")), dm.Resolve(DynamicValue.FromPath("/header"), null));
    }

    [Fact]
    public void ResolveWithBasePath_EmptyBase_BehavesLikeNoScopeResolve()
    {
        var dm = BuildTemplateFixture();

        Assert.Equal("global", dm.Resolve(DynamicValue.FromPath("/header"), basePath: ""));
    }

    [Fact]
    public void ResolveWithBasePath_BaseWithTrailingSlash_NormalizesSingleSeparator()
    {
        var dm = BuildTemplateFixture();

        var resolved = dm.Resolve(DynamicValue.FromPath("name"), basePath: "/items/1/");

        Assert.Equal("beta", resolved);
    }

    [Fact]
    public void ResolveWithBasePath_RelativePathIntoArray_ResolvesWithIndex()
    {
        var dm = BuildTemplateFixture();

        var resolved = dm.Resolve(DynamicValue.FromPath("tags/0"), basePath: "/items/0");

        Assert.Equal("x", resolved);
    }

    [Fact]
    public void ResolveWithBasePath_StringLiteral_UnaffectedByScope()
    {
        var dm = BuildTemplateFixture();

        var resolved = dm.Resolve(DynamicValue.FromString("literal"), basePath: "/items/0");

        Assert.Equal("literal", resolved);
    }

    [Fact]
    public void ResolveWithBasePath_MissingRelativePath_ReturnsNull()
    {
        var dm = BuildTemplateFixture();

        var resolved = dm.Resolve(DynamicValue.FromPath("missing"), basePath: "/items/0");

        Assert.Null(resolved);
    }

    [Fact]
    public void ResolveWithBasePath_NullValue_ReturnsNull()
    {
        var dm = BuildTemplateFixture();

        Assert.Null(dm.Resolve(null, basePath: "/items/0"));
    }

    [Fact]
    public void ResolveWithBasePath_RelativeBase_Throws()
    {
        var dm = BuildTemplateFixture();

        // basePath must be an absolute JSON Pointer.
        var ex = Assert.Throws<ArgumentException>(() => dm.Resolve(DynamicValue.FromPath("name"), basePath: "items/0"));

        Assert.Equal("basePath", ex.ParamName);
        Assert.Contains("absolute JSON Pointer", ex.Message);
    }

    // ── Root reads & slash normalization (A-08 / conformance data_model.yaml) ──

    [Theory]
    [InlineData("/")]
    [InlineData("")]
    [InlineData("///")]
    public void Resolve_RootPaths_ReturnsRootSnapshotJson(string rootPath)
    {
        var dm = new DataModel();
        Assert.Equal("{}", dm.Resolve(DynamicValue.FromPath(rootPath)));

        dm.SetSnapshot(JsonSerializer.SerializeToElement(new { newRoot = "foo" }));
        Assert.Equal("{\"newRoot\":\"foo\"}", dm.Resolve(DynamicValue.FromPath(rootPath)));
    }

    [Fact]
    public void ApplyAndResolve_TrailingAndConsecutiveSlashes_NormalizeSegments()
    {
        var dm = new DataModel();

        dm.Apply("/foo/", JsonSerializer.SerializeToElement("bar"));
        Assert.Equal("bar", dm.Resolve(DynamicValue.FromPath("/foo")));
        Assert.Equal("bar", dm.Resolve(DynamicValue.FromPath("/foo/")));

        dm.Apply("//a///b//", JsonSerializer.SerializeToElement("val"));
        Assert.Equal("val", dm.Resolve(DynamicValue.FromPath("/a/b")));
        Assert.Equal("val", dm.Resolve(DynamicValue.FromPath("//a///b//")));

        dm.Delete("//a///b//");
        Assert.Null(dm.Resolve(DynamicValue.FromPath("/a/b")));
    }

    // ── RFC 6901 §4 array index grammar & MaxArrayIndex bounds (A-07) ──

    [Theory]
    [InlineData("/items/01")]
    [InlineData("/items/00")]
    [InlineData("/items/-1")]
    [InlineData("/items/+1")]
    [InlineData("/items/01/nested")]
    public void ApplyAndResolve_InvalidArrayIndexSyntax_AbsentOnReadAndThrowsOnWrite(string path)
    {
        var dm = new DataModel();
        dm.SetSnapshot(JsonSerializer.SerializeToElement(new { items = new[] { "a", "b", "c" } }));

        Assert.Null(dm.Resolve(DynamicValue.FromPath(path)));

        var ex = Assert.Throws<JsonException>(() => dm.Apply(path, JsonSerializer.SerializeToElement("invalid")));
        Assert.Contains("non-numeric segment", ex.Message);
    }

    [Fact]
    public void Apply_ArrayIndexAtAndAboveMaxBound_EnforcesMaxArrayIndex10000()
    {
        var dm = new DataModel();
        dm.SetSnapshot(JsonSerializer.SerializeToElement(new { items = new[] { "a", "b", "c" } }));

        dm.Apply("/items/10000", JsonSerializer.SerializeToElement("max_element"));
        Assert.Equal("max_element", dm.Resolve(DynamicValue.FromPath("/items/10000")));

        Assert.Throws<JsonException>(() => dm.Apply("/items/10001", JsonSerializer.SerializeToElement("x")));
        Assert.Throws<JsonException>(() => dm.Apply("/items/999999999", JsonSerializer.SerializeToElement("x")));
        Assert.Throws<JsonException>(() => dm.Apply("/items/10001/name", JsonSerializer.SerializeToElement("x")));
        Assert.Throws<JsonException>(() => dm.Apply("/newArray/10000000", JsonSerializer.SerializeToElement("x")));
        Assert.Throws<JsonException>(() => dm.Apply("/newArray/10000000/field", JsonSerializer.SerializeToElement("x")));
        Assert.Null(dm.Resolve(DynamicValue.FromPath("/newArray")));
    }

    [Fact]
    public void SurfaceManager_UpdateDataModelWithInvalidEscape_HandlesFormatException()
    {
        const string CreateJson = """
            {
                "version": "1.0",
                "createSurface": {
                    "surfaceId": "s1",
                    "components": [
                        { "component": "Text", "id": "root", "text": "Hello" }
                    ]
                }
            }
            """;
        const string InvalidEscapeUpdateJson = """
            {
                "version": "1.0",
                "updateDataModel": {
                    "surfaceId": "s1",
                    "path": "/~2invalid",
                    "value": "bad"
                }
            }
            """;

        var lenient = new SurfaceManager(options: new SurfaceManagerOptions { StrictMode = false });
        lenient.Process(CreateJson);
        lenient.Process(InvalidEscapeUpdateJson);

        var strict = new SurfaceManager(options: new SurfaceManagerOptions { StrictMode = true });
        strict.Process(CreateJson);
        var ex = Assert.Throws<A2UiMessageValidationException>(() => strict.Process(InvalidEscapeUpdateJson));
        Assert.IsType<FormatException>(ex.InnerException);
    }

    [Fact]
    public void ApplyAndSetSnapshot_WithDisposableJsonDocument_ResolvesAccuratelyAfterDispose()
    {
        var dm = new DataModel();

        using (var snapshotDoc = JsonDocument.Parse("""{"profile":{"name":"Ada"},"items":[10,20]}"""))
        {
            dm.SetSnapshot(snapshotDoc.RootElement);
        }

        using (var objectDoc = JsonDocument.Parse("""{"city":"London","active":true}"""))
        {
            dm.Apply("/profile/address", objectDoc.RootElement);
        }

        using (var arrayDoc = JsonDocument.Parse("""["alpha","beta"]"""))
        {
            dm.Apply("/tags", arrayDoc.RootElement);
        }

        using (var scalarDoc = JsonDocument.Parse("""42"""))
        {
            dm.Apply("/count", scalarDoc.RootElement);
        }

        // All source JsonDocuments are now disposed; pooled buffers must not back DataModel nodes.
        Assert.Equal("Ada", dm.Resolve(DynamicValue.FromPath("/profile/name")));
        Assert.Equal("London", dm.Resolve(DynamicValue.FromPath("/profile/address/city")));
        Assert.Equal("true", dm.Resolve(DynamicValue.FromPath("/profile/address/active")));
        Assert.Equal(2, dm.GetArrayLength("/tags"));
        Assert.Equal("beta", dm.Resolve(DynamicValue.FromPath("/tags/1")));
        Assert.Equal("42", dm.Resolve(DynamicValue.FromPath("/count")));
    }
}
