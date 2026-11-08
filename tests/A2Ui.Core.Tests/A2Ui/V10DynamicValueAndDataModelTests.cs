using System.Collections.Generic;
using System.Text.Json;
using Blazejewicz.A2Ui.Core.Bindings;
using Blazejewicz.A2Ui.Core.Surfaces;

namespace Blazejewicz.A2Ui.Core.Tests.A2Ui;

public sealed class V10DynamicValueAndDataModelTests
{
    private static readonly JsonSerializerOptions s_opts = new(JsonSerializerDefaults.Web)
    {
        AllowOutOfOrderMetadataProperties = true,
    };

    [Fact]
    public void DynamicValue_ObjectValue_UnionCasesAndMethods()
    {
        var dict = new Dictionary<string, object?>
        {
            ["name"] = "Alice",
            ["age"] = 30.0,
            ["active"] = true,
        };

        var dv = DynamicValue.FromObject(dict);

        Assert.True(dv.IsObject);
        Assert.False(dv.IsString);
        Assert.False(dv.IsNumber);
        Assert.False(dv.IsBool);
        Assert.False(dv.IsPath);
        Assert.False(dv.IsFunction);

        Assert.NotNull(dv.AsObject);
        Assert.Equal("Alice", dv.AsObject!.Value["name"]);

        string matched = dv.Match(
            onString: s => "str",
            onNumber: n => "num",
            onBool: b => "bool",
            onArray: a => "arr",
            onPath: p => "path",
            onFunction: fc => "call",
            onObject: o => $"obj:{o.Value.Count}"
        );
        Assert.Equal("obj:3", matched);

        bool switched = false;
        dv.Switch(
            onString: _ => { },
            onNumber: _ => { },
            onBool: _ => { },
            onArray: _ => { },
            onPath: _ => { },
            onFunction: _ => { },
            onObject: o => switched = o.Value.ContainsKey("name")
        );
        Assert.True(switched);
    }

    [Fact]
    public void DynamicValue_AtBinding_Deserialization()
    {
        using var scope = DynamicValueConverter.EnterVersionScope(A2UiProtocol.Version10);

        const string Json = """{"@path": "/profile/username"}""";
        var dv = JsonSerializer.Deserialize<DynamicValue>(Json, s_opts)!;

        Assert.True(dv.IsPath);
        Assert.Equal("/profile/username", dv.AsPath!.DataPath);

        // Round-trip writes back with @path in v1.0 mode
        var roundTripJson = JsonSerializer.Serialize(dv, s_opts);
        Assert.Contains("\"@path\"", roundTripJson);
    }

