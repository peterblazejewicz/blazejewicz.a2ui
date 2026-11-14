using System.Collections.Generic;
using System.Text.Json;
using Blazejewicz.A2Ui.Core.Children;
using Blazejewicz.A2Ui.Core.Components;
using Blazejewicz.A2Ui.Core.Messages;
using Blazejewicz.A2Ui.Core.Validation;

namespace Blazejewicz.A2Ui.Core.Tests;

public sealed class V10ComponentConverterAndValidationTests
{
    private static readonly JsonSerializerOptions s_opts = new(JsonSerializerDefaults.Web)
    {
        AllowOutOfOrderMetadataProperties = true,
    };

    [Fact]
    public void A2UiComponentConverter_DeserializesUnknownToGenericComponent()
    {
        const string Json = """
            {
                "component": "CustomGauge",
                "id": "gauge_1",
                "catalogId": "https://example.com/charts.json",
                "weight": 2.5,
                "min": 0,
                "max": 100,
                "current": 42
            }
            """;

        var comp = JsonSerializer.Deserialize<A2UiComponent>(Json, s_opts);

        Assert.NotNull(comp);
        Assert.IsType<GenericComponent>(comp);
        var gc = (GenericComponent)comp;
        Assert.Equal("CustomGauge", gc.Component);
        Assert.Equal("CustomGauge", gc.ComponentType);
        Assert.Equal("gauge_1", gc.Id);
        Assert.Equal("https://example.com/charts.json", gc.CatalogId);
        Assert.Equal(2.5, gc.Weight);
        Assert.True(gc.Properties.ContainsKey("min"));
        Assert.Equal(42, gc.Properties["current"].GetInt32());

        // Round-trip serialization
        var roundTripJson = JsonSerializer.Serialize<A2UiComponent>(gc, s_opts);
        var roundTripComp = JsonSerializer.Deserialize<A2UiComponent>(roundTripJson, s_opts);
        Assert.IsType<GenericComponent>(roundTripComp);
        var roundTripGc = (GenericComponent)roundTripComp;
        Assert.Equal("CustomGauge", roundTripGc.ComponentType);
        Assert.Equal(42, roundTripGc.Properties["current"].GetInt32());
    }

    [Fact]
    public void Uax31Validator_ValidatesIdentifier()
    {
        Assert.True(Uax31Validator.IsValidIdentifier("validName"));
        Assert.True(Uax31Validator.IsValidIdentifier("_privateVar"));
        Assert.True(Uax31Validator.IsValidIdentifier("component123"));
        Assert.True(Uax31Validator.IsValidIdentifier("日本語"));

        Assert.False(Uax31Validator.IsValidIdentifier(""));
        Assert.False(Uax31Validator.IsValidIdentifier("123num"));
        Assert.False(Uax31Validator.IsValidIdentifier("kebab-case"));
        Assert.False(Uax31Validator.IsValidIdentifier("has space"));
        Assert.False(Uax31Validator.IsValidIdentifier("dot.name"));
    }

    [Fact]
    public void Uax31Validator_RejectsSurfaceComponentName()
    {
        Uax31Validator.ValidateComponentName("Button");
        Uax31Validator.ValidateComponentName("CustomCard");

        var ex = Assert.Throws<A2UiMessageValidationException>(() => Uax31Validator.ValidateComponentName("Surface"));
        Assert.Contains("reserved", ex.Message);
    }

    [Fact]
    public void Uax31Validator_RejectsA2UiExtensionPrefix()
    {
        Uax31Validator.ValidateExtensionKey("vendor_custom");
        Uax31Validator.ValidateExtensionKey("my_ext");

        var ex = Assert.Throws<A2UiMessageValidationException>(() =>
            Uax31Validator.ValidateExtensionKey("a2ui_custom")
        );
        Assert.Contains("reserved", ex.Message);

        var ex2 = Assert.Throws<A2UiMessageValidationException>(() =>
            Uax31Validator.ValidateExtensionKey("a2ui_version")
        );
        Assert.Contains("reserved", ex2.Message);
    }

    [Fact]
    public void CompositionValidator_ValidatesRootVirtualSurfaceParent()
    {
        var schema = new CatalogSchemaDefinition
        {
            CatalogId = "test",
            Components = new Dictionary<string, CatalogComponentConstraint>
            {
                ["RestrictedRoot"] = new()
                {
                    AllowedParents = ["Card"], // Doesn't include "Surface"
                },
            },
        };

        var components = new List<A2UiComponent>
        {
            new GenericComponent { ComponentType = "RestrictedRoot", Id = "root" },
        };

        var errors = CompositionValidator.Validate("surface_1", components, schema.Components);

        Assert.Single(errors);
        Assert.Equal("UNALLOWED_PARENT", errors[0].Code);
        Assert.Equal("surface_1", errors[0].SurfaceId);
        Assert.Equal("/components/0", errors[0].Path);
    }

    [Fact]
    public void CompositionValidator_ValidatesParentChildHierarchy()
    {
        var schema = new CatalogSchemaDefinition
        {
            CatalogId = "test",
            Components = new Dictionary<string, CatalogComponentConstraint>
            {
                ["Container"] = new() { AllowedParents = ["Surface"], AllowedChildren = ["ValidChild"] },
                ["InvalidChild"] = new() { AllowedParents = ["Container"] },
            },
        };

        var components = new List<A2UiComponent>
        {
            new GenericComponent
            {
                ComponentType = "Container",
                Id = "root",
                Children = ChildList.FromIds(["child_1"]),
            },
            new GenericComponent
            {
                ComponentType = "InvalidChild",
                Id = "child_1",
                Parent = "root",
            },
        };

        var errors = CompositionValidator.Validate("surface_1", components, schema.Components);

        Assert.Single(errors);
        Assert.Equal("UNALLOWED_CHILD", errors[0].Code);
        Assert.Equal("surface_1", errors[0].SurfaceId);
    }
}
