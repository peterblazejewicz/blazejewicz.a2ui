using System.Text.Json;
using System.Text.Json.Serialization;
using AGUI.Abstractions;

namespace Blazejewicz.AgUi.Protocol.Tests;

public sealed class RunAgentInputSerializationTests
{
    // Intended AG-UI wire contract: optional fields (parentRunId, state, tools, context,
    // forwardedProps) are omitted when null. Consumers serializing RunAgentInput over HTTP
    // must opt into WhenWritingNull to match the AG-UI reference server's expectations.
    private static readonly JsonSerializerOptions s_opts = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = AGUIJsonUtilities.DefaultTypeInfoResolver,
    };

    [Fact]
    public void RunAgentInput_RoundTrip_PreservesParentRunId()
    {
        var input = new RunAgentInput
        {
            ThreadId = "t1",
            RunId = "r1",
            ParentRunId = "parent-42",
            Messages = [],
        };

        var json = JsonSerializer.Serialize(input, s_opts);
        var roundTripped = JsonSerializer.Deserialize<RunAgentInput>(json, s_opts);

        Assert.NotNull(roundTripped);
        Assert.Equal("parent-42", roundTripped!.ParentRunId);
        Assert.Contains("\"parentRunId\":\"parent-42\"", json);
    }

    [Fact]
    public void RunAgentInput_Deserialize_OmittedParentRunId_IsNull()
    {
        const string Json = """{"threadId":"t1","runId":"r1","messages":[]}""";

        var input = JsonSerializer.Deserialize<RunAgentInput>(Json, s_opts);

        Assert.NotNull(input);
        Assert.Null(input!.ParentRunId);
    }

    [Fact]
    public void RunAgentInput_Serialize_NullParentRunId_IsOmittedUnderWireContract()
    {
        var input = new RunAgentInput
        {
            ThreadId = "t1",
            RunId = "r1",
            Messages = [],
        };

        var json = JsonSerializer.Serialize(input, s_opts);

        Assert.DoesNotContain("parentRunId", json);
    }
}