    [Fact]
    public void DynamicValue_AtBinding_RejectsExtraProperties()
    {
        using var scope = DynamicValueConverter.EnterVersionScope(A2UiProtocol.Version10);

        const string Json = """{"@path": "/profile/username", "extra": "invalid"}""";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DynamicValue>(Json, s_opts));
    }

    [Fact]
    public void DynamicValue_AtCall_Deserialization()
    {
        using var scope = DynamicValueConverter.EnterVersionScope(A2UiProtocol.Version10);

        const string Json = """
            {
                "@call": "formatCurrency",
                "catalogId": "https://example.com/finance.json",
                "returnType": "string",
                "args": {
                    "amount": 100,
                    "currency": "USD"
                }
            }
            """;

        var dv = JsonSerializer.Deserialize<DynamicValue>(Json, s_opts)!;

        Assert.True(dv.IsFunction);
        var fc = dv.AsFunction!.Call;
        Assert.Equal("formatCurrency", fc.Call);
        Assert.Equal("https://example.com/finance.json", fc.CatalogId);
        Assert.Equal("string", fc.ReturnType);
        Assert.NotNull(fc.Args);
        Assert.True(fc.Args!.ContainsKey("amount"));

        // Round trip
        var roundTripJson = JsonSerializer.Serialize(dv, s_opts);
        Assert.Contains("\"@call\"", roundTripJson);
    }

    [Fact]
    public void DynamicValue_V10PlainObject_DeserializesToObjectValue()
    {
        using var scope = DynamicValueConverter.EnterVersionScope(A2UiProtocol.Version10);

        const string Json = """
            {
                "title": "Welcome",
                "count": 5,
                "nested": { "enabled": true }
            }
            """;

        var dv = JsonSerializer.Deserialize<DynamicValue>(Json, s_opts)!;

        Assert.True(dv.IsObject);
        var dict = dv.AsObject!.Value;
        Assert.Equal("Welcome", dict["title"]);
        Assert.Equal(5L, dict["count"]);
        Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(dict["nested"]);
    }

    [Fact]
    public void DynamicValue_V10PlainObject_DoubledAtUnescapes()
    {
        using var scope = DynamicValueConverter.EnterVersionScope(A2UiProtocol.Version10);

        const string Json = """
            {
                "@@type": "CustomType",
                "@@": "single_at"
            }
            """;

        var dv = JsonSerializer.Deserialize<DynamicValue>(Json, s_opts)!;

        Assert.True(dv.IsObject);
        var dict = dv.AsObject!.Value;
        Assert.True(dict.ContainsKey("@type"));
        Assert.Equal("CustomType", dict["@type"]);
        Assert.True(dict.ContainsKey("@"));
        Assert.Equal("single_at", dict["@"]);
    }

    [Fact]
    public void DynamicValue_V10PlainObject_SingleAtThrowsJsonException()
    {
        using var scope = DynamicValueConverter.EnterVersionScope(A2UiProtocol.Version10);

        const string Json = """
            {
                "@if": true,
                "foo": "bar"
            }
            """;

        var ex = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DynamicValue>(Json, s_opts));
        Assert.Contains("single '@' prefix is reserved", ex.Message);
    }

    [Fact]
    public void DataModel_Delete_RemovesObjectProperty()
    {
        var dm = new DataModel();
        dm.SetSnapshot(JsonSerializer.SerializeToElement(new { user = new { name = "Bob", role = "Admin" } }));

        dm.Delete("/user/role");

        Assert.Equal("Bob", dm.Resolve(DynamicValue.FromPath("/user/name")));
        Assert.Null(dm.Resolve(DynamicValue.FromPath("/user/role")));
    }

    [Fact]
    public void DataModel_Delete_RemovesArrayElementAndShifts()
    {
        var dm = new DataModel();
        dm.SetSnapshot(JsonSerializer.SerializeToElement(new { items = new[] { "a", "b", "c" } }));

        dm.Delete("/items/1"); // removes "b"

        Assert.Equal("a", dm.Resolve(DynamicValue.FromPath("/items/0")));
        Assert.Equal("c", dm.Resolve(DynamicValue.FromPath("/items/1")));
        Assert.Null(dm.Resolve(DynamicValue.FromPath("/items/2")));
    }

    [Fact]
    public void DataModel_Delete_RootClearsModel()
    {
        var dm = new DataModel();
        dm.SetSnapshot(JsonSerializer.SerializeToElement(new { name = "Bob" }));

        dm.Delete("/");
        Assert.Empty(dm.TopLevelKeys);
    }

    [Fact]
    public void DataModel_Apply_DeleteOnNullTrue_RemovesArrayElement()
    {
        var dm = new DataModel();
        dm.SetSnapshot(JsonSerializer.SerializeToElement(new { items = new[] { "x", "y", "z" } }));

        dm.Apply("/items/0", null, deleteOnNull: true);

        Assert.Equal("y", dm.Resolve(DynamicValue.FromPath("/items/0")));
        Assert.Equal("z", dm.Resolve(DynamicValue.FromPath("/items/1")));
        Assert.Null(dm.Resolve(DynamicValue.FromPath("/items/2")));
    }
}
