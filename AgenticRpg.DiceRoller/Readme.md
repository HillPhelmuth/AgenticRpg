# AgenticRpg.DiceRoller

A Blazor WebAssembly component library for integrating animated 3D dice rolls into AI-driven RPG agents. This library provides tools that allow AI agents to request dice rolls from players during gameplay, with support for various die types (d4, d6, d8, d10, d12, d20) and automatic or player-controlled rolls.

## Overview

The DiceRoller library bridges the gap between AI agents and player interaction by providing tools that agents can invoke to request dice rolls. When an agent needs a dice roll (for attacks, skill checks, damage, etc.), it calls these tools, which display an animated modal to the player and return the results back to the agent.

## Setup

### 1. Register Services

In your `Program.cs`:

```csharp
using AgenticRpg.DiceRoller.Models;

builder.Services.AddRollDisplayService();
```

### 2. Add Component to Layout

Add the `RollDiceWindow` component to your main layout (e.g., `MainLayout.razor`):

```razor
@using AgenticRpg.DiceRoller

<RollDiceWindow />
```

## AI Agent Integration

The library provides `DieRollerTools` and `DieRollerExtensions` for seamless integration with AI agents built using the Microsoft Agent Framework.

### Adding Dice Roller Tools to Your Agent

Use the `GetDiceRollerTools()` extension method to add all dice rolling capabilities to your agent:

```csharp
using AgenticRpg.DiceRoller.Models;

public class GameMasterAgent : BaseGameAgent
{
    private readonly RollDiceService _diceService;

    public GameMasterAgent(RollDiceService diceService, /* other dependencies */)
    {
        _diceService = diceService;
        // ... initialization
    }

    protected override List<AITool> GetTools()
    {
        var tools = new List<AITool>();
        
        // Add dice roller tools
        tools.AddRange(_diceService.GetDiceRollerTools());
        
        // Add other agent-specific tools
        // tools.AddRange(otherTools);
        
        return tools;
    }
}
```

### Available Agent Tools

The `GetDiceRollerTools()` extension method provides three tools to your agent:

#### 1. RollDie
Rolls a single die with optional modifier.

**Parameters:**
- `reasonForRolling` (string) - Purpose of the roll explained to the player (one sentence or less)
- `dieType` (DieType) - Type of die based on number of sides (D4, D6, D8, D10, D12, D20)
- `modifier` (int, optional) - Value to add or subtract from the roll result (default: 0)

**Returns:** `int` - The total result (roll + modifier)

**Example Agent Usage:**
```
Agent: "I need to check if the player succeeds at picking the lock. Roll a D20 with your Dexterity modifier."
Tool Call: RollDie("Lockpicking Check", DieType.D20, modifier: 3)
Result: 17 (rolled 14 + 3 modifier)
```

#### 2. RollPlayerDice
Prompts the player to roll multiple dice of the same type.

**Parameters:**
- `reasonForRolling` (string) - Purpose of the roll explained to the player
- `dieType` (DieType) - Type of die to roll
- `numberOfRolls` (int) - Number of times to roll the die (e.g., 3 for "3d6")
- `modifier` (int, optional) - Value to add or subtract from the total (default: 0)
- `dropLowest` (bool, optional) - Ignore the lowest die roll (used for ability score generation)

**Returns:** `DieRollResults` - Object containing:
- `RollResults` (List<int>) - Individual die results
- `Total` (int) - Sum of all rolls plus modifier

**Example Agent Usage:**
```
Agent: "Roll damage for your greatsword attack."
Tool Call: RollPlayerDice("Greatsword Damage", DieType.D6, numberOfRolls: 2, modifier: 4)
Result: DieRollResults { RollResults = [4, 5], Total = 13 } (4+5+4)
```

**Example with Drop Lowest:**
```
Agent: "Let's generate your Strength ability score."
Tool Call: RollPlayerDice("Strength Score", DieType.D6, numberOfRolls: 4, dropLowest: true)
Result: DieRollResults { RollResults = [5, 4, 3], Total = 12 } (dropped 2, kept 5+4+3)
```

#### 3. RollNonPlayerCharacterDice
Automatically rolls dice for NPCs or monsters without requiring player interaction. The roll is displayed in the top-right corner for transparency but executes immediately.

**Parameters:**
- `reasonForRolling` (string) - Purpose of the roll shown to the player
- `dieType` (DieType) - Type of die to roll
- `numberOfRolls` (int) - Number of times to roll the die
- `modifier` (int, optional) - Value to add or subtract from the total (default: 0)

**Returns:** `DieRollResults` - Object containing individual rolls and total

**Example Agent Usage:**
```
Agent: "The goblin attacks you with its scimitar."
Tool Call: RollNonPlayerCharacterDice("Goblin Attack Roll", DieType.D20, numberOfRolls: 1, modifier: 4)
Result: DieRollResults { RollResults = [12], Total = 16 } (12+4)
```

### Tool Selection Guidelines for Agents

**Use `RollDie`** when:
- You need a single die roll
- The result is a simple success/failure check
- You want to minimize complexity

**Use `RollPlayerDice`** when:
- Rolling multiple dice (e.g., 3d6, 2d8+3)
- You need access to individual die results (for effects that target specific dice)
- Generating ability scores (with `dropLowest: true`)
- The player is performing the action

**Use `RollNonPlayerCharacterDice`** when:
- An NPC or monster is performing the action
- You want automatic, non-interactive rolls
- Maintaining game flow is important (no waiting for player input)

## Implementation Example

Here's a complete example of a Combat Agent using the dice roller tools:

