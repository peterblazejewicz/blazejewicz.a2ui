using System.Text.Json;
using Blazejewicz.A2Ui.Core.Bindings;
using Blazejewicz.A2Ui.Core.Components;
using Blazejewicz.A2Ui.Core.Messages;

namespace Blazejewicz.A2Ui.Core.Versioning;

/// <summary>
/// Base class for protocol-version-neutral internal operations dispatched into <see cref="Surfaces.SurfaceManager"/>.
/// </summary>
public abstract record InternalOperation
{
    /// <summary>Canonical operation type name.</summary>
    public abstract string OperationType { get; }

    /// <summary>Target surface ID, or null if the operation is surface-independent.</summary>
    public abstract string? TargetSurfaceId { get; }
}

/// <summary>Internal operation representing a surface creation request.</summary>
public sealed record CreateSurfaceOp : InternalOperation
{
    /// <inheritdoc/>
    public override string OperationType => "createSurface";

    /// <inheritdoc/>
    public override string TargetSurfaceId => this.SurfaceId;

    /// <summary>Unique identifier of the surface.</summary>
    public required string SurfaceId { get; init; }

    /// <summary>Default catalog ID for the surface.</summary>
    public string? CatalogId { get; init; }

    /// <summary>Initial inline components created with the surface.</summary>
    public IReadOnlyList<A2UiComponent>? Components { get; init; }

    /// <summary>Initial root data model snapshot.</summary>
    public JsonElement? DataModel { get; init; }

    /// <summary>Optional theme payload (v0.9 only).</summary>
    public JsonElement? Theme { get; init; }

    /// <summary>Whether the renderer should send back the data model.</summary>
    public bool SendDataModel { get; init; }

    /// <summary>Surface-level metadata.</summary>
    public SurfaceMetadata? Metadata { get; init; }

    /// <summary>Supported catalog IDs for this surface.</summary>
    public IReadOnlyList<string>? SupportedCatalogIds { get; init; }

    /// <summary>Whether duplicate creation of an already-active surface ID must fail even when StrictMode is disabled (v1.0).</summary>
    public bool EnforceUniqueness { get; init; }

    /// <summary>Protocol specification version of this surface.</summary>
    public string? ProtocolVersion { get; init; }
}

/// <summary>Internal operation representing a component tree update.</summary>
public sealed record UpdateComponentsOp : InternalOperation
{
    /// <inheritdoc/>
    public override string OperationType => "updateComponents";

    /// <inheritdoc/>
    public override string TargetSurfaceId => this.SurfaceId;

    /// <summary>Identifier of the surface to update.</summary>
    public required string SurfaceId { get; init; }

    /// <summary>Components to add or update.</summary>
    public required IReadOnlyList<A2UiComponent> Components { get; init; }

    /// <summary>Protocol specification version of this operation.</summary>
    public string ProtocolVersion { get; init; } = A2UiProtocol.Version10;
}

/// <summary>Internal operation representing a data model update.</summary>
public sealed record UpdateDataModelOp : InternalOperation
{
    /// <inheritdoc/>
    public override string OperationType => "updateDataModel";

    /// <inheritdoc/>
    public override string TargetSurfaceId => this.SurfaceId;

    /// <summary>Identifier of the surface to update.</summary>
    public required string SurfaceId { get; init; }

    /// <summary>JSON pointer path to update.</summary>
    public string? Path { get; init; }

    /// <summary>Value to set.</summary>
    public JsonElement? Value { get; init; }

    /// <summary>When true, null values delete the target path.</summary>
    public bool DeleteOnNull { get; init; }

    /// <summary>Protocol specification version of this operation.</summary>
    public string ProtocolVersion { get; init; } = A2UiProtocol.Version10;
}

/// <summary>Internal operation representing a surface deletion request.</summary>
public sealed record DeleteSurfaceOp : InternalOperation
{
    /// <inheritdoc/>
    public override string OperationType => "deleteSurface";

    /// <inheritdoc/>
    public override string TargetSurfaceId => this.SurfaceId;

    /// <summary>Identifier of the surface to delete.</summary>
    public required string SurfaceId { get; init; }

    /// <summary>Protocol specification version of this operation.</summary>
    public string ProtocolVersion { get; init; } = A2UiProtocol.Version10;
}

/// <summary>Internal operation representing a renderer-executed function call request.</summary>
public sealed record CallRendererFunctionOp : InternalOperation
{
    /// <inheritdoc/>
    public override string OperationType => "callRendererFunction";

    /// <inheritdoc/>
    public override string? TargetSurfaceId => this.SurfaceId;

    /// <summary>Optional surface ID whose data model provides the evaluation context.</summary>
    public string? SurfaceId { get; init; }

    /// <summary>Unique ID for this instance of the function call.</summary>
    public required string FunctionCallId { get; init; }

    /// <summary>Function call details to execute.</summary>
    public required FunctionCallValue CallFunction { get; init; }
}

/// <summary>Internal operation representing an agent's response to a renderer function call.</summary>
public sealed record AgentFunctionResponseOp : InternalOperation
{
    /// <inheritdoc/>
    public override string OperationType => "agentFunctionResponse";

    /// <inheritdoc/>
    public override string? TargetSurfaceId => null;

    /// <summary>Unique ID matching the initiating function call.</summary>
    public required string FunctionCallId { get; init; }

    /// <summary>Return value of the function.</summary>
    public JsonElement? Value { get; init; }

    /// <summary>Error object indicating failure of the function execution.</summary>
    public FunctionError? Error { get; init; }
}
