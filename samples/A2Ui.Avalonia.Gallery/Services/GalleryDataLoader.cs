using System.Globalization;
using System.Text.Json;
using Blazejewicz.A2Ui.Avalonia.Gallery.Models;
using Blazejewicz.A2Ui.Core;
using Blazejewicz.A2Ui.Core.Messages;
using Microsoft.Extensions.Logging;

namespace Blazejewicz.A2Ui.Avalonia.Gallery.Services;

/// <summary>
/// Loads A2UI spec examples from the Specs/ directory relative to the app executable.
/// Handles both envelope format ({ name, description, messages[] }) and bare array format.
/// Auto-injects a createSurface message if the example doesn't include one.
/// </summary>
public sealed class GalleryDataLoader(ILogger<GalleryDataLoader> logger)
{
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        AllowOutOfOrderMetadataProperties = true,
    };

    private readonly ILogger<GalleryDataLoader> _logger = logger;

    public async Task<IReadOnlyList<DemoItem>> LoadAsync(CancellationToken ct = default)
    {
        var items = new List<DemoItem>();
        string specsDir = Path.Combine(AppContext.BaseDirectory, "Specs");

        await this.LoadFromDirectoryAsync(
                Path.Combine(specsDir, "minimal"),
                isBasic: false,
                protocolVersion: "v0.9.1",
                items,
                ct
            )
            .ConfigureAwait(false);
        await this.LoadFromDirectoryAsync(
                Path.Combine(specsDir, "basic"),
                isBasic: true,
                protocolVersion: "v0.9.1",
                items,
                ct
            )
            .ConfigureAwait(false);
        await this.LoadFromDirectoryAsync(
                Path.Combine(specsDir, "v1_0", "basic"),
                isBasic: true,
                protocolVersion: "v1.0",
                items,
                ct
            )
            .ConfigureAwait(false);

        return items;
    }

    private async Task LoadFromDirectoryAsync(
        string directory,
        bool isBasic,
        string protocolVersion,
        List<DemoItem> items,
        CancellationToken ct
    )
    {
        if (!Directory.Exists(directory))
        {
            this._logger.LogWarning("Specs directory not found: {Directory}", directory);
            return;
        }

        string[] files = Directory.GetFiles(directory, "*.json");
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);

        foreach (string filePath in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                DemoItem? item = await LoadFileAsync(filePath, isBasic, protocolVersion, ct).ConfigureAwait(false);
                if (item is not null)
                {
                    items.Add(item);
                }
            }
            catch (JsonException ex)
            {
                this._logger.LogWarning(ex, "Skipping malformed JSON: {FilePath}", filePath);
            }
            catch (IOException ex)
            {
                this._logger.LogWarning(ex, "Cannot read file: {FilePath}", filePath);
            }
        }
    }

    private static async Task<DemoItem?> LoadFileAsync(
        string filePath,
        bool isBasic,
        string protocolVersion,
        CancellationToken ct
    )
    {
        string json = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        // Extract messages — handle both envelope and bare array
        A2UiMessage[] messages = [];
        var rawMessages = new List<string>();
        string? description = null;

        if (root.ValueKind == JsonValueKind.Array)
        {
            ExtractMessages(root, protocolVersion, rawMessages, ref messages);
        }
        else
        {
            if (root.TryGetProperty("description", out JsonElement descEl))
            {
                description = descEl.GetString();
            }

            if (
                root.TryGetProperty("messages", out JsonElement messagesEl)
                && messagesEl.ValueKind == JsonValueKind.Array
            )
            {
                ExtractMessages(messagesEl, protocolVersion, rawMessages, ref messages);
            }
            else
            {
                return null;
            }
        }

        if (rawMessages.Count == 0 && messages.Length == 0)
        {
            return null;
        }

        string filename = Path.GetFileName(filePath);
        string surfaceId = Path.GetFileNameWithoutExtension(filePath);

        if (protocolVersion is "v1.0" or A2UiProtocol.Version10)
        {
            foreach (string raw in rawMessages)
            {
                try
                {
                    using var mDoc = JsonDocument.Parse(raw);
                    if (
                        mDoc.RootElement.TryGetProperty("createSurface", out var cs)
                        && cs.TryGetProperty("surfaceId", out var sid)
                    )
                    {
                        surfaceId = sid.GetString() ?? surfaceId;
                        break;
                    }
                }
                catch
                {
                    // Ignore parse errors here
                }
            }
        }
        else
        {
            // Auto-inject createSurface if missing
            bool hasCreate = messages.Any(m => m.CreateSurface is not null);
            if (!hasCreate)
            {
                string catalogId = isBasic
                    ? "https://a2ui.org/specification/v0_9/basic_catalog.json"
                    : "https://a2ui.org/specification/v0_9/catalogs/minimal/minimal_catalog.json";

                var createMsg = new A2UiMessage
                {
                    Version = A2UiProtocol.Version09,
                    CreateSurface = new CreateSurface { SurfaceId = surfaceId, CatalogId = catalogId },
                };
                messages = [createMsg, .. messages];
                rawMessages.Insert(0, JsonSerializer.Serialize(createMsg, s_jsonOptions));
            }
            else
            {
                // Use the surfaceId from the createSurface message
                A2UiMessage? createMessage = messages.FirstOrDefault(m => m.CreateSurface is not null);
                if (createMessage?.CreateSurface is not null)
                {
                    surfaceId = createMessage.CreateSurface.SurfaceId;
                }
            }
        }

        // Derive display title from the example filename
        string title = DeriveTitleFromFilename(filename);

        return new DemoItem(
            Id: surfaceId,
            Title: title,
            Filename: filename,
            Description: description ?? $"Source: {filename}",
            Messages: messages,
            IsBasic: isBasic,
            ProtocolVersion: protocolVersion,
            RawJsonMessages: protocolVersion is "v1.0" or A2UiProtocol.Version10 ? rawMessages : null
        );
    }

    private static void ExtractMessages(
        JsonElement arrayEl,
        string protocolVersion,
        List<string> rawMessages,
        ref A2UiMessage[] messages
    )
    {
        foreach (JsonElement el in arrayEl.EnumerateArray())
        {
            rawMessages.Add(el.GetRawText());
        }

        if (protocolVersion is not ("v1.0" or A2UiProtocol.Version10))
        {
            messages = JsonSerializer.Deserialize<A2UiMessage[]>(arrayEl.GetRawText(), s_jsonOptions) ?? [];
        }
    }

    private static string DeriveTitleFromFilename(string filename)
    {
        string stem = Path.GetFileNameWithoutExtension(filename);
        string[] words = stem.Split(['_', '-'], StringSplitOptions.RemoveEmptyEntries);

        // Skip the leading numeric token (e.g., "1", "01", "33") so that the
        // caller can assign continuous display indices without duplication.
        if (words.Length > 1 && words[0].All(char.IsDigit))
        {
            words = words[1..];
        }

        return string.Join(
            ' ',
            words.Select(w => string.IsNullOrEmpty(w) ? w : char.ToUpper(w[0], CultureInfo.InvariantCulture) + w[1..])
        );
    }
}