```csharp
using AgenticRpg.DiceRoller;
using AgenticRpg.DiceRoller.Models;
using Microsoft.Extensions.AI;

public class CombatAgent : BaseGameAgent
{
    private readonly RollDiceService _diceService;
    
    public CombatAgent(RollDiceService diceService, AgentConfiguration config)
        : base(config)
    {
        _diceService = diceService;
    }

    protected override string Instructions => @"
        You are a combat agent managing tactical encounters in an RPG.
        
        When a player attacks:
        1. Use RollPlayerDice for attack rolls (1d20 + attack bonus)
        2. Compare result to target's AC
        3. If hit, use RollPlayerDice for damage (e.g., 2d6 + strength modifier)
        
        When an NPC/monster attacks:
        1. Use RollNonPlayerCharacterDice for attack rolls
        2. Use RollNonPlayerCharacterDice for damage rolls
        
        Always explain the purpose of each roll to the player.";

    protected override string Description => 
        "Handles all aspects of combat including attacks, damage, and tactical decisions.";

    protected override List<AITool> GetTools()
    {
        var tools = new List<AITool>();
        
        // Add dice roller tools
        tools.AddRange(_diceService.GetDiceRollerTools());
        
        // Add combat-specific tools
        tools.Add(AIFunctionFactory.Create(ApplyDamage));
        tools.Add(AIFunctionFactory.Create(CheckInitiative));
        
        return tools;
    }
    
    // ... other combat methods
}
```

## How It Works

1. **Agent Invocation**: When an AI agent determines a dice roll is needed, it invokes one of the three dice roller tools
2. **Modal Display**: The `RollDiceWindow` component displays an animated modal showing the dice
3. **Player Interaction** (for player rolls): Player clicks to roll the dice and sees the animated result
4. **Automatic Execution** (for NPC rolls): Dice roll automatically and display briefly
5. **Result Return**: The roll results are returned to the agent as structured data
6. **Agent Continuation**: The agent processes the results and continues the game flow

## Data Structures

### DieRollResults

Returned by `RollPlayerDice` and `RollNonPlayerCharacterDice`:

```csharp
public record DieRollResults(List<int> RollResults, int Total);
```

**Properties:**
- `RollResults` - List of individual die values rolled
- `Total` - Sum of all rolls plus any modifier

**Example:**
```csharp
// 3d6+2 rolled [4, 5, 3]
var result = new DieRollResults(
    RollResults: [4, 5, 3],
    Total: 14  // 4+5+3+2
);
```

### DieType Enum

Supported die types for RPG gameplay:

```csharp
public enum DieType
{
    D4 = 4,      // 4-sided die
    D6 = 6,      // 6-sided die
    D8 = 8,      // 8-sided die
    D10 = 10,    // 10-sided die
    D12 = 12,    // 12-sided die
    D20 = 20     // 20-sided die
}
```

## Common Agent Scenarios

### Skill Check
```csharp
// Agent determines a skill check is needed
var result = await RollDie("Perception Check to spot hidden door", DieType.D20, modifier: 5);
if (result >= 15)
{
    // Success - reveal information
}
```

### Attack and Damage
```csharp
// Player attacks
var attackRoll = await RollPlayerDice("Attack Roll vs Orc", DieType.D20, 1, modifier: 6);
if (attackRoll.Total >= enemyAC)
{
    var damage = await RollPlayerDice("Longsword Damage", DieType.D8, 1, modifier: 3);
    ApplyDamage(enemy, damage.Total);
}
```

### NPC Attack
```csharp
// Goblin attacks player
var attack = await RollNonPlayerCharacterDice("Goblin Attack", DieType.D20, 1, modifier: 4);
if (attack.Total >= playerAC)
{
    var damage = await RollNonPlayerCharacterDice("Scimitar Damage", DieType.D6, 1, modifier: 2);
    ApplyDamage(player, damage.Total);
}
```

### Ability Score Generation
```csharp
// Generate Strength score using 4d6 drop lowest
var rolls = await RollPlayerDice("Generate Strength Score", DieType.D6, 4, dropLowest: true);
character.Strength = rolls.Total;
```

## Technical Details

- **Target Framework**: .NET 10.0
- **Platform**: Browser (WebAssembly)
- **Key Dependencies**:
  - Microsoft.AspNetCore.Components.Web 10.0.0
  - Microsoft.Agents.AI.OpenAI 1.0.0-preview (for AI agent tools)
  - Markdig 0.43.0

## Best Practices

1. **Always provide context**: Use descriptive `reasonForRolling` parameters so players understand why they're rolling
2. **Use appropriate tool for the actor**: Player actions use `RollPlayerDice`, NPC actions use `RollNonPlayerCharacterDice`
3. **Apply modifiers in tool calls**: Pass modifiers to the tool rather than calculating after
4. **Handle async properly**: All dice roll tools are async - use `await` in agent implementations
5. **Keep roll purposes concise**: Limit `reasonForRolling` to one sentence or less for better UX

## Troubleshooting

### Tools not appearing in agent
- Verify `RollDiceService` is injected into your agent
- Ensure `GetDiceRollerTools()` is called in `GetTools()` method
- Check that service is registered in DI container

### Modal doesn't appear
- Confirm `RollDiceWindow` component is added to `MainLayout.razor`
- Verify service is registered with `AddRollDisplayService()`

### Results always null or zero
- Ensure you're using `await` when calling the tool methods
- Check that the modal isn't being closed prematurely

## Contributing

When extending this library:
1. Follow the project's coding standards (see `.github/instructions/Core-Instructions.instructions.md`)
2. Keep files under 500 lines
3. Add XML documentation for public APIs
4. Update this README with new features
5. Test with multiple die types and edge cases

