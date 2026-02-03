using AgenticRpg.Core.Agents.Tools.Results;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Models.Game;
using AgenticRpg.Core.Repositories;
using AgenticRpg.Core.Rules;
using AgenticRpg.Core.State;
using AgenticRpg.DiceRoller.Models;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using AgenticRpg.Core.Services;
using Location = AgenticRpg.DiceRoller.Models.Location;
// ReSharper disable All

namespace AgenticRpg.Core.Agents.Tools;

/// <summary>
/// Combat-related tools for the Combat Agent to manage tactical combat encounters
/// </summary>
public class CombatTools(
    IGameStateManager stateManager,
    CombatRulesEngine rulesEngine,
    ICharacterRepository characterRepository,
    INarrativeRepository narrativeRepository,
    IRollDiceService rollDiceService, VideoGenService videoGenService)
{
    private readonly Random _random = new();

    // Creates standard roll window options for combat dice prompts.
    private static RollDiceWindowOptions CreateRollWindowOptions(string title, Location location)
    {
        return new RollDiceWindowOptions
        {
            Title = title,
            Location = location,
            Style = "width:max-content;min-width:40vw;height:max-content"
        };
    }

    // Creates roll parameters with optional player targeting.
    private static RollDiceParameters CreateRollParameters(DieType dieType, int numberOfRolls, bool isManual, string? playerId)
    {
        var parameters = new RollDiceParameters
        {
            DieType = dieType,
            NumberOfRolls = numberOfRolls,
            IsManual = isManual
        };

        if (!string.IsNullOrWhiteSpace(playerId))
        {
            parameters.Set("PlayerId", playerId);
        }

        return parameters;
    }

    // Rolls a single d20 and returns the highest total from the window results.
    private async Task<int> RollTopD20Async(string campaignId, RollDiceWindowOptions windowOptions, bool isManual, string? playerId, int rollWindows)
    {
        var parameters = CreateRollParameters(DieType.D20, 1, isManual, playerId);
        var result = await rollDiceService.RequestDiceRoll(campaignId, parameters, windowOptions, rollWindows);
        return result.OrderByDescending(x => x?.Results?.Total).FirstOrDefault()?.Results?.Total ?? 0;
    }

    // Rolls damage dice and returns the summed total.
    private async Task<int> RollDiceSumAsync(string campaignId, RollDiceWindowOptions windowOptions, DieType dieType, int numberOfRolls, bool isManual, string? playerId)
    {
        var parameters = CreateRollParameters(dieType, numberOfRolls, isManual, playerId);
        var rolls = await rollDiceService.RequestDiceRoll(campaignId, parameters, windowOptions);
        return rolls.Sum(r => r.Results.Total);
    }

    // Serializes a CombatAttackResult error response.
    private static string SerializeCombatAttackError(string error)
    {
        return JsonSerializer.Serialize(new CombatAttackResult
        {
            Success = false,
            Message = "",
            Error = error
        });
    }

    private static int GetAttackModifier(WeaponItem weapon, int might, int agility)
    {
        var modAttribute = weapon.WeaponType == WeaponType.Ranged ? AttributeType.Agility : AttributeType.Might;
        var baseAttribute = modAttribute == AttributeType.Agility ? agility : might;
        return AttributeCalculator.CalculateModifier(baseAttribute);
    }

    [Description("Executes a melee or ranged weapon attack for a player character against an enemy monster. Parameters: attackerId (character id or name), targetId (monster name), weapon, campaignId. Returns JSON with attack roll, target AC, hit success, and damage roll if hit.")]
    public async Task<string> ExecutePlayerWeaponAttack(
        [Description("The unique ID of the combatant making the attack (attacker).")] string attackerId,
        [Description("The unique ID of the combatant being attacked (target/defender).")] string targetId,
        [Description("The name of the weapon (or weapon-like monster feature) attack being used (e.g., 'Longsword', 'Claws', 'LongBow').")] WeaponItem weapon,
        [Description("The unique ID of the campaign where this combat is occurring.")] string campaignId)
    {
        var gameState = await stateManager.GetCampaignStateAsync(campaignId);
        
        if (gameState?.CurrentCombat == null)
        {
            return SerializeCombatAttackError("No active combat");
        }

        if (gameState?.CurrentCombat.InitiativeOrder.Count == 0)
        {
            return SerializeCombatAttackError("Initiative order is empty. Invoke `DetermineInitiative` to start combat.");
        }
        var attacker = gameState.CurrentCombat.FindPartyCharacter(attackerId);
        var target = gameState.CurrentCombat.FindEnemyMonster(targetId);

        if (attacker == null || target == null)
        {
            return SerializeCombatAttackError("Attacker or target not found");
        }

        // Player attacks are always manual rolls.
        var windowOptions = CreateRollWindowOptions(
            $"{attackerId}:Roll {1}{DieType.D20} to attack {targetId} with {weapon.Name}",
            Location.Center);

        var roll = await RollTopD20Async(campaignId, windowOptions, true, attacker.PlayerId, 1);

        var attackModifier = StatsCalculator.CalculateAttackBonus(attacker, weapon);
        if (attacker.Skills.TryGetValue("Melee Combat", out var meleeSkill) && weapon.WeaponType is WeaponType.Melee or WeaponType.Both)
        {
            attackModifier += meleeSkill;
        }
        else if (attacker.Skills.TryGetValue("Ranged Combat", out var rangeSkill) && weapon.WeaponType is WeaponType.Ranged or WeaponType.Both)
        {
            attackModifier += rangeSkill;
        }
        // Roll attack
        var attackResult = rulesEngine.ResolveAttack(attackModifier, target.ArmorClass, roll);
        var isHit = attackResult.IsHit;
        var isCritical = attackResult.IsCritical;

        var damage = 0;
        if (isHit)
        {
            var dieType = weapon.GetDieType();
            var numberOfDice = weapon.GetDieRollCount();

            var damageWindowOptions = CreateRollWindowOptions(
                $"Roll {numberOfDice}{dieType} for {weapon.Name} damage",
                Location.Center);

            var rawRollValue = await RollDiceSumAsync(campaignId, damageWindowOptions, dieType, numberOfDice, true, attacker.PlayerId);
            if (isCritical) rawRollValue *= 2;

            damage = rawRollValue + attackModifier + weapon.GetDieRollBonus();
            target.CurrentHP = Math.Max(0, target.CurrentHP - damage);
        }

        var targetDefeated = target.CurrentHP <= 0;
        gameState.CurrentCombat.CurrentTurnIndex++;
        // Add to combat log
        gameState.CurrentCombat.CombatLog.Add(new CombatLogEntry
        {
            Round = gameState.CurrentCombat.Round,
            CombatantId = attackerId,
            CombatantName = attacker.Name,
            ActionType = "Attack",
            Description = $"{attacker.Name} attacks {target.Name} with {weapon.Name} " + (isHit ? $"and hits for {damage} damage!" : "but misses. (sad face)"),
            TargetId = targetId,
            TargetName = target.Name,
            RollResult = attackResult.Total,
            IsCritical = isCritical,
            DamageDealt = damage
        });

        await stateManager.UpdateCampaignStateAsync(gameState);

        return JsonSerializer.Serialize(new CombatAttackResult
        {
            Success = true,
            AttackerName = attacker.Name,
            TargetName = target.Name,
            SourceName = weapon.Name,
            SourceType = "Weapon",
            AttackRoll = attackResult.Roll,
            AttackModifier = attackModifier,
            TotalAttack = attackResult.Total,
            TargetAC = target.ArmorClass,
            IsHit = isHit,
            IsCritical = isCritical,
            Damage = damage,
            TargetHP = target.CurrentHP,
            TargetMaxHP = target.MaxHP,
            TargetDefeated = targetDefeated,
            Message = isHit
                ? (isCritical
                    ? $"Critical hit! Attack {attackResult.Total} vs AC {target.ArmorClass}. Damage {damage}."
                    : $"Hit! Attack {attackResult.Total} vs AC {target.ArmorClass}. Damage {damage}.")
                : $"Miss. Attack {attackResult.Total} vs AC {target.ArmorClass}."
        });
    }

    [Description("Executes a melee or ranged weapon attack for an enemy monster against a player character. Parameters: attackerId (monster name), targetId (character id or name), weapon, campaignId. Returns JSON with attack roll, target AC, hit success, and damage roll if hit.")]
    public async Task<string> ExecuteMonsterWeaponAttack(
        [Description("The monster name (attacker). Must match an enemy monster in the current combat.")] string attackerId,
        [Description("The character id or name (target/defender). Must match a party character in the current combat.")] string targetId,
        [Description("The monster's chosen attack represented as a WeaponItem (e.g., Claws/Bite mapped to a weapon-like attack).")] WeaponItem weapon,
        [Description("The unique ID of the campaign where this combat is occurring.")] string campaignId)
    {
        var gameState = await stateManager.GetCampaignStateAsync(campaignId);
        if (gameState?.CurrentCombat == null)
        {
            return SerializeCombatAttackError("No active combat");
        }
        if (gameState?.CurrentCombat.InitiativeOrder.Count == 0)
        {
            return SerializeCombatAttackError("Initiative order is empty. Invoke `DetermineInitiative` to start combat.");
        }
        var attacker = gameState.CurrentCombat.FindEnemyMonster(attackerId);
        var target = gameState.CurrentCombat.FindPartyCharacter(targetId);

        if (attacker == null || target == null)
        {
            return SerializeCombatAttackError("Attacker or target not found");
        }
        if (attacker.CurrentHP <= 0)
        {
            return SerializeCombatAttackError("Attacker is defeated");
        }

        var windowOptions = CreateRollWindowOptions(
            $"{attacker.Name}:Roll {1}{DieType.D20} to attack {target.Name} with {weapon.Name}",
            Location.TopRight);

        var roll = await RollTopD20Async(campaignId, windowOptions, false, null, 1);

        var attackModifier = GetAttackModifier(weapon, attacker.Might, attacker.Agility);

        var attackResult = rulesEngine.ResolveAttack(attackModifier, target.ArmorClass, roll);
        var isHit = attackResult.IsHit;
        var isCritical = attackResult.IsCritical;

        var damage = 0;
        if (isHit)
        {
            var dieType = weapon.GetDieType();
            var numberOfDice = weapon.GetDieRollCount();

            var damageWindowOptions = CreateRollWindowOptions(
                $"Roll {numberOfDice}{dieType} for {weapon.Name} damage",
                Location.Center);

            var rawRollValue = await RollDiceSumAsync(campaignId, damageWindowOptions, dieType, numberOfDice, false, null);
            if (isCritical) rawRollValue *= 2;

            damage = rawRollValue + attackModifier + weapon.GetDieRollBonus();

            // Apply to TempHP first.
            if (target.CurrentHP > 0)
            {
                var tempLoss = Math.Min(target.CurrentHP, damage);
                target.CurrentHP -= tempLoss;
                damage -= tempLoss;
            }

            if (damage > 0)
            {
                target.CurrentHP = Math.Max(0, target.CurrentHP - damage);
            }
        }

        var targetDefeated = target.CurrentHP <= 0;
        gameState.CurrentCombat.CurrentTurnIndex++;
        gameState.CurrentCombat.CombatLog.Add(new CombatLogEntry
        {
            Round = gameState.CurrentCombat.Round,
            CombatantId = attacker.Name ?? attackerId,
            CombatantName = attacker.Name ?? attackerId,
            ActionType = "Attack",
            Description = $"{attacker.Name} attacks {target.Name} with {weapon.Name} " + (isHit ? $"and hits for {damage} damage! Ouch!" : "but misses. (Ha!)"),
            TargetId = target.Id,
            TargetName = target.Name,
            RollResult = attackResult.Total,
            IsCritical = isCritical,
            DamageDealt = damage
        });

        await stateManager.UpdateCampaignStateAsync(gameState);

        return JsonSerializer.Serialize(new CombatAttackResult
        {
            Success = true,
            AttackerName = attacker.Name ?? attackerId,
            TargetName = target.Name,
            SourceName = weapon.Name,
            SourceType = "Weapon",
            AttackRoll = attackResult.Roll,
            AttackModifier = attackModifier,
            TotalAttack = attackResult.Total,
            TargetAC = target.ArmorClass,
            IsHit = isHit,
            IsCritical = isCritical,
            Damage = damage,
            TargetHP = target.CurrentHP,
            TargetMaxHP = target.MaxHP,
            TargetDefeated = targetDefeated,
            Message = isHit
                ? (isCritical
                    ? $"Critical hit! Attack {attackResult.Total} vs AC {target.ArmorClass}. Damage {damage}."
                    : $"Hit! Attack {attackResult.Total} vs AC {target.ArmorClass}. Damage {damage}.")
                : $"Miss. Attack {attackResult.Total} vs AC {target.ArmorClass}."
        });
    }

    public async Task<string> ExecutePlayerSpellAttack(
        [Description("The unique ID of the combatant casting the spell (attacker).")] string attackerId,
        [Description("The unique ID of the combatant being attacked (target/defender).")] string targetId,
        [Description("The name of the spell being used (e.g., 'Fireball', 'Magic Missile').")] Spell spell,

        [Description("The unique ID of the campaign where this combat is occurring.")] string campaignId)
    {
        var gameState = await stateManager.GetCampaignStateAsync(campaignId);
        if (gameState?.CurrentCombat == null)
        {
            return JsonSerializer.Serialize(new CombatAttackResult
            {
                Success = false,
                Message = "",
                Error = "No active combat"
            });
        }
        if (gameState?.CurrentCombat.InitiativeOrder.Count == 0)
        {
            return JsonSerializer.Serialize(new CombatAttackResult
            {
                Success = false,
                Message = "",
                Error = "Initiative order is empty. Invoke `DetermineInitiative` to start combat."
            });
        }
        var attacker = gameState.CurrentCombat.FindPartyCharacter(attackerId);
        var target = gameState.CurrentCombat.FindEnemyMonster(targetId);
        
        if (attacker == null || target == null)
        {
            return JsonSerializer.Serialize(new CombatAttackResult
            {
                Success = false,
                Message = "",
                Error = "Attacker or target not found"
            });
        }
        var attackerSpell = attacker.KnownSpells.FirstOrDefault(s => s.Name == spell.Name)!;
        var spellCost = attackerSpell.Level + 1;
        if (attacker.CurrentMP < spellCost)
        {
            return JsonSerializer.Serialize(new CombatAttackResult
            {
                Success = false,
                Message = "",
                Error = "Not enough MP"
            });
        }
        attacker.CurrentMP -= spellCost;
        // Spells always hit. The target may make a saving throw to reduce effects.
        // # Reason: The system is being simplified away from attack-vs-AC spell mechanics.

        var spellEffect = attackerSpell.Effect;

        // Default spellcasting modifier to Wits for player casters.
        var spellcastingModifier = AttributeCalculator.CalculateModifier(attacker.Attributes[AttributeType.Wits]);

        // Default save DC: 8 + spellcasting modifier.
        // # Reason: We don't currently store spell save DCs per spell/effect.
        var saveDc = 8 + spellcastingModifier;

        // Pick a save attribute.
        // If the effect declares a damage type we can infer a reasonable default, otherwise use Vitality.
        var saveAttribute = spellEffect.SavingThrowDamageType switch
        {
            DamageType.Fire or DamageType.Cold or DamageType.Lightning or DamageType.Poison => AttributeType.Vitality,
            DamageType.Necrotic or DamageType.Radiant or DamageType.Magical => AttributeType.Wits,
            DamageType.Psychic => AttributeType.Presence,
            DamageType.Force or DamageType.Thunder => AttributeType.Might,
            DamageType.Acid => AttributeType.Agility,
            _ => AttributeType.Vitality
        };

        // Roll target saving throw (auto, non-player).
        var saveWindowOptions = new RollDiceWindowOptions
        {
            Title = $"{target.Name}:Roll 1{DieType.D20} to resist {attackerSpell.Name} (DC {saveDc} {saveAttribute})",
            Location = Location.TopRight,
            Style = "width:max-content;min-width:40vw;height:max-content"
        };

        var saveRoll = await RollTopD20Async(campaignId, saveWindowOptions, false, attacker.PlayerId, 1);

        var inferredSaveModifier = saveAttribute switch
        {
            AttributeType.Might => AttributeCalculator.CalculateModifier(target.Might),
            AttributeType.Agility => AttributeCalculator.CalculateModifier(target.Agility),
            AttributeType.Vitality => AttributeCalculator.CalculateModifier(target.Vitality),
            AttributeType.Wits => AttributeCalculator.CalculateModifier(target.Wits),
            AttributeType.Presence => AttributeCalculator.CalculateModifier(target.Presence),
            _ => 0
        };

        var saveTotal = saveRoll + inferredSaveModifier;
        var saveSucceeded = saveTotal >= saveDc;

        var damage = 0;
        var rawEffectMagnitude = spellEffect.EffectMagnitude;
        var appliedEffectMagnitude = rawEffectMagnitude;

        // Resolve damage only when the spell has dice.
        var levelEffectDice = spellEffect.GetLevelEffectDice(attacker.Level);
        if (levelEffectDice is { Length: > 0 })
        {
            var parts = levelEffectDice.Trim().ToLowerInvariant().Split('d', '+');
            if (parts.Length >= 2 &&
                int.TryParse(parts[0], out var diceCount) &&
                Enum.TryParse<DieType>(parts[1], out var diceSides))
            {
                var damageWindowOptions = new RollDiceWindowOptions
                {
                    Title = $"Roll {diceCount}{diceSides} for {attackerSpell.Name} damage",
                    Location = Location.Center,
                    Style = "width:max-content;min-width:40vw;height:max-content"
                };

                // Reason: Player spell damage dice should only show for the casting player.
                var rawRollValue = await RollDiceSumAsync(campaignId, damageWindowOptions, diceSides, diceCount, true, attacker.PlayerId);
                if (parts.Length > 2 && int.TryParse(parts[2], out var bonus))
                {
                    // Add any flat bonus to damage magnitude.
                    rawRollValue += bonus;
                }
                if (saveSucceeded)
                {
                    // Success on the save halves the damage and magnitude.
                    rawRollValue = (int)Math.Floor(rawRollValue / 2.0);
                    appliedEffectMagnitude = (int)Math.Floor(rawEffectMagnitude / 2.0);
                }

                damage = rawRollValue + appliedEffectMagnitude + 50;
                target.CurrentHP = Math.Max(0, target.CurrentHP - damage);
            }
        }
        else
        {
            if (saveSucceeded)
            {
                appliedEffectMagnitude = (int)Math.Floor(rawEffectMagnitude / 2.0);
            }
        }

        var targetDefeated = target.CurrentHP <= 0;
        gameState.CurrentCombat.CurrentTurnIndex++;
        gameState.CurrentCombat.CombatLog.Add(new CombatLogEntry
        {
            Round = gameState.CurrentCombat.Round,
            CombatantId = attackerId,
            CombatantName = attacker.Name,
            ActionType = "Spell",
            Description = saveSucceeded
                ? $"{attacker.Name} casts {attackerSpell.Name} at {target.Name}. {target.Name} resists (Save {saveTotal} vs DC {saveDc}) - total damage: {damage}"
                : $"{attacker.Name} casts {attackerSpell.Name} at {target.Name}. {target.Name} fails to resist (Save {saveTotal} vs DC {saveDc}) - total damage: {damage}",
            TargetId = targetId,
            TargetName = target.Name,
            RollResult = saveTotal,
            IsCritical = false,
            DamageDealt = damage
        });

        await stateManager.UpdateCampaignStateAsync(gameState);

        return JsonSerializer.Serialize(new CombatAttackResult
        {
            Success = true,
            AttackerName = attacker.Name,
            TargetName = target.Name,
            SourceName = attackerSpell.Name,
            SourceType = "Spell",
            AttackRoll = 0,
            AttackModifier = spellcastingModifier,
            TotalAttack = 0,
            TargetAC = target.ArmorClass,
            IsHit = true,
            IsCritical = false,
            Damage = damage,
            TargetHP = target.CurrentHP,
            TargetMaxHP = target.MaxHP,
            TargetDefeated = targetDefeated,
            Message = damage > 0
                ? (saveSucceeded
                    ? $"{attackerSpell.Name} hits automatically. Save succeeds ({saveTotal} vs DC {saveDc}) so damage is reduced to {damage}."
                    : $"{attackerSpell.Name} hits automatically. Save fails ({saveTotal} vs DC {saveDc}) so damage is {damage}.")
                : (saveSucceeded
                    ? $"{attackerSpell.Name} hits automatically. Save succeeds ({saveTotal} vs DC {saveDc}) so the effect is reduced."
                    : $"{attackerSpell.Name} hits automatically. Save fails ({saveTotal} vs DC {saveDc}).")
        });
    }

    
    [Description("Processes a saving throw for a combatant against a spell or effect. Parameters: characterId (combatant making save), savingThrowType (Might/Agility/Vitality/Wits/Presence), difficultyClass (DC to beat), hasAdvantage (true if advantage), campaignId. Returns JSON with save roll, DC, and success/failure.")]
    public async Task<string> ProcessSavingThrow(
        [Description("The unique character ID of the combatant making the saving throw.")] string characterId,
        [Description("The attribute used for this saving throw. Must be one of: Might, Agility, Vitality, Wits, Presence.")] string savingThrowType,
        [Description("The Difficulty Class (DC) that the saving throw must meet or exceed to succeed. Typical values: Easy=10, Moderate=12, Hard=15, Very Hard=20.")] int difficultyClass,
        [Description("Whether the character has advantage on this saving throw (roll twice, take higher). True if advantage, false otherwise.")] bool hasAdvantage,
        [Description("The unique ID of the campaign where this combat is occurring.")] string campaignId)
    {
        var gameState = await stateManager.GetCampaignStateAsync(campaignId);
        if (gameState?.CurrentCombat == null)
        {
            return JsonSerializer.Serialize(new SavingThrowResult
            {
                Success = false,
                Message = "",
                Error = "No active combat"
            });
        }

        var character = gameState.CurrentCombat.FindPartyCharacter(characterId);
        if (character == null)
        {
            return JsonSerializer.Serialize(new SavingThrowResult
            {
                Success = false,
                Message = "",
                Error = "Character not found"
            });
        }

        // Get attribute modifier for save
        var saveModifier = 0;
        if (Enum.TryParse<AttributeType>(savingThrowType, true, out var attributeType))
        {
            var attributeValue = character.Attributes[attributeType];
            saveModifier = AttributeCalculator.CalculateModifier(attributeValue);
        }

        // Roll save
        var isSuccess = rulesEngine.ResolveSavingThrow(saveModifier, difficultyClass, hasAdvantage);

        // Calculate the roll total for display (simplified since ResolveSavingThrow returns bool)
        var saveRoll = _random.Next(1, 21); // d20
        var saveTotal = saveRoll + saveModifier;

        // Add to combat log
        gameState.CurrentCombat.CombatLog.Add(new CombatLogEntry
        {
            Round = gameState.CurrentCombat.Round,
            CombatantId = character.Id,
            CombatantName = character.Name,
            ActionType = "SavingThrow",
            Description = $"{character.Name} makes a {savingThrowType} saving throw",
            RollResult = saveTotal
        });

        await stateManager.UpdateCampaignStateAsync(gameState);

        return JsonSerializer.Serialize(new SavingThrowResult
        {
            Success = true,
            CharacterName = character.Name,
            SaveType = savingThrowType,
            SaveRoll = saveRoll,
            SaveModifier = saveModifier,
            TotalSave = saveTotal,
            DifficultyClass = difficultyClass,
            IsSuccess = isSuccess,
            Message = isSuccess ?
                $"Success! Save: {saveTotal} vs DC {difficultyClass}" :
                $"Failure. Save: {saveTotal} vs DC {difficultyClass}"
        });
    }

    [Description("Determines initiative order for all combatants at the start of combat. Parameters: combatantIds (array of all combatant IDs), campaignId. Returns JSON with initiative order sorted from highest to lowest.")]
    public async Task<string> DetermineInitiative(
        [Description("Array of all combatant IDs participating in this combat encounter. Include both player characters and monsters/enemies.")] string[] combatantIds,
        [Description("The unique ID of the campaign where this combat is occurring.")] string campaignId)
    {
        var gameState = await stateManager.GetCampaignStateAsync(campaignId);
        if (gameState?.CurrentCombat == null)
        {
            return JsonSerializer.Serialize(new InitiativeResult
            {
                Success = false,
                InitiativeOrder = [],
                Message = "",
                Error = "No active combat"
            });
        }

        var initiativeResults = new List<(string Id, string Name, int Initiative)>();
        gameState.CurrentCombat.InitiativeRolls.Clear();

        foreach (var participantId in combatantIds)
        {
            if (string.IsNullOrWhiteSpace(participantId)) continue;

            var character = gameState.CurrentCombat.FindPartyCharacter(participantId);
            if (character != null)
            {
                var initiativeModifier = AttributeCalculator.CalculateModifier(character.Attributes[AttributeType.Agility]);
                var initiativeRoll = rulesEngine.RollInitiative(initiativeModifier);
                character.Initiative = initiativeRoll;
                gameState.CurrentCombat.InitiativeRolls[character.Id] = initiativeRoll;
                initiativeResults.Add((character.Id, character.Name, initiativeRoll));
                continue;
            }

            var monster = gameState.CurrentCombat.FindEnemyMonster(participantId);
            if (monster != null)
            {
                var initiativeModifier = monster.InitiativeBonus;
                var initiativeRoll = rulesEngine.RollInitiative(initiativeModifier);
                var monsterId = monster.Name ?? participantId;
                gameState.CurrentCombat.InitiativeRolls[monsterId] = initiativeRoll;
                initiativeResults.Add((monsterId, monster.Name ?? monsterId, initiativeRoll));
            }
        }

        // Sort by initiative (highest first)
        var sortedInitiative = initiativeResults.OrderByDescending(i => i.Initiative).ToList();
        gameState.CurrentCombat.InitiativeOrder = sortedInitiative.Select(i => i.Id).ToList();
        gameState.CurrentCombat.CurrentTurnIndex = 0;

        await stateManager.UpdateCampaignStateAsync(gameState);

        return JsonSerializer.Serialize(new InitiativeResult
        {
            Success = true,
            InitiativeOrder = sortedInitiative.Select(i => new InitiativeEntry
            {
                CombatantId = i.Id,
                Name = i.Name,
                Initiative = i.Initiative
            }).ToList(),
            CurrentTurn = sortedInitiative.Count != 0 ? sortedInitiative.FirstOrDefault().Name : "",
            Message = $"Initiative order determined. {sortedInitiative.FirstOrDefault()} goes first!"
        });
    }

    [Description("Resolves a special ability, spell, or monster power used by a combatant. Parameters: characterId (user of ability), abilityName (name of ability/non-combat spell), targetIds (array of target combatant IDs), campaignId. Returns JSON with ability effects and results.")]
    public async Task<string> ResolveSpecialAbility(
        [Description("The unique character ID or combatant ID of the entity using the special ability.")] string characterId,
        [Description("The name of the non-damage/utility special ability, spell, or power being used (e.g., 'Blindness', 'Inspire Fear').")] string abilityName,
        [Description("Array of combatant IDs targeted by this ability. Can be empty for self-targeting abilities or single/multiple targets depending on the ability.")] string[] targetIds,
        [Description("The unique ID of the campaign where this combat is occurring.")] string campaignId)
    {
        var gameState = await stateManager.GetCampaignStateAsync(campaignId);
        if (gameState?.CurrentCombat == null)
        {
            return JsonSerializer.Serialize(new SpecialAbilityResult
            {
                Success = false,
                Message = "",
                Error = "No active combat"
            });
        }

        var userCharacter = gameState.CurrentCombat.FindPartyCharacter(characterId);
        var userMonster = gameState.CurrentCombat.FindEnemyMonster(characterId);

        if (userCharacter == null && userMonster == null)
        {
            return JsonSerializer.Serialize(new SpecialAbilityResult
            {
                Success = false,
                Message = "",
                Error = "Combatant not found"
            });
        }

        // Add to combat log
        var targetNames = targetIds.Select(id => gameState.CurrentCombat.GetParticipantName(id) ?? "Unknown").ToArray();

        var userId = userCharacter?.Id ?? userMonster?.Name ?? characterId;
        var userName = userCharacter?.Name ?? userMonster?.Name ?? characterId;
        gameState.CurrentCombat.CurrentTurnIndex++;
        gameState.CurrentCombat.CombatLog.Add(new CombatLogEntry
        {
            Round = gameState.CurrentCombat.Round,
            CombatantId = userId,
            CombatantName = userName,
            ActionType = "SpecialAbility",
            Description = $"{userName} uses {abilityName} targeting {string.Join(", ", targetNames)}"
        });

        await stateManager.UpdateCampaignStateAsync(gameState);

        // Note: Actual ability resolution would require ability database
        return JsonSerializer.Serialize(new SpecialAbilityResult
        {
            Success = true,
            UserName = userName,
            AbilityName = abilityName,
            Targets = targetNames,
            Message = $"{userName} uses {abilityName}! Resolve ability effects manually or implement ability-specific logic."
        });
    }

    [Description("Ends the combat encounter, determines victor, awards XP and loot, and returns control to Game Master. Parameters: victor (Party/Enemies/Draw), rewards (JSON string with XP and loot), campaignId. Returns JSON confirming combat end and transition back to GameMaster.")]
    public async Task<string> EndCombat([Description("Your reason for ending the combat. Did it actually play out to a conclusion? What steps lead to the combat ending?")] string justification, [Description("The victor of the combat encounter. Must be one of: Party (players won), Enemies (monsters won), Draw (stalemate or retreat).")] CombatVictor victor, [Description("Object describing rewards from the combat, including XP amounts and loot items earned by the party.")] CombatRewards rewards,
        [Description("The unique ID of the campaign where this combat occurred.")] string campaignId)
    {
        var gameState = await stateManager.GetCampaignStateAsync(campaignId);
        if (gameState?.CurrentCombat == null)
        {
            return JsonSerializer.Serialize(new EndCombatResult
            {
                Success = false,
                Message = "",
                Error = "No active combat"
            });
        }
        if (!TryEndCombat(gameState.CurrentCombat, victor, out var message))
        {
            return JsonSerializer.Serialize(new EndCombatResult
            {
                Success = false,
                Message = "",
                Error = message
            });
        }

        var combatEncounterSnapshot = gameState.CurrentCombat;

        // Update combat status
        gameState.CurrentCombat.Status = CombatStatus.Ended;
        gameState.CurrentCombat.EndedAt = DateTime.UtcNow;

        // Record combat end narrative
        var narrative = new Narrative
        {
            CampaignId = campaignId,
            Content = $"Combat ended. Reason: {justification}.\nVictor: {victor}. Rewards:\n{rewards.AsMarkdown()}",
            Type = "Combat",
            Visibility = NarrativeVisibility.Global,
            AgentType = "Combat",
            Timestamp = DateTime.UtcNow
        };
        await narrativeRepository.CreateAsync(narrative);
        gameState.RecentNarratives.Add(narrative);
        var sb = new StringBuilder();
        sb.AppendLine($"**Combat ended. {victor} victorious!**");
        sb.AppendLine("\n## Recent Combat Actions:");
        var recentLog = gameState.CurrentCombat.CombatLog.TakeLast(8);
        foreach (var entry in recentLog)
        {
            sb.AppendLine($"- Round {entry.Round}: {entry.Description}");
        }

        
        // Update character HP/TempHP in repository for survivors
        var gold = rewards.Gold;
        var goldPerPlayer = gold / Math.Max(1, gameState.CurrentCombat.PartyCharacters.Count);
        foreach (var partyMember in gameState.CurrentCombat.PartyCharacters)
        {
            var character = await characterRepository.GetByIdAsync(partyMember.Id);
            if (character != null)
            {
                character.CurrentHP = partyMember.CurrentHP;
                character.CurrentHP = partyMember.CurrentHP;
                character.Gold += goldPerPlayer;
                //character.Experience += rewards.ExperiencePoints;
                await characterRepository.UpdateAsync(character);
            }
        }

        rewards.Gold = 0;
        gameState.PartyCombatRewards.Add(rewards);
        sb.AppendLine("## Loot");
        sb.AppendLine(rewards.AsMarkdown());
        gameState.Metadata["HandoffContext"] = sb.ToString();
        // Return control to GameMaster
        gameState.ActiveAgent = "GameMaster";
        gameState.CurrentCombat = null;

        await stateManager.UpdateCampaignStateAsync(gameState);

        _ = Task.Run(async () =>
        {
            try
            {
                await GenerateVideoAndAddToCampaignNarrative(combatEncounterSnapshot, gameState, stateManager);
            }
            catch (Exception ex)
            {
                // Reason: Background video generation should never block combat completion.
                Console.Error.WriteLine($"Combat video generation failed: {ex}");
            }
        });
        

        return JsonSerializer.Serialize(new EndCombatResult
        {
            Success = true,
            Victor = victor.ToString(),
            Rewards = rewards,
            Message = $"Combat ended. {victor} victorious. Returning to Game Master.",
            ActiveAgent = "GameMaster"
        });
    }

    private async Task GenerateVideoAndAddToCampaignNarrative(CombatEncounter combatEncounter, GameState gameState, IGameStateManager stateManager)
    {
        var src = await VideoGenService.GenerateSoraVideo(combatEncounter, gameState.CampaignId);
        if (!string.IsNullOrEmpty(src))
        {
            var videoHtml = AddVideoHtml(src);
            var narrative = new Narrative
            {
                CampaignId = gameState.CampaignId,
                Content = videoHtml,
                Type = "CombatVideo",
                Visibility = NarrativeVisibility.Global,
                AgentType = "Combat",
                Timestamp = DateTime.UtcNow
            };
            await narrativeRepository.CreateAsync(narrative);
            gameState.RecentNarratives.Add(narrative);
            await stateManager.UpdateCampaignStateAsync(gameState);
        }
    }

    private static string AddVideoHtml(string src)
    {
        return $"""
                <video controls>
                    <source src="{src}" type="video/mp4">
                    Your browser does not support the video tag.
                </video>
                """;
    }
    
    private static bool TryEndCombat(CombatEncounter encounter, CombatVictor victor, out string message)
    {
        if (encounter == null)
        {
            message = "No active combat";
            return false;
        }
        switch (victor)
        {
            case CombatVictor.Party when !encounter.AllEnemiesDefeated():
                message = $"Cannot end combat in favor of {victor}: Not all enemies are defeated.";
                return false;
            case CombatVictor.Enemies when !encounter.AllPartyDefeated():
                message = $"Cannot end combat in favor of {victor}: Not all party members are defeated.";
                return false;
            default:
                message = "";
                return true;
        }
    }
}

