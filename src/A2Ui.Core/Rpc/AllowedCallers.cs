namespace Blazejewicz.A2Ui.Core.Rpc;

/// <summary>
/// Specifies which parties are permitted to invoke a catalog function.
/// Maps to allowedCallers in specification/v1_0/json/catalog_definition.json.
/// </summary>
public enum AllowedCallers
{
    /// <summary>Callable only by the client renderer (default).</summary>
    RendererOnly,

    /// <summary>Callable only by the server agent.</summary>
    AgentOnly,

    /// <summary>Callable by either renderer or agent.</summary>
    RendererOrAgent,
}
