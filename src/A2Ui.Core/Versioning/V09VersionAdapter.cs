using System.Text.Json;
using Blazejewicz.A2Ui.Core.Messages;

namespace Blazejewicz.A2Ui.Core.Versioning;

/// <summary>
/// Version adapter for A2UI v0.9 and v0.9.1 protocol payloads.
/// </summary>
public sealed class V09VersionAdapter : IVersionAdapter
{
    /// <inheritdoc />
    public string Version => A2UiProtocol.LatestV09;

    /// <inheritdoc />
    public InternalOperation AdaptAgentToRenderer(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return this.AdaptAgentToRenderer(doc.RootElement);
    }

    /// <inheritdoc />
    public InternalOperation AdaptAgentToRenderer(JsonElement root)
    {
        var message =
            JsonSerializer.Deserialize(root, A2UiJsonUtilities.V09Context.A2UiMessage)
            ?? throw new A2UiMessageValidationException("Failed to deserialize A2UiMessage from JSON.");

        return this.AdaptAgentToRenderer(message);
    }

    /// <summary>
    /// Adapts an incoming <see cref="A2UiMessage"/> instance into an internal operation.
    /// </summary>
    /// <param name="message">The v0.9/v0.9.1 message to adapt.</param>
    /// <returns>The adapted internal operation.</returns>
    public InternalOperation AdaptAgentToRenderer(A2UiMessage message)
    {
        message.Validate();
        string version =
            message.Version == A2UiProtocol.Version09 ? A2UiProtocol.Version09 : A2UiProtocol.Version091;

        if (message.CreateSurface is { } cs)
        {
            if (string.IsNullOrEmpty(cs.CatalogId))
            {
                throw new A2UiMessageValidationException("createSurface requires 'catalogId' in v0.9/v0.9.1.");
            }

            return new CreateSurfaceOp
            {
                SurfaceId = cs.SurfaceId,
                CatalogId = cs.CatalogId,
                Theme = cs.Theme?.Clone(),
                SendDataModel = cs.SendDataModel ?? false,
                Components = cs.Components,
                DataModel = cs.DataModel?.Clone(),
                Metadata = cs.Metadata,
                EnforceUniqueness = false,
                ProtocolVersion = version,
            };
        }

        if (message.DeleteSurface is { } ds)
        {
            return new DeleteSurfaceOp { SurfaceId = ds.SurfaceId, ProtocolVersion = version };
        }

        if (message.UpdateComponents is { } uc)
        {
            return new UpdateComponentsOp
            {
                SurfaceId = uc.SurfaceId,
                Components = uc.Components,
                ProtocolVersion = version,
            };
        }

        if (message.UpdateDataModel is { } ud)
        {
            return new UpdateDataModelOp
            {
                SurfaceId = ud.SurfaceId,
                Path = ud.Path,
                Value = ud.Value?.Clone(),
                DeleteOnNull = false,
                ProtocolVersion = version,
            };
        }

        throw new A2UiMessageValidationException("No valid operation found in A2UiMessage.");
    }
}