public class CombatRewards
{
    public int Gold { get; set; }
    public int ExperiencePoints { get; set; }
    public List<InventoryItem> SimpleLoot { get; set; } = [];
    public List<WeaponItem> WeaponsLoot { get; set; } = [];
    public List<ArmorItem> ArmorLoot { get; set; } = [];
    public List<MagicItem> MagicLoot { get; set; } = [];

    public string AsMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"- Gold: {Gold}");
        sb.AppendLine($"- Experience Points: {ExperiencePoints}");
        if (SimpleLoot.Count != 0)
        {
            sb.AppendLine("- Simple Items:");
            foreach (var item in SimpleLoot)
            {
                sb.AppendLine($"  - {item.Name} x{item.Quantity}");
            }
        }
        if (WeaponsLoot.Count != 0)
        {
            sb.AppendLine("- Weapons:");
            foreach (var item in WeaponsLoot)
            {
                sb.AppendLine($"  - {item.Name} x{item.Quantity}");
            }
        }
        if (ArmorLoot.Count != 0)
        {
            sb.AppendLine("- Armor:");
            foreach (var item in ArmorLoot)
            {
                sb.AppendLine($"  - {item.Name} x{item.Quantity}");
            }
        }
        if (MagicLoot.Count != 0)
        {
            sb.AppendLine("- Magic Items:");
            foreach (var item in MagicLoot)
            {
                sb.AppendLine($"  - {item.Name} x{item.Quantity}");
            }
        }
        return sb.ToString();
    }
}
