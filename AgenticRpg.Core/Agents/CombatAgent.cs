using AgenticRpg.Core.Agents.Threads;
using AgenticRpg.Core.Agents.Tools;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Repositories;
using AgenticRpg.Core.Rules;
using AgenticRpg.Core.State;
using AgenticRpg.DiceRoller.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Text;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Services;

namespace AgenticRpg.Core.Agents;

/// <summary>
/// Combat Agent manages tactical combat encounters with turn-based gameplay
/// </summary>
public class CombatAgent : BaseGameAgent
{
    private readonly CombatTools _tools;
    private readonly IRollDiceService _diceService;

    /// <summary>
    /// Combat Agent manages tactical combat encounters with turn-based gameplay
    /// </summary>
    public CombatAgent(AgentConfiguration config,
        IAgentContextProvider contextProvider,
        IGameStateManager stateManager,
        CombatRulesEngine rulesEngine,
        ICharacterRepository characterRepository,
        INarrativeRepository narrativeRepository,
        IRollDiceService diceService,
        ILoggerFactory loggerFactory,
        IAgentThreadStore threadStore, VideoGenService videoGenService) : base(config, contextProvider, AgentType.Combat, loggerFactory, threadStore)
    {
        _diceService = diceService;
        _tools = new CombatTools(stateManager, rulesEngine, characterRepository, narrativeRepository, diceService, videoGenService);
        //Agent = InitializeAgent(AgentType.Combat);
    }

    protected override string Description => "Manages tactical turn-based combat encounters by calculating attack rolls, applying damage, processing saving throws, determining initiative, resolving special abilities, and coordinating combat flow until victory conditions are met.";

    protected override IEnumerable<AITool> GetTools()
    {
        var baseTools = new List<AITool>
        {
            AIFunctionFactory.Create(_tools.ExecutePlayerWeaponAttack),
            AIFunctionFactory.Create(_tools.ExecuteMonsterWeaponAttack),
            AIFunctionFactory.Create(_tools.ExecutePlayerSpellAttack),
            AIFunctionFactory.Create(_tools.ProcessSavingThrow),
            AIFunctionFactory.Create(_tools.DetermineInitiative),
            AIFunctionFactory.Create(_tools.ResolveSpecialAbility),
            AIFunctionFactory.Create(_tools.EndCombat)
        };
        
        // Add dice roller tools
        var diceTools = _diceService.GetDiceRollerTools();
        baseTools.Add(AIFunctionFactory.Create(HandbackToGameMaster));
        
        return baseTools.Concat(diceTools);
    }

    protected override string Instructions => """

                                              Developer: # Role and Objective
                                              You are the Combat Agent for a tactical, turn-based RPG combat system. Your task is to manage combat encounters strictly by the mechanics and provide vivid, present-tense narration.
                                              
                                              # Instructions
                                              - Track initiative and manage turn order.
                                              - Use function tools to resolve all attacks, spells, abilities, damage, and saving throws.
                                              - Narrate combat actions clearly and dynamically using present tense.
                                              - Apply combat rules exactly as written, and act impartially.
                                              - End combat promptly when victory conditions are met, then return control to the Game Master.
                                              
                                              # Combat Flow
                                              1. **Start**: Set initiative using `DetermineInitiative`.
                                              2. **Each Turn**:
                                                 - Announce whose turn it is.
                                                 - Wait for player input or narrate monster actions.
                                                 - Use functions to resolve actions.
                                                 - Narrate immediate outcomes in present tense.
                                                 - Check if combat ends.
                                              3. **End**: Use `EndCombat` if victory, defeat, retreat, or surrender.
                                              
                                              # Turn Structure
                                              Each combatant's turn typically includes:
                                              - **Move**
                                              - **Action**
                                              - **Bonus Action** (if available)
                                              - **Reaction** (track between turns)
                                              
                                              # Narrative Guidelines
                                              - Use present tense for all narration.
                                              - Describe tactical context (flanking, cover, high ground).
                                              - Vary action descriptions (slashes, thrusts, crushing blows).
                                              - Show immediate consequences (wounds, reactions, exhaustion).
                                              - Build tension with close calls and turning points.
                                              - Keep combat pacing brisk.
                                              
                                              # Damage Types
                                              - Physical: Slashing, Piercing, Bludgeoning
                                              - Magical: Force, Radiant, Necrotic
                                              - Elemental: Fire, Cold, Lightning, Acid, Poison
                                              
                                              # Victory Conditions
                                              - **Victory**: All enemies at 0 HP
                                              - **Defeat**: All party members at 0 HP
                                              - **Retreat**: One side flees
                                              - **Surrender**: Opponent yields
                                              
                                              # Combat Rules
                                              - Use attack functions for weapon/spell actions.
                                              - Track temporary HP separately.
                                              - Critical hits (natural 20) double the damage dice.
                                              - Death at 0 HP (monsters die; PCs fall unconscious)
                                              - Use saving throw or special ability functions as needed.
                                              - Every action must be logged.
                                              
                                              # Monster Tactics
                                              - Use tactics that fit monster intelligence.
                                              - Target wounded or weak opponents as appropriate.
                                              - Use terrain when possible.
                                              - Intelligent monsters may flee if outmatched; mindless foes may fight to the end.
                                              
                                              # Game Context
                                              {{ $baseContext }}

                                              {{ $combatStatus }}

                                              {{ $initiativeOrder }}

                                              {{ $partyCombatants }}

                                              {{ $enemyCombatants }}

                                              {{ $recentCombatActions }}

                                              At the start of each turn, announce whose turn it is. End combat promptly with `EndCombat` when conditions are met.
                                              """;
    
