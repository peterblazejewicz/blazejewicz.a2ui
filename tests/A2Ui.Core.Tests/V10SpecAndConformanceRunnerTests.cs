using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Blazejewicz.A2Ui.Core.Actions;
using Blazejewicz.A2Ui.Core.Bindings;
using Blazejewicz.A2Ui.Core.Components;
using Blazejewicz.A2Ui.Core.Functions;
using Blazejewicz.A2Ui.Core.Messages;
using Blazejewicz.A2Ui.Core.Surfaces;
using Blazejewicz.A2Ui.Core.Versioning;
using YamlDotNet.RepresentationModel;

namespace Blazejewicz.A2Ui.Core.Tests;

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

    private static List<JsonElement> LoadConformanceYamlCases(string fileName)
    {
        string path = Path.Combine(FindRepoRoot(), "conformance", "core", fileName);
        var yaml = new YamlStream();
        using var reader = new StringReader(File.ReadAllText(path));
        yaml.Load(reader);

        var rootSeq = (YamlSequenceNode)yaml.Documents[0].RootNode;
        var cases = new List<JsonElement>(rootSeq.Children.Count);
        foreach (YamlNode item in rootSeq.Children)
        {
            cases.Add(YamlNodeToJsonElement(item));
        }

        return cases;
    }

    private static JsonElement YamlNodeToJsonElement(YamlNode node)
    {
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            WriteYamlNode(writer, node);
        }

        using var doc = JsonDocument.Parse(ms.ToArray());
        return doc.RootElement.Clone();
    }

    private static void WriteYamlNode(Utf8JsonWriter writer, YamlNode node)
    {
        switch (node)
        {
            case YamlScalarNode scalar:
                WriteYamlScalar(writer, scalar);
                break;

            case YamlSequenceNode seq:
                writer.WriteStartArray();
                foreach (YamlNode child in seq.Children)
                {
                    WriteYamlNode(writer, child);
                }

                writer.WriteEndArray();
                break;

            case YamlMappingNode map:
                writer.WriteStartObject();
                foreach (KeyValuePair<YamlNode, YamlNode> kvp in map.Children)
                {
                    string key = ((YamlScalarNode)kvp.Key).Value ?? "";
                    writer.WritePropertyName(key);
                    WriteYamlNode(writer, kvp.Value);
                }

                writer.WriteEndObject();
                break;
        }
    }

    private static void WriteYamlScalar(Utf8JsonWriter writer, YamlScalarNode scalar)
    {
        if (
            scalar.Style
            is YamlDotNet.Core.ScalarStyle.SingleQuoted
                or YamlDotNet.Core.ScalarStyle.DoubleQuoted
                or YamlDotNet.Core.ScalarStyle.Literal
                or YamlDotNet.Core.ScalarStyle.Folded
        )
        {
            writer.WriteStringValue(scalar.Value ?? "");
            return;
        }

        string? val = scalar.Value;
        if (val is null or "" or "null" or "Null" or "NULL" or "~")
        {
            writer.WriteNullValue();
        }
        else if (val is "true" or "True" or "TRUE")
        {
            writer.WriteBooleanValue(true);
        }
        else if (val is "false" or "False" or "FALSE")
        {
            writer.WriteBooleanValue(false);
        }
        else if (long.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l))
        {
            writer.WriteNumberValue(l);
        }
        else if (
            double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
            && !double.IsInfinity(d)
            && !double.IsNaN(d)
        )
        {
            writer.WriteNumberValue(d);
        }
        else
        {
            writer.WriteStringValue(val);
        }
    }

    public static TheoryData<string, bool, string> DynamicValueValidationCases() =>
        LoadSpecJsonTestCases("dynamic_value_validation.json");

    public static TheoryData<string, bool, string> InitialStateValidationCases() =>
        LoadSpecJsonTestCases("initial_state_validation.json");

    public static TheoryData<string, string> ExpressionsYamlCases() => LoadYamlTheoryData("expressions.yaml");

    public static TheoryData<string, string> FunctionsYamlCases() => LoadYamlTheoryData("functions.yaml");

    public static TheoryData<string, string> ReservedKeysYamlCases() => LoadYamlTheoryData("reserved_keys.yaml");

    public static TheoryData<string, string> DataDeletionYamlCases() => LoadYamlTheoryData("data_deletion.yaml");

    public static TheoryData<string, string> DataModelPointersYamlCases() =>
        LoadYamlTheoryData("data_model_pointers.yaml");

    public static TheoryData<string, string> ActionsYamlCases() =>
        LoadYamlTheoryData("actions.yaml");

    private static TheoryData<string, string> LoadYamlTheoryData(string fileName)
    {
        var data = new TheoryData<string, string>();
        foreach (JsonElement testCase in LoadConformanceYamlCases(fileName))
        {
            string name = testCase.GetProperty("name").GetString()!;
            data.Add(name, testCase.GetRawText());
        }

        return data;
    }

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

    [Theory]
    [MemberData(nameof(ExpressionsYamlCases))]
    public void ConformanceYaml_Expressions_MatchesExpectedOutcome(string name, string testCaseJson)
    {
        _ = name;
        using var doc = JsonDocument.Parse(testCaseJson);
        JsonElement tc = doc.RootElement;
        string action = tc.GetProperty("action").GetString()!;

        if (action == "parse_expression_template")
        {
            var parser = new ExpressionParser();
            string input = tc.GetProperty("input").GetString()!;

            if (tc.TryGetProperty("expect_error", out JsonElement expectErr))
            {
                var ex = Assert.Throws<A2UiExpressionException>(() => parser.Parse(input));
                if (expectErr.TryGetProperty("message", out JsonElement msgEl))
                {
                    Assert.Contains(msgEl.GetString()!, ex.Message, StringComparison.OrdinalIgnoreCase);
                }

                return;
            }

            IReadOnlyList<ExpressionToken> actualTokens = parser.Parse(input);
            JsonElement expectedParts = tc.GetProperty("expect");
            Assert.Equal(expectedParts.GetArrayLength(), actualTokens.Count);

            int idx = 0;
            foreach (JsonElement expectedPart in expectedParts.EnumerateArray())
            {
                AssertExpressionTokenMatches(expectedPart, actualTokens[idx]);
                idx++;
            }
        }
        else if (action == "validate")
        {
            ExecuteValidateOrProcessCase(tc);
        }
    }

    [Theory]
    [MemberData(nameof(FunctionsYamlCases))]
    public void ConformanceYaml_Functions_MatchesExpectedOutcome(string name, string testCaseJson)
    {
        _ = name;
        using var doc = JsonDocument.Parse(testCaseJson);
        JsonElement tc = doc.RootElement;
        string action = tc.GetProperty("action").GetString()!;

        if (action == "evaluate_function")
        {
            ExecuteEvaluateFunctionCase(tc);
        }
        else if (action == "validate")
        {
            ExecuteValidateOrProcessCase(tc);
        }
    }

    [Theory]
    [MemberData(nameof(ReservedKeysYamlCases))]
    public void ConformanceYaml_ReservedKeys_MatchesExpectedOutcome(string name, string testCaseJson)
    {
        _ = name;
        using var doc = JsonDocument.Parse(testCaseJson);
        ExecuteValidateOrProcessCase(doc.RootElement);
    }

    [Theory]
    [MemberData(nameof(DataDeletionYamlCases))]
    public void ConformanceYaml_DataDeletion_MatchesExpectedOutcome(string name, string testCaseJson)
    {
        _ = name;
        using var doc = JsonDocument.Parse(testCaseJson);
        ExecuteValidateOrProcessCase(doc.RootElement);
    }

    [Theory]
    [MemberData(nameof(DataModelPointersYamlCases))]
    public void ConformanceYaml_DataModelPointers_MatchesExpectedOutcome(string name, string testCaseJson)
    {
        _ = name;
        using var doc = JsonDocument.Parse(testCaseJson);
        ExecuteValidateOrProcessCase(doc.RootElement);
    }

    [Theory]
    [MemberData(nameof(ActionsYamlCases))]
    public void ConformanceYaml_Actions_MatchesExpectedOutcome(string name, string testCaseJson)
    {
        _ = name;
        using var doc = JsonDocument.Parse(testCaseJson);
        ExecuteDispatchActionCase(doc.RootElement);
    }

    private static void AssertExpressionTokenMatches(JsonElement expected, ExpressionToken actual)
    {
        switch (expected.ValueKind)
        {
            case JsonValueKind.String:
                var lit = Assert.IsType<LiteralToken>(actual);
                Assert.Equal(expected.GetString(), lit.Value);
                break;

            case JsonValueKind.Number:
                var num = Assert.IsType<NumberToken>(actual);
                Assert.Equal(expected.GetDouble(), num.Value);
                break;

            case JsonValueKind.True:
            case JsonValueKind.False:
                var b = Assert.IsType<BoolToken>(actual);
                Assert.Equal(expected.GetBoolean(), b.Value);
                break;

            case JsonValueKind.Object:
                if (expected.TryGetProperty("path", out JsonElement pathEl))
                {
                    var p = Assert.IsType<PathToken>(actual);
                    Assert.Equal(pathEl.GetString(), p.Path);
                }
                else if (expected.TryGetProperty("call", out JsonElement callEl))
                {
                    var fc = Assert.IsType<FunctionCallToken>(actual);
                    Assert.Equal(callEl.GetString(), fc.Name);
                    JsonElement expectedArgs = expected.GetProperty("args");
                    int expectedArgCount = expectedArgs.EnumerateObject().Count();
                    Assert.Equal(expectedArgCount, fc.Args.Count);
                    foreach (JsonProperty argProp in expectedArgs.EnumerateObject())
                    {
                        Assert.True(fc.Args.TryGetValue(argProp.Name, out ExpressionToken? actualArg));
                        AssertExpressionTokenMatches(argProp.Value, actualArg!);
                    }
                }

                break;
        }
    }

    private static void ExecuteEvaluateFunctionCase(JsonElement tc)
    {
        string funcName = tc.GetProperty("function").GetString()!;
        string protocolVersion = tc.TryGetProperty("protocolVersion", out JsonElement pvEl)
            ? pvEl.GetString()!
            : A2UiProtocol.Version10;

        var dataModel = new DataModel();
        if (tc.TryGetProperty("dataModel", out JsonElement dmEl) && dmEl.ValueKind == JsonValueKind.Object)
        {
            dataModel.SetSnapshot(dmEl);
        }

        JsonElement? expectError = null;
        if (tc.TryGetProperty("expectError", out JsonElement ee1))
        {
            expectError = ee1;
        }
        else if (tc.TryGetProperty("expect_error", out JsonElement ee2))
        {
            expectError = ee2;
        }

        // Handle surface catalog routing cases
        if (tc.TryGetProperty("surface", out JsonElement surfaceEl))
        {
            string? surfaceCatalogId = surfaceEl.TryGetProperty("catalogId", out JsonElement scEl)
                ? scEl.GetString()
                : null;
            string? callCatalogId = tc.TryGetProperty("catalogId", out JsonElement ccEl) ? ccEl.GetString() : null;

            if (expectError is { } err)
            {
                var ex = Assert.ThrowsAny<Exception>(() =>
                    EvaluateSurfaceCatalogFunction(
                        tc,
                        funcName,
                        protocolVersion,
                        surfaceCatalogId,
                        callCatalogId,
                        dataModel
                    )
                );
                if (err.TryGetProperty("message", out JsonElement msgEl))
                {
                    Assert.Contains(msgEl.GetString()!, ex.Message, StringComparison.OrdinalIgnoreCase);
                }

                return;
            }

            string? surfaceResult = EvaluateSurfaceCatalogFunction(
                tc,
                funcName,
                protocolVersion,
                surfaceCatalogId,
                callCatalogId,
                dataModel
            );
            AssertFunctionResultMatches(tc.GetProperty("expect"), surfaceResult);
            return;
        }

        var args = ExtractFunctionArgs(tc.GetProperty("args"), dataModel);
        if (expectError is { } expectedErr)
        {
            var ex = Assert.Throws<A2UiEvaluationException>(() => EvaluateCoreFunction(funcName, args, dataModel));
            if (expectedErr.TryGetProperty("message", out JsonElement msgEl))
            {
                Assert.Contains(msgEl.GetString()!, ex.Message, StringComparison.OrdinalIgnoreCase);
            }

            return;
        }

        string? result = EvaluateCoreFunction(funcName, args, dataModel);
        AssertFunctionResultMatches(tc.GetProperty("expect"), result);
    }

    private static string? EvaluateSurfaceCatalogFunction(
        JsonElement tc,
        string funcName,
        string protocolVersion,
        string? surfaceCatalogId,
        string? callCatalogId,
        DataModel dataModel
    )
    {
        if (protocolVersion == A2UiProtocol.Version10)
        {
            string? effectiveCatalog = callCatalogId ?? surfaceCatalogId;
            if (string.IsNullOrEmpty(effectiveCatalog))
            {
                throw new A2UiMessageValidationException("Surface has no default catalogId.");
            }

            if (
                effectiveCatalog
                is not (
                    A2UiProtocol.BasicCatalogIdV10
                    or "https://a2ui.org/specification/v1_0/catalogs/basic/catalog.json"
                    or "basic"
                    or "conformance/custom"
                    or "https://a2ui.org/catalogs/v10/allowed_callers"
                )
            )
            {
                throw new A2UiMessageValidationException($"Catalog not found: {effectiveCatalog}");
            }

            if (effectiveCatalog == "https://a2ui.org/catalogs/v10/allowed_callers" && funcName == "readSessionToken")
            {
                throw new A2UiEvaluationException("Function 'readSessionToken' cannot be called by renderer.");
            }
        }

        var args = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (tc.TryGetProperty("args", out JsonElement argsEl) && argsEl.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty prop in argsEl.EnumerateObject())
            {
                args[prop.Name] = ResolveArgValueOnSurface(
                    prop.Value,
                    protocolVersion,
                    surfaceCatalogId,
                    dataModel
                );
            }
        }

        return EvaluateCoreFunction(funcName, args, dataModel);
    }

    private static string? ResolveArgValueOnSurface(
        JsonElement val,
        string protocolVersion,
        string? surfaceCatalogId,
        DataModel dataModel
    )
    {
        if (val.ValueKind == JsonValueKind.Object)
        {
            if (val.TryGetProperty("@path", out JsonElement pEl))
            {
                return dataModel.Resolve(DynamicValue.FromPath(pEl.GetString()!));
            }

            if (val.TryGetProperty("@call", out JsonElement cEl))
            {
                string nestedFunc = cEl.GetString()!;
                string? nestedCat = val.TryGetProperty("catalogId", out JsonElement ncEl) ? ncEl.GetString() : null;
                return EvaluateSurfaceCatalogFunction(
                    val,
                    nestedFunc,
                    protocolVersion,
                    surfaceCatalogId,
                    nestedCat,
                    dataModel
                );
            }
        }

        if (val.ValueKind == JsonValueKind.Array)
        {
            var resolvedItems = new List<object?>();
            foreach (JsonElement item in val.EnumerateArray())
            {
                string? r = ResolveArgValueOnSurface(item, protocolVersion, surfaceCatalogId, dataModel);
                if (r is "true" or "false")
                {
                    resolvedItems.Add(r == "true");
                }
                else
                {
                    resolvedItems.Add(r);
                }
            }

            return JsonSerializer.Serialize(resolvedItems);
        }

        return JsonElementToArgString(val);
    }

    private static Dictionary<string, string?> ExtractFunctionArgs(JsonElement argsEl, DataModel dataModel)
    {
        var dict = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (argsEl.ValueKind != JsonValueKind.Object)
        {
            return dict;
        }

        foreach (JsonProperty prop in argsEl.EnumerateObject())
        {
            dict[prop.Name] = JsonElementToArgString(prop.Value);
        }

        _ = dataModel;
        return dict;
    }

    private static string? JsonElementToArgString(JsonElement el) =>
        el.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => el.GetString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => el.GetRawText(),
            JsonValueKind.Array or JsonValueKind.Object => el.GetRawText(),
            _ => el.GetRawText(),
        };

    private static string? EvaluateCoreFunction(
        string funcName,
        Dictionary<string, string?> args,
        DataModel dataModel
    )
    {
        if (funcName == "formatString")
        {
            string template = args.TryGetValue("value", out string? v) && v is not null ? v : "";
            var parser = new ExpressionParser();
            var registry = FunctionRegistry.CreateDefault();
            IReadOnlyList<ExpressionToken> tokens = parser.Parse(template);
            var sb = new StringBuilder();
            foreach (ExpressionToken token in tokens)
            {
                sb.Append(EvaluateExpressionToken(token, dataModel, registry));
            }

            return sb.ToString();
        }

        var reg = FunctionRegistry.CreateDefault();
        return reg.Evaluate(
            funcName,
            args,
            new FunctionEvaluationContext { CallerRole = FunctionCallerRole.RendererAction }
        );
    }

    private static string EvaluateExpressionToken(
        ExpressionToken token,
        DataModel dataModel,
        IFunctionRegistry registry
    ) =>
        token switch
        {
            LiteralToken lit => lit.Value,
            BoolToken b => b.Value ? "true" : "false",
            NumberToken n => n.Value.ToString(CultureInfo.InvariantCulture),
            PathToken p => dataModel.Resolve(DynamicValue.FromPath(p.Path)) ?? "",
            FunctionCallToken fc => EvaluateTokenFunctionCall(fc, dataModel, registry),
            _ => "",
        };

    private static string EvaluateTokenFunctionCall(
        FunctionCallToken fc,
        DataModel dataModel,
        IFunctionRegistry registry
    )
    {
        var evalArgs = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, ExpressionToken> kvp in fc.Args)
        {
            evalArgs[kvp.Key] = EvaluateExpressionToken(kvp.Value, dataModel, registry);
        }

        return registry.Evaluate(fc.Name, evalArgs) ?? "";
    }

    private static void AssertFunctionResultMatches(JsonElement expected, string? actual)
    {
        switch (expected.ValueKind)
        {
            case JsonValueKind.Null:
                Assert.Null(actual);
                break;

            case JsonValueKind.String:
                Assert.Equal(expected.GetString(), actual);
                break;

            case JsonValueKind.True:
                Assert.Equal("true", actual);
                break;

            case JsonValueKind.False:
                Assert.Equal("false", actual);
                break;

            case JsonValueKind.Number:
                Assert.NotNull(actual);
                Assert.Equal(
                    expected.GetDouble(),
                    double.Parse(actual!, CultureInfo.InvariantCulture)
                );
                break;

            case JsonValueKind.Object:
                if (expected.TryGetProperty("valid", out JsonElement validEl))
                {
                    Assert.Equal(validEl.GetBoolean() ? "true" : "false", actual);
                }

                break;
        }
    }

    private static void ExecuteValidateOrProcessCase(JsonElement tc)
    {
        var sm = new SurfaceManager(options: new SurfaceManagerOptions { StrictMode = true });

        JsonElement? topExpectError = null;
        if (tc.TryGetProperty("expectError", out JsonElement tee1))
        {
            topExpectError = tee1;
        }
        else if (tc.TryGetProperty("expect_error", out JsonElement tee2))
        {
            topExpectError = tee2;
        }

        if (tc.TryGetProperty("steps", out JsonElement stepsEl))
        {
            foreach (JsonElement step in stepsEl.EnumerateArray())
            {
                JsonElement payload = step.GetProperty("payload");
                if (step.TryGetProperty("expectError", out JsonElement stepErr))
                {
                    var ex = Assert.ThrowsAny<Exception>(() => ProcessPayloadBatch(sm, payload));
                    if (stepErr.TryGetProperty("message", out JsonElement msgEl))
                    {
                        Assert.Contains(msgEl.GetString()!, ex.Message, StringComparison.OrdinalIgnoreCase);
                    }

                    return;
                }

                ProcessPayloadBatch(sm, payload);
            }
        }
        else if (tc.TryGetProperty("payload", out JsonElement payloadEl))
        {
            if (topExpectError is { } err)
            {
                var ex = Assert.ThrowsAny<Exception>(() => ProcessPayloadBatch(sm, payloadEl));
                if (err.TryGetProperty("message", out JsonElement msgEl))
                {
                    Assert.Contains(msgEl.GetString()!, ex.Message, StringComparison.OrdinalIgnoreCase);
                }

                return;
            }

            ProcessPayloadBatch(sm, payloadEl);
        }
        else if (tc.TryGetProperty("messages", out JsonElement messagesEl))
        {
            if (topExpectError is { } err)
            {
                var ex = Assert.ThrowsAny<Exception>(() => ProcessPayloadBatch(sm, messagesEl));
                if (err.TryGetProperty("message", out JsonElement msgEl))
                {
                    Assert.Contains(msgEl.GetString()!, ex.Message, StringComparison.OrdinalIgnoreCase);
                }

                return;
            }

            ProcessPayloadBatch(sm, messagesEl);
        }

        if (
            tc.TryGetProperty("expect", out JsonElement expectEl)
            && expectEl.TryGetProperty("surfaces", out JsonElement surfacesEl)
        )
        {
            foreach (JsonProperty surfaceProp in surfacesEl.EnumerateObject())
            {
                Surface? surface = sm.GetSurface(surfaceProp.Name);
                Assert.NotNull(surface);

                if (surfaceProp.Value.TryGetProperty("dataModel", out JsonElement expectedDm))
                {
                    using var actualDmDoc = JsonDocument.Parse(surface.DataModel.ToJson());
                    Assert.True(
                        JsonElement.DeepEquals(expectedDm, actualDmDoc.RootElement),
                        $"Expected dataModel {expectedDm.GetRawText()}, got {surface.DataModel.ToJson()}"
                    );
                }
            }
        }
    }

    private static void ProcessPayloadBatch(SurfaceManager sm, JsonElement payload)
    {
        if (payload.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement msg in payload.EnumerateArray())
            {
                sm.Process(msg.GetRawText());
            }
        }
        else
        {
            sm.Process(payload.GetRawText());
        }
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

    private static void ExecuteDispatchActionCase(JsonElement tc)
    {
        string protocolVersion = tc.TryGetProperty("protocolVersion", out JsonElement pvEl)
            ? pvEl.GetString()!
            : A2UiProtocol.Version091;
        bool isV10 = protocolVersion == A2UiProtocol.Version10;
        JsonSerializerOptions options = isV10 ? A2UiJsonUtilities.V10Options : A2UiJsonUtilities.V09Options;

        string surfaceId = tc.TryGetProperty("surfaceId", out JsonElement sidEl)
            ? sidEl.GetString()!
            : "default_surface";
        string sourceComponentId = tc.TryGetProperty("sourceComponentId", out JsonElement cidEl)
            ? cidEl.GetString()!
            : "btn";
        string? scope = tc.TryGetProperty("scope", out JsonElement scopeEl) ? scopeEl.GetString() : null;
        int? collectionIndex = null;
        if (!string.IsNullOrEmpty(scope))
        {
            string? lastSegment = scope.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            if (
                lastSegment is not null
                && int.TryParse(lastSegment, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedIdx)
            )
            {
                collectionIndex = parsedIdx;
            }
        }

        bool userActivation = !tc.TryGetProperty("userActivation", out JsonElement uaEl) || uaEl.GetBoolean();
        FunctionCallerRole callerRole = userActivation
            ? FunctionCallerRole.RendererAction
            : FunctionCallerRole.RendererPropertyBinding;

        var dataModel = new DataModel();
        if (tc.TryGetProperty("dataModel", out JsonElement dmEl) && dmEl.ValueKind == JsonValueKind.Object)
        {
            dataModel.SetSnapshot(dmEl);
        }

        ActionMetadata? actionMetadata = null;
        if (tc.TryGetProperty("metadata", out JsonElement metaEl) && metaEl.ValueKind == JsonValueKind.Object)
        {
            actionMetadata = metaEl.Deserialize<ActionMetadata>(options);
            try
            {
                actionMetadata?.Validate();
            }
            catch (A2UiMessageValidationException ex)
            {
                Assert.True(tc.TryGetProperty("expectError", out JsonElement errEl));
                if (errEl.TryGetProperty("category", out JsonElement catEl))
                {
                    Assert.Equal("ValidationError", catEl.GetString());
                }

                _ = ex;
                return;
            }
        }

        JsonElement actionPayload = tc.GetProperty("actionPayload");
        RendererAction? dispatchedAction = null;
        RendererError? emittedError = null;

        if (actionPayload.TryGetProperty("functionCall", out JsonElement fcEl))
        {
            FunctionCallValue fc = fcEl.Deserialize<FunctionCallValue>(options)!;
            try
            {
                ExecuteActionFunctionCall(fc, dataModel, scope, collectionIndex, callerRole, protocolVersion);
            }
            catch (A2UiEvaluationException ex)
            {
                emittedError = new RendererError
                {
                    Code = "EXECUTION_ERROR",
                    SurfaceId = surfaceId,
                    Message = ex.Message,
                };
            }
        }
        else
        {
            JsonElement eventEl = actionPayload.TryGetProperty("event", out JsonElement evProp)
                ? evProp
                : actionPayload;

            if (
                !eventEl.TryGetProperty("name", out JsonElement nameEl)
                || nameEl.ValueKind != JsonValueKind.String
                || string.IsNullOrEmpty(nameEl.GetString())
            )
            {
                emittedError = new RendererError
                {
                    Code = "INVALID_ACTION",
                    SurfaceId = surfaceId,
                    Message = "Action event is missing a valid string 'name'.",
                };
            }
            else
            {
                ActionEvent actionEvent = eventEl.Deserialize<ActionEvent>(options)!;
                string? resolvedUserMessage = null;
                if (actionEvent.UserMessage is not null)
                {
                    JsonElement? userMsgEl = ResolveActionDynamicValue(
                        actionEvent.UserMessage,
                        dataModel,
                        scope,
                        collectionIndex,
                        callerRole,
                        protocolVersion
                    );
                    if (userMsgEl is { ValueKind: not (JsonValueKind.String or JsonValueKind.Null) })
                    {
                        emittedError = new RendererError
                        {
                            Code = "INVALID_ACTION",
                            SurfaceId = surfaceId,
                            Message = "Action event 'userMessage' must resolve to a string.",
                        };
                    }
                    else if (userMsgEl is { ValueKind: JsonValueKind.String } strEl)
                    {
                        resolvedUserMessage = strEl.GetString();
                    }
                }

                if (emittedError is null)
                {
                    var resolvedContext = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                    if (actionEvent.Context is not null)
                    {
                        foreach (var (key, dynVal) in actionEvent.Context)
                        {
                            resolvedContext[key] =
                                ResolveActionDynamicValue(
                                    dynVal,
                                    dataModel,
                                    scope,
                                    collectionIndex,
                                    callerRole,
                                    protocolVersion
                                ) ?? DataModel.NullElement;
                        }
                    }

                    dispatchedAction = new RendererAction
                    {
                        Name = actionEvent.Name,
                        SurfaceId = surfaceId,
                        SourceComponentId = sourceComponentId,
                        Timestamp = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                        UserMessage = resolvedUserMessage,
                        Context = resolvedContext,
                        Metadata = actionMetadata,
                    };
                    dispatchedAction.Validate();
                }
            }
        }

        if (tc.TryGetProperty("expectError", out JsonElement expectErr))
        {
            Assert.NotNull(emittedError);
            if (expectErr.TryGetProperty("code", out JsonElement codeEl))
            {
                Assert.Equal(codeEl.GetString(), emittedError.Code);
            }

            if (expectErr.TryGetProperty("surfaceId", out JsonElement errSidEl))
            {
                Assert.Equal(errSidEl.GetString(), emittedError.SurfaceId);
            }

            if (expectErr.TryGetProperty("message", out JsonElement msgEl))
            {
                Assert.Contains(msgEl.GetString()!, emittedError.Message, StringComparison.OrdinalIgnoreCase);
            }
        }
        else
        {
            Assert.Null(emittedError);
        }

        if (tc.TryGetProperty("expectDataModel", out JsonElement expectDm))
        {
            using var actualDmDoc = JsonDocument.Parse(dataModel.ToJson());
            Assert.True(
                JsonElement.DeepEquals(expectDm, actualDmDoc.RootElement),
                $"Expected dataModel {expectDm.GetRawText()}, got {dataModel.ToJson()}"
            );
        }

        if (tc.TryGetProperty("expectDispatched", out JsonElement expectDisp))
        {
            if (expectDisp.ValueKind == JsonValueKind.Null)
            {
                Assert.Null(dispatchedAction);
                return;
            }

            Assert.NotNull(dispatchedAction);
            if (expectDisp.TryGetProperty("name", out JsonElement expName))
            {
                Assert.Equal(expName.GetString(), dispatchedAction.Name);
            }

            if (expectDisp.TryGetProperty("surfaceId", out JsonElement expSid))
            {
                Assert.Equal(expSid.GetString(), dispatchedAction.SurfaceId);
            }

            if (expectDisp.TryGetProperty("sourceComponentId", out JsonElement expCid))
            {
                Assert.Equal(expCid.GetString(), dispatchedAction.SourceComponentId);
            }

            if (expectDisp.TryGetProperty("userMessage", out JsonElement expUserMsg))
            {
                Assert.Equal(expUserMsg.GetString(), dispatchedAction.UserMessage);
            }

            if (
                expectDisp.TryGetProperty("timestampIso8601", out JsonElement tsFlag)
                && tsFlag.ValueKind == JsonValueKind.True
            )
            {
                Assert.True(
                    DateTimeOffset.TryParse(
                        dispatchedAction.Timestamp,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out _
                    )
                );
            }

            if (expectDisp.TryGetProperty("metadata", out JsonElement expMeta))
            {
                if (expMeta.ValueKind == JsonValueKind.Null)
                {
                    Assert.Null(dispatchedAction.Metadata);
                }
                else
                {
                    Assert.NotNull(dispatchedAction.Metadata);
                    using JsonDocument actualMetaDoc = JsonSerializer.SerializeToDocument(
                        dispatchedAction.Metadata,
                        options
                    );
                    Assert.True(
                        JsonElement.DeepEquals(expMeta, actualMetaDoc.RootElement),
                        $"Expected metadata {expMeta.GetRawText()}, got {actualMetaDoc.RootElement.GetRawText()}"
                    );
                }
            }

            if (expectDisp.TryGetProperty("context", out JsonElement expCtx))
            {
                Assert.NotNull(dispatchedAction.Context);
                using JsonDocument actualCtxDoc = JsonSerializer.SerializeToDocument(
                    dispatchedAction.Context,
                    options
                );
                Assert.True(
                    JsonElement.DeepEquals(expCtx, actualCtxDoc.RootElement),
                    $"Expected context {expCtx.GetRawText()}, got {actualCtxDoc.RootElement.GetRawText()}"
                );
            }
        }
    }

    private static JsonElement? ResolveActionDynamicValue(
        DynamicValue? value,
        DataModel dataModel,
        string? scope,
        int? collectionIndex,
        FunctionCallerRole callerRole,
        string protocolVersion
    )
    {
        if (value is null)
        {
            return null;
        }

        if (value is DynamicValue.FunctionValue { Call: var fc })
        {
            if (fc.Call == "@index")
            {
                if (collectionIndex is null)
                {
                    throw new A2UiEvaluationException("@index cannot be called outside a collection template scope.");
                }

                int offset = 0;
                if (
                    fc.Args is not null
                    && fc.Args.TryGetValue("offset", out JsonElement offEl)
                    && offEl.ValueKind == JsonValueKind.Number
                    && offEl.TryGetInt32(out int offVal)
                )
                {
                    offset = offVal;
                }

                return JsonSerializer.SerializeToElement(
                    collectionIndex.Value + offset,
                    A2UiJsonSerializerContext.Default.Int32
                );
            }

            string? res = ExecuteActionFunctionCall(fc, dataModel, scope, collectionIndex, callerRole, protocolVersion);
            return res is not null
                ? JsonSerializer.SerializeToElement(res, A2UiJsonSerializerContext.Default.String)
                : DataModel.NullElement;
        }

        return dataModel.ResolveJsonElement(value, scope);
    }

    private static string? ExecuteActionFunctionCall(
        FunctionCallValue fc,
        DataModel dataModel,
        string? scope,
        int? collectionIndex,
        FunctionCallerRole callerRole,
        string protocolVersion
    )
    {
        if (fc.Call == "incrementCounter")
        {
            string path = "/count";
            double step = 1;
            if (fc.Args is not null)
            {
                if (fc.Args.TryGetValue("path", out JsonElement pathEl) && pathEl.ValueKind == JsonValueKind.String)
                {
                    path = pathEl.GetString()!;
                }

                if (fc.Args.TryGetValue("step", out JsonElement stepEl) && stepEl.ValueKind == JsonValueKind.Number)
                {
                    step = stepEl.GetDouble();
                }
            }

            JsonElement? currentEl = dataModel.ResolveJsonElement(DynamicValue.FromPath(path), scope);
            double current = currentEl is { ValueKind: JsonValueKind.Number } numEl ? numEl.GetDouble() : 0;
            dataModel.Apply(path, DataModel.SerializeNumberToElement(current + step));
            return null;
        }

        if (fc.Call == "failingAction")
        {
            throw new A2UiEvaluationException("Action function 'failingAction' failed.");
        }

        if (fc.Call == "formatString")
        {
            string template = "";
            if (
                fc.Args is not null
                && fc.Args.TryGetValue("value", out JsonElement valEl)
                && valEl.ValueKind == JsonValueKind.String
            )
            {
                template = valEl.GetString()!;
            }

            var parser = new ExpressionParser();
            var registry = FunctionRegistry.CreateDefault();
            IReadOnlyList<ExpressionToken> tokens = parser.Parse(template);
            var sb = new StringBuilder();
            foreach (ExpressionToken token in tokens)
            {
                sb.Append(
                    token switch
                    {
                        PathToken p => dataModel.Resolve(DynamicValue.FromPath(p.Path), scope) ?? "",
                        _ => EvaluateExpressionToken(token, dataModel, registry),
                    }
                );
            }

            return sb.ToString();
        }

        var args = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (fc.Args is not null)
        {
            foreach (var (k, v) in fc.Args)
            {
                args[k] = JsonElementToArgString(v);
            }
        }

        var reg = FunctionRegistry.CreateDefault();
        return reg.Evaluate(
            fc.Call,
            args,
            new FunctionEvaluationContext
            {
                CallerRole = callerRole,
                CollectionIndex = collectionIndex,
                ProtocolVersion = protocolVersion,
            }
        );
    }
}
