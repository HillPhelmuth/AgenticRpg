# AgenticRpg

An AI-driven, agent-based RPG platform for character creation, campaign management, and collaborative storytelling.

## Highlights
- Multi-campaign support with turn-based gameplay.
- Agent-orchestrated narrative, combat, and character workflows.
- Startup menu now limits visible campaigns to those you own or add via invitation code.
- Campaign API endpoints enforce ownership using authenticated user claims.

## Solution Layout
- AgenticRpg: ASP.NET Core host with API endpoints and SignalR hub.
- AgenticRpg.Client: Blazor WebAssembly client UI.
- AgenticRpg.Core: Core models, repositories, agents, and services.
- AgenticRpg.Components: Shared UI components.

## Running Locally
1. Configure app settings for Cosmos DB and OpenAI/OpenRouter keys in appsettings.json.
2. Run the ASP.NET Core host project.
3. Open the client in a browser and sign in.

## Authentication Notes
- The Blazor client requests access tokens using AzureAd:DefaultScopes.
- Ensure the App Registration exposes the scope api://<ClientId>/access_as_user and that the client is granted access.
