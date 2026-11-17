using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using AGUI.Abstractions;

namespace Blazejewicz.AgUi.Protocol.Tests.Events;

/// <summary>
/// Verifies that all public <c>const string</c> fields on <see cref="AGUIEventTypes"/>
/// deserialize via <see cref="BaseEvent"/> and <see cref="AGUIJsonSerializerContext"/>.
/// </summary>
public sealed class EventTypeCoverageTests
{
    private static readonly JsonSerializerOptions s_opts = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = AGUIJsonUtilities.DefaultTypeInfoResolver,
    };

    private static HashSet<string> GetEventTypeConstants()
    {
        return typeof(AGUIEventTypes)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);
    }

    [Fact]
    public void AllEventTypeConstants_DeserializeViaBaseEvent()
    {
        HashSet<string> constants = GetEventTypeConstants();

        Assert.NotEmpty(constants);

        foreach (string eventType in constants)
        {
            string deltaValue = eventType switch
            {
                "TEXT_MESSAGE_CONTENT"
                or "REASONING_MESSAGE_CONTENT"
                or "TEXT_MESSAGE_CHUNK"
                or "REASONING_MESSAGE_CHUNK"
                or "TOOL_CALL_ARGS"
                or "TOOL_CALL_CHUNK" => "\"test delta\"",
                "STATE_DELTA" or "ACTIVITY_DELTA" => "[{\"op\":\"replace\",\"path\":\"/test\",\"value\":42}]",
                _ => "[]",
            };

            string extraFields = eventType switch
            {
                "REASONING_ENCRYPTED_VALUE" =>
                    ",\"subtype\":\"message\",\"entityId\":\"e1\",\"encryptedValue\":\"enc\"",
                "TOOL_CALL_RESULT" => ",\"content\":\"result\"",
                "TOOL_CALL_START" => ",\"toolCallName\":\"render_ui\"",
                "MESSAGES_SNAPSHOT" => ",\"messages\":[]",
                "ACTIVITY_SNAPSHOT" => ",\"activityType\":\"act\",\"content\":{}",
                _ => "",
            };

            string json =
                $$"""{"type":"{{eventType}}","message":"test","threadId":"t","runId":"r","subagentRunId":"sub-1","messageId":"m","toolCallId":"tc","snapshot":{},"name":"n","value":"v","delta":{{deltaValue}},"stepName":"s","activityType":"act","patch":{{deltaValue}},"event":{},"source":"src"{{extraFields}}}""";

            BaseEvent? evt = JsonSerializer.Deserialize<BaseEvent>(json, s_opts);
            Assert.NotNull(evt);
        }
    }

    [Fact]
    public void UnknownEventType_ThrowsAGUIUnknownEventTypeException()
    {
        const string Json = """{"type":"NOT_A_REAL_EVENT_TYPE"}""";

        Assert.Throws<AGUIUnknownEventTypeException>(() => JsonSerializer.Deserialize<BaseEvent>(Json, s_opts));
    }
}