    /// <summary>
    /// Builds game-state variables used to render prompt templates for combat.
    /// </summary>
    protected override Dictionary<string, object?> BuildContextVariables(GameState gameState)
    {
        var variables = base.BuildContextVariables(gameState);

        variables.TryAdd("combatStatus", BuildCombatStatus(gameState));
        variables.TryAdd("initiativeOrder", BuildInitiativeOrder(gameState));
        variables.TryAdd("partyCombatants", BuildPartyCombatants(gameState));
        variables.TryAdd("enemyCombatants", BuildEnemyCombatants(gameState));
        variables.TryAdd("recentCombatActions", BuildRecentCombatActions(gameState));

        return variables;
    }

    /// <summary>
    /// Builds the overall combat status block.
    /// </summary>
    private static string BuildCombatStatus(GameState gameState)
    {
        if (gameState.CurrentCombat is null)
        {
            return "No active combat encounter.";
        }

        return $"""
                ## Current Combat
                Round: {gameState.CurrentCombat.Round}
                Status: {gameState.CurrentCombat.Status}
                """;
    }

    /// <summary>
    /// Builds the initiative order block.
    /// </summary>
    private static string BuildInitiativeOrder(GameState gameState)
    {
        if (gameState.CurrentCombat is null || !gameState.CurrentCombat.InitiativeOrder.Any())
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine("## Initiative Order:");
        var currentTurnId = gameState.CurrentCombat.GetCurrentTurnId();

        foreach (var combatantId in gameState.CurrentCombat.InitiativeOrder)
        {
            var name = gameState.CurrentCombat.GetParticipantName(combatantId) ?? combatantId;
            var init = gameState.CurrentCombat.InitiativeRolls.GetValueOrDefault(combatantId, 0);
            var isCurrent = string.Equals(combatantId, currentTurnId, StringComparison.OrdinalIgnoreCase);
            var marker = isCurrent ? " → " : "   ";
            sb.AppendLine($"{marker}{name} (Init: {init})");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Builds the party combatant status block.
    /// </summary>
    private static string BuildPartyCombatants(GameState gameState)
    {
        if (gameState.CurrentCombat is null)
        {
            return string.Empty;
        }

        var party = gameState.CurrentCombat.PartyCharacters;
        if (!party.Any())
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine("## Party:");

        foreach (var pc in party)
        {
            var status = pc.IsAlive ? "Alive" : "Defeated";
            sb.AppendLine($"- {pc.Name}: {pc.CurrentHP}/{pc.MaxHP} HP (+{pc.CurrentHP} temp), AC {pc.ArmorClass}, Initiative {pc.Initiative} ({status})");

            var weapon = pc.Equipment.MainHand;
            if (weapon != null)
            {
                sb.Append($"  Equipped Weapon: {weapon.Name} ({weapon.DamageDice} {weapon.DamageType})");
                if (weapon.WeaponProperties.Any())
                {
                    sb.Append($" [{string.Join(", ", weapon.WeaponProperties)}]");
                }
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Builds the enemy combatant status block.
    /// </summary>
    private static string BuildEnemyCombatants(GameState gameState)
    {
        if (gameState.CurrentCombat is null)
        {
            return string.Empty;
        }

        var enemies = gameState.CurrentCombat.EnemyMonsters;
        if (!enemies.Any())
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine("## Enemies:");

        foreach (var enemy in enemies)
        {
            var status = enemy.CurrentHP > 0 ? "Alive" : "Defeated";
            var id = enemy.Name ?? "Unknown";
            var init = gameState.CurrentCombat.InitiativeRolls.TryGetValue(id, out var roll) ? roll : 0;
            sb.AppendLine($"- {enemy.Name}: {enemy.CurrentHP}/{enemy.MaxHP} HP, AC {enemy.ArmorClass}, Initiative {init} ({status})");
            sb.AppendLine($"  CR: {enemy.ChallengeRating}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Builds the recent combat log block.
    /// </summary>
    private static string BuildRecentCombatActions(GameState gameState)
    {
        if (gameState.CurrentCombat is null || !gameState.CurrentCombat.CombatLog.Any())
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine("## Recent Combat Actions:");

        var recentLog = gameState.CurrentCombat.CombatLog.TakeLast(5);
        foreach (var entry in recentLog)
        {
            sb.AppendLine($"- Round {entry.Round}: {entry.Description}");
        }

        return sb.ToString();
    }

    
}
