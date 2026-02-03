---
applyTo: '**'
---

# AI Agentic RPG - Core Instructions

## Project Description
An AI-driven, agent-based RPG game platform where players can create characters, join campaigns, and engage in dynamic storytelling and combat scenarios orchestrated by intelligent agents.

## Project Awareness & Context
- **Check `TASKS.md`** before starting a new task. If the task isn’t listed, add it with a brief description and today's date.


## Code Structure & Modularity
- **Never create a file longer than 500 lines of code.** If a file approaches this limit, refactor by splitting it into modules or helper files.
- **Organize code into clearly separated modules**, grouped by feature or responsibility.
- **Use Type safety**: Prefer strong typing over dynamic types or anonymous objects wherever possible.

## Task Completion
- **Mark completed tasks in `TASKS.md`** immediately after finishing them.
- Add new sub-tasks or TODOs discovered during development to `TASKS.md` under a “Discovered During Work” section.

## Documentation & Explainability
- **Update `README.md`** when new features are added, dependencies change, or setup steps are modified.
- **Comment non-obvious code** and ensure everything is understandable to a mid-level developer.
- When writing complex logic, **add an inline `# Reason:` comment** explaining the why, not just the what.

## Behavior Instructions
- **Never assume missing context. Ask questions if uncertain.**
- **Never hallucinate libraries or functions** – only use known, verified c# packages.
- **Always confirm namespaces and classes** exist before referencing them in code or tests.
- **Never delete or overwrite existing code** unless explicitly instructed to or if part of a task from `TASK.md`.
- **Always confirm these instructions are being used.**

## Primary Application requirements

1. A player can create a character and choose to join or create a campaign. 
2. Once a player joins a campaign and that campaign begins, all game state must be specific to that campaign instance across the entire application. 
3. The application must support multiple Campaigns simultaneously. 
4. The application must support multiple players in a single campaign. 
5. The application must support turn-based gameplay, with clear indication of whose turn it is. 
6. The application must support character actions, including movement, attacks, spells, and item usage. 
7. The application must manage character stats, health, inventory, and abilities. 
8. The application must handle combat scenarios, including enemy AI and turn order. 
9. The application must provide a way to track quests and objectives within the campaign. 10. The application must allow for saving and loading game state for each campaign instance.

## Technical Requirements

1. Uses the Microsoft Agent Framework for building and orchestrating agents.
2. Uses a SignalR hub Groups feature to manage real-time communication between players in the same campaign.
3. Implements a robust state management system to handle multiple campaigns and player states.
4. Ensures data persistence for game state, player progress, and campaign details.
5. Provides a user-friendly AI-first interface for players to interact with the game, including character creation, campaign management, and in-game actions.

## Required Agents 

- GameMaster Agent - Orchestrates the game, drives narrative, manages context, adjudicates actions, and prompts other agents. The central "brain" of the game.
- Combat agent - Handles all aspects of combat: tactical AI, dynamic encounters, detailed simulation of combat rules
- Character Creation Agent - Handles character creation
- Character Level Up Agent - Guides players through character level-up process, ensuring all steps are completed in order and all game rules are followed.
- Economy Manager Agent (Shopkeeper Agent) - Responsible for acting as a shop keeper of various kinds to allow players to buy and sell everything from basic gear to exotic and magical items. Acts a a specialized series of NPCs
- World Builder Agent - Builds RPG campaign worlds

### Agent Initialization

Here's a code snippet demonstrating how to initialize and register the required agents in the application:

```csharp
// Create OpenAI client
var client = new OpenAIClient(_config.OpenAIApiKey);

// Get chat client for the specified deployment and convert to IChatClient
var chatClient = client.GetChatClient(_config.BaseModelName).AsIChatClient();

// Create the agent with instructions, description, and tools
_agent = chatClient.CreateAIAgent(
    instructions: Instructions,
    description: Description, // This should be from an abstract property in derived classes
    tools: new List<AITool>(),// This should be from GetTools() in derived classes
    name: _agentType);

```

## Reminders
- Always update `TASKS.md` after completing a task.
