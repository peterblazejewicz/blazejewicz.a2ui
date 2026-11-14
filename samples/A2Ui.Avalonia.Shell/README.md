# A2UI Avalonia Shell (`Blazejewicz.A2Ui.Avalonia.Shell`)

`Blazejewicz.A2Ui.Avalonia.Shell` (`A2Ui.Avalonia.Shell.csproj`) is a connected Avalonia `12.1.5` desktop client that communicates with A2UI-capable agents over the A2A HTTP JSON-RPC transport.

It provides an interactive conversational UI capable of rendering streaming surfaces, updating data models, and round-tripping user actions back to agents.

## 1. Architecture and wire contracts

* **A2A transport:** Uses the `A2A.V0_3` (`1.0.0-preview4`) NuGet package for A2A v0.3 JSON-RPC 2.0 communication (`SendMessageRequest`, `MessageSendParams`, `Message`, `DataPart`, `TextPart`, `Task`, `AgentCard`). `A2AAgentClient` performs request-response round-trips against agent endpoints.
* **Protocol negotiation:** Sends the `X-A2A-Extensions` HTTP header advertising multi-version A2UI support:
  ```http
  X-A2A-Extensions: https://a2ui.org/a2a-extension/a2ui/v1.0, https://a2ui.org/a2a-extension/a2ui/v0.9.1, https://a2ui.org/a2a-extension/a2ui/v0.9
  ```
* **Outbound action serialization:** User interactions from rendered controls are serialized via `UserActionSerializer`. For `v1.0` surfaces, it emits typed `RendererToAgentMessage` envelopes; for `v0.9` / `v0.9.1` surfaces, it emits typed `ClientToServerMessage` envelopes (`{ "version": "v0.9.1", "action": { "name", "surfaceId", "sourceComponentId", "timestamp", "context" } }`).
* **Surface lifecycle & capabilities:** Outbound `AgentMessage.Metadata` includes `a2uiRendererCapabilities` (`"1.0"`), `a2uiClientCapabilities` (`"v0.9"`), and active `sendDataModel` surface states (`a2uiRendererDataModel` / `a2uiClientDataModel`), while persisting `ContextId` across conversation turns.
* **Observability:** Structured `[LoggerMessage]` logging (`A2AAgentClientLog`, `LoggingHttpMessageHandlerLog`, `RequestSummaryLoggerLog`, `ShellViewModelLog`, `ShellWindowLog`, `SurfaceManagerLog`), Serilog `BeginScope` request correlation (`CorrelationId`, `MessageId`), and `ActivitySource` distributed tracing (`A2A.SendMessage`).

## 2. Prerequisites and agent setup

The Shell connects by default to an external A2A agent listening on `http://localhost:10002`.

### Running the Python `restaurant_finder` agent

From an A2UI agent checkout (`samples/agent/adk/restaurant_finder/`):

1. Configure `.env` in the agent directory:
   ```bash
   # Satisfy ADK pre-flight check when using non-Gemini models via LiteLLM
   GEMINI_API_KEY=unused

   # Option A: Anthropic Claude Sonnet 4.6
   LITELLM_MODEL=anthropic/claude-sonnet-4-6
   ANTHROPIC_API_KEY=your-api-key

   # Option B: Local Gemma 4 31B on Ollama
   # LITELLM_MODEL=ollama_chat/gemma4:31b
   # OLLAMA_API_BASE=http://localhost:11434
   ```
   *(When running local Gemma 4 via Ollama, set `temperature=0.2` and `top_p=0.9` in LiteLLM model parameters for deterministic JSON schema adherence.)*
2. Start the agent server:
   ```bash
   uv run .
   ```
3. Verify the agent card endpoint is reachable:
   ```bash
   curl http://localhost:10002/.well-known/agent-card.json
   ```

## 3. Build and run

From the repository root:

```bash
dotnet build samples/A2Ui.Avalonia.Shell/A2Ui.Avalonia.Shell.csproj --configuration Release
dotnet run --project samples/A2Ui.Avalonia.Shell/A2Ui.Avalonia.Shell.csproj --configuration Release
```

## 4. End-to-end verification matrix

| Step | Action | Expected result |
|------|--------|-----------------|
| 1. Startup | Start agent on `http://localhost:10002` and launch Shell | Status bar transitions from `"Connecting..."` to `"Restaurant Agent"`. |
| 2. Search query | Submit `"Top 5 Chinese restaurants in New York"` | Restaurant list surface renders with cards, images, ratings, and `"Book Now"` buttons. |
| 3. Surface replacement | Submit `"Italian restaurants in Seattle"` | Prior surface clears cleanly before the new list renders. |
| 4. Booking form | Click `"Book Now"` on a restaurant card | Booking form surface renders with party size input, `CalendarDatePicker`, and `TimePicker`. |
| 5. Two-way data binding | Enter party size, select date and time | Input controls write back into the surface `DataModel` via `InputHelper`. |
| 6. Action submission | Click `"Submit Booking"` | Outbound action dispatches with bound context; confirmation card replaces the booking form. |
| 7. Wire payload check | Inspect agent console output | Received payload contains `action` (or v1.0 `RendererToAgentMessage.action`) with `name: "book_restaurant"`, `surfaceId`, `sourceComponentId`, and `context`. |
| 8. Telemetry check | Inspect `samples/A2Ui.Avalonia.Shell/bin/Release/net10.0/logs/shell-*.log` | `RequestSummary` Information log and `SurfaceManagerLog` EventIds (`1`, `3`, `4`, `8`) recorded with zero warnings. |
