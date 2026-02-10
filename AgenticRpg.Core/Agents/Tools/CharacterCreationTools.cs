using System.ClientModel;
using AgenticRpg.Core.Agents.Llms;
using AgenticRpg.Core.Agents.Tools.Results;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Repositories;
using AgenticRpg.Core.Rules;
using AgenticRpg.Core.State;
using AgenticRpg.DiceRoller.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using System.ComponentModel;
using System.Text.Json;
using AgenticRpg.Core.Models.Game;
using AgenticRpg.Core.Services;
using ChatResponseFormat = Microsoft.Extensions.AI.ChatResponseFormat;
using Location = AgenticRpg.DiceRoller.Models.Location;

#pragma warning disable OPENAI001

namespace AgenticRpg.Core.Agents.Tools;

/// <summary>
/// Character creation tools for the CharacterCreationAgent.
/// Each method is decorated with [Description] for AI function calling.
/// These tools save data progressively to session state for real-time display and recovery.
/// </summary>
public class CharacterCreationTools(
    ICharacterRepository characterRepository,
    ISessionStateManager sessionStateManager,
    AgentConfiguration configuration, IRollDiceService rollDiceService)
{
    private const string? QuickCreateInstructions = $"""
                                                    Using the provided character concept, create a detailed character profile.

                                                    ## Character Requirements
                                                    #### Race Selection
                                                    Present the 6 available races and use **SaveRaceChoice** to save their choice:
                                                    - Humans - Versatile, +1 to any attribute, bonus skill rank
                                                    - Duskborn - Shadow-affiliated, +2 Agility/-1 Might, teleport ability
                                                    - Ironforged - Construct-like, +2 Vitality/-1 Agility, poison resistance
                                                    - Wildkin - Beast-connected, +1 Agility/+1 Presence/-1 Wits, animal communication
                                                    - Emberfolk - Fire-touched, +2 Wits/-1 Vitality, fire resistance
                                                    - Stoneborn - Earth-born, +1 Vitality/+1 Might/-1 Agility, damage reduction

                                                    #### Class Selection
                                                    Present the 6 available classes and use **SaveClassChoice** to save their choice:
                                                    - Cleric (d8 HP, d6 MP) - Divine magic, healing, medium armor
                                                    - Wizard (d6 HP, d8 MP) - Arcane magic, spellbook, no armor
                                                    - Warrior (d10 HP, no MP) - Combat expert, all weapons and armor
                                                    - Rogue (d8 HP, no MP) - Stealth, skills, light armor
                                                    - Paladin (d10 HP, d4 MP) - Holy warrior, heavy armor, limited spells
                                                    - War Mage (d8 HP, d6 MP) - Spellsword, medium armor, combat magic

                                                    #### Attribute Allocation
                                                    - **Standard Array**: 15, 14, 13, 12, 10 (assign to five attributes)

                                                    The five attributes are: Might, Agility, Vitality, Wits, Presence
                                                    Racial bonuses are automatically applied when saved.

                                                    #### Available Skills and Spells
                                                    Use the following skill list when assigning skills:
                                                    
                                                    {SkillList}
                                                    
                                                    If the character is a spellcaster (Wizard, Cleric, Paladin, WarMage), select appropriate starting spells from these lists:
                                                    **Wizard or WarMage Spells:**
                                                    {WizardSpellList}
                                                    
                                                    **Cleric or Paladin Spells (Light Domain):**
                                                    {LightClericOrPaladinSpellList}
                                                    
                                                    **Cleric or Paladin Spells (Shadow Domain):**
                                                    {DarkClericOrPaladinSpellList}

                                                    #### Step 5: Background (Optional but Recommended)
                                                    Create a colorful backstory, personality, and motivations for the character.

                                                    ---

                                                    """;

    [Description("Find a character owned by the player by name to modify or complete")]
    public async Task<string> LoadCharacterToModify([Description("The unique session ID for this character creation session. This tracks the character being created.")] string sessionId, [Description("The unique player ID for this character creation session.")] string playerId, [Description("The name of the character to modify or complete.")] string characterName)
    {
        var session = await sessionStateManager.GetSessionStateAsync(sessionId);
        if (session == null)
        {
            return JsonSerializer.Serialize(new CharacterSheetResult
            {
                Valid = false,
                Error = "Session not found"
            });
        }
        var characters = await characterRepository.GetByPlayerIdAsync(playerId);
        var character = characters.FirstOrDefault(c => c.Name.Equals(characterName, StringComparison.OrdinalIgnoreCase));
        if (character == null)
        {
            return JsonSerializer.Serialize(new CharacterSheetResult
            {
                Valid = false,
                Error = "Character not found"
            });
        }
        session.Context.DraftCharacter = character;
        session.Context.CurrentStep = "modify";

        await sessionStateManager.UpdateSessionStateAsync(session);
        return JsonSerializer.Serialize(new CharacterSheetResult
        {
            Valid = true,
            Character = character
        });
    }

    [Description("Saves the chosen race to the character in progress and returns the race details including attribute modifiers, movement speed, special abilities, and available classes. Valid races are: Humans, Duskborn, Ironforged, Wildkin, Emberfolk, Stoneborn. Requires sessionId.")]
    public async Task<string> SaveRaceChoice(
        [Description("The unique session ID for this character creation session. This tracks the character being created.")] string sessionId,
        [Description("The race to assign to the character. Must be one of: Humans, Duskborn, Ironforged, Wildkin, Emberfolk, Stoneborn.")] CharacterRace race)
    {
        var validRaces = new Dictionary<CharacterRace, RaceDetails>
        {
            [CharacterRace.Human] = new()
            {
                Name = "Humans",
                AttributeBonus = new AttributeBonuses { Any = 1 },
                MovementSpeed = 30,
                SpecialAbility = "Versatile Nature - Gain one additional skill rank at first level",
                AvailableClasses = ["All"]
            },
            [CharacterRace.Duskborn] = new()
            {
                Name = "Duskborn",
                AttributeBonus = new AttributeBonuses { Agility = 2, Might = -1 },
                MovementSpeed = 30,
                SpecialAbility = "Shadow Step - Once per short rest, teleport 15 feet to an unoccupied space you can see in dim light or darkness",
                AvailableClasses = ["Rogue", "Wizard", "War Mage"]
            },
            [CharacterRace.Ironforged] = new()
            {
                Name = "Ironforged",
                AttributeBonus = new AttributeBonuses { Vitality = 2, Agility = -1 },
                MovementSpeed = 25,
                SpecialAbility = "Iron Constitution - Advantage on saving throws against poison, and resistance to poison damage",
                AvailableClasses = ["Warrior", "Paladin", "Cleric"]
            },
            [CharacterRace.Wildkin] = new()
            {
                Name = "Wildkin",
                AttributeBonus = new AttributeBonuses { Agility = 1, Presence = 1, Wits = -1 },
                MovementSpeed = 35,
                SpecialAbility = "Beast Sense - Communicate simple concepts with animals, advantage on Perception checks in natural environments",
                AvailableClasses = ["Warrior", "Rogue", "Cleric"]
            },
            [CharacterRace.Emberfolk] = new()
            {
                Name = "Emberfolk",
                AttributeBonus = new AttributeBonuses { Wits = 2, Vitality = -1 },
                MovementSpeed = 30,
                SpecialAbility = "Flame Affinity - Resistance to fire damage, can cast 'Produce Flame' at will",
                AvailableClasses = ["Wizard", "War Mage"]
            },
            [CharacterRace.Stoneborn] = new()
            {
                Name = "Stoneborn",
                AttributeBonus = new AttributeBonuses { Vitality = 1, Might = 1, Agility = -1 },
                MovementSpeed = 25,
                SpecialAbility = "Stone's Endurance - Once per short rest, reduce incoming damage by 1d12 + Vitality modifier",
                AvailableClasses = ["Warrior", "Paladin", "Cleric"]
            }
        };

        if (!validRaces.TryGetValue(race, out var raceDetails))
        {
            return JsonSerializer.Serialize(new RaceValidationResult
            {
                Valid = false,
                Error = $"Invalid race '{race}'. Valid options are: {string.Join(", ", Enum.GetNames<CharacterRace>())}"
            });
        }

        // Save to session state
        var session = await sessionStateManager.GetSessionStateAsync(sessionId);
        if (session == null)
        {
            return JsonSerializer.Serialize(new RaceValidationResult
            {
                Valid = false,
                Error = "Session not found"
            });
        }

        // Initialize DraftCharacter if needed
        if (session.Context.DraftCharacter == null)
        {
            session.Context.DraftCharacter = new Character
            {
                Id = Guid.NewGuid().ToString(),
                CreatedAt = DateTime.UtcNow
            };
        }

        // Update race
        session.Context.DraftCharacter.Race = race;
        session.Context.CurrentStep = "class";
        session.Context.CompletedSteps.Add("race");
        session.LastUpdatedAt = DateTime.UtcNow;

        await sessionStateManager.UpdateSessionStateAsync(session);

        return JsonSerializer.Serialize(new RaceValidationResult
        {
            Valid = true,
            Race = raceDetails
        });
    }

    [Description("Saves the chosen class to the character in progress and returns class details including Hit Dice, Mana Dice, prime attributes, and starting equipment. Valid classes are: Cleric, Wizard, Warrior, Rogue, Paladin, WarMage. Requires sessionId.")]
    public async Task<string> SaveClassChoice(
        [Description("The unique session ID for this character creation session.")] string sessionId,
        [Description("The class to assign to the character. Must be one of: Cleric, Wizard, Warrior, Rogue, Paladin, WarMage.")] CharacterClass characterClass)
    {
        var classDetails = new Dictionary<CharacterClass, ClassDetails>
        {
            [CharacterClass.Cleric] = new()
            {
                Name = "Cleric",
                HitDice = "1d8",
                ManaDice = "1d6",
                PrimeAttributes = ["Willpower", "Vitality"],
                Weapons = ["Simple Weapons", "Shields"],
                Armor = ["Light Armor", "Medium Armor", "Shields"],
                StartingEquipment = ["Holy Symbol", "Mace or Warhammer", "Scale Mail or Leather Armor", "Shield", "Priest's Pack"
                ]
            },
            [CharacterClass.Wizard] = new()
            {
                Name = "Wizard",
                HitDice = "1d6",
                ManaDice = "1d8",
                PrimeAttributes = ["Intellect", "Willpower"],
                Weapons = ["Daggers", "Quarterstaffs", "Light Crossbows"],
                Armor = ["None"],
                StartingEquipment = ["Spellbook", "Component Pouch", "Quarterstaff or Dagger", "Scholar's Pack"]
            },
            [CharacterClass.Warrior] = new()
            {
                Name = "Warrior",
                HitDice = "1d10",
                ManaDice = "None",
                PrimeAttributes = ["Might", "Vitality"],
                Weapons = ["All Weapons"],
                Armor = ["All Armor", "Shields"],
                StartingEquipment = ["Chain Mail or Leather Armor", "Martial Weapon and Shield or Two Martial Weapons", "Light Crossbow and 20 Bolts", "Dungeoneer's Pack"
                ]
            },
            [CharacterClass.Rogue] = new()
            {
                Name = "Rogue",
                HitDice = "1d8",
                ManaDice = "None",
                PrimeAttributes = ["Agility", "Perception"],
                Weapons = ["Simple Weapons", "Hand Crossbows", "Longswords", "Rapiers", "Shortswords"],
                Armor = ["Light Armor"],
                StartingEquipment = ["Rapier or Shortsword", "Shortbow and Quiver of 20 Arrows", "Burglar's Pack", "Leather Armor", "Thieves' Tools"
                ]
            },
            [CharacterClass.Paladin] = new()
            {
                Name = "Paladin",
                HitDice = "1d10",
                ManaDice = "1d4",
                PrimeAttributes = ["Might", "Willpower"],
                Weapons = ["All Weapons"],
                Armor = ["All Armor", "Shields"],
                StartingEquipment = ["Chain Mail", "Martial Weapon and Shield", "Holy Symbol", "Priest's Pack"]
            },
            [CharacterClass.WarMage] = new()
            {
                Name = "War Mage",
                HitDice = "1d8",
                ManaDice = "1d6",
                PrimeAttributes = ["Intellect", "Agility"],
                Weapons = ["Simple Weapons", "Martial Weapons (one-handed)"],
                Armor = ["Light Armor", "Medium Armor"],
                StartingEquipment = ["Spellbook", "Component Pouch", "Longsword or Rapier", "Leather Armor", "Scholar's Pack"
                ]
            }
        };

        if (!classDetails.TryGetValue(characterClass, out var value))
        {
            return JsonSerializer.Serialize(new ClassValidationResult
            {
                Valid = false,
                Error = $"Invalid class '{characterClass}'. Valid options are: {string.Join(", ", Enum.GetNames<CharacterClass>())}"
            });
        }

        // Get session and validate race exists
        var session = await sessionStateManager.GetSessionStateAsync(sessionId);
        if (session?.Context.DraftCharacter == null || session.Context.DraftCharacter.Race == default)
        {
            return JsonSerializer.Serialize(new ClassValidationResult
            {
                Valid = false,
                Error = "Race must be selected before class"
            });
        }

        // Save class to session
        session.Context.DraftCharacter.Class = characterClass;
        session.Context.CurrentStep = "attributes";
        if (!session.Context.CompletedSteps.Contains("class"))
        {
            session.Context.CompletedSteps.Add("class");
        }
        session.LastUpdatedAt = DateTime.UtcNow;

        await sessionStateManager.UpdateSessionStateAsync(session);

        return JsonSerializer.Serialize(new ClassValidationResult
        {
            Valid = true,
            Class = value
        });
    }

    [Description("Saves attribute allocation to the character in progress using Standard Array (15, 14, 13, 12, 10) or previously rolled values (range 3-18). Returns validation result and calculated modifiers with racial bonuses applied. Five attributes are: Might, Agility, Vitality, Wits, Presence. Requires sessionId.")]
    public async Task<string> SaveAttributeAllocation(
        [Description("The unique session ID for this character creation session.")] string sessionId,
        [Description("The Might attribute value (physical strength and melee power). Range 3-18 for rolled, or one of 15,14,13,12,10 for standard array.")] int might,
        [Description("The Agility attribute value (dexterity, reflexes, ranged attacks). Range 3-18 for rolled, or one of 15,14,13,12,10 for standard array.")] int agility,
        [Description("The Vitality attribute value (constitution, health, endurance). Range 3-18 for rolled, or one of 15,14,13,12,10 for standard array.")] int vitality,
        [Description("The Wits attribute value (intelligence, spellcasting for wizards). Range 3-18 for rolled, or one of 15,14,13,12,10 for standard array.")] int wits,
        [Description("The Presence attribute value (charisma, force of personality, social skills). Range 3-18 for rolled, or one of 15,14,13,12,10 for standard array.")] int presence,
        [Description("The attribute generation method used. Must be either StandardArray or Rolled. StandardArray validates exact values [15,14,13,12,10], Rolled validates range 3-18.")] AttributeGenerationMethod method)
    {
        var attributes = new Dictionary<AttributeType, int>
        {
            [AttributeType.Might] = might,
            [AttributeType.Agility] = agility,
            [AttributeType.Vitality] = vitality,
            [AttributeType.Wits] = wits,
            [AttributeType.Presence] = presence
        };

        // Validate attribute generation method
        var isValid = method switch
        {
            AttributeGenerationMethod.StandardArray => AttributeCalculator.ValidateStandardArray(attributes),
            AttributeGenerationMethod.Rolled => AttributeCalculator.ValidateRolledAttributes(attributes),
            _ => false
        };

        if (!isValid)
        {
            return JsonSerializer.Serialize(new AttributeAllocationResult
            {
                Valid = false,
                Error = method == AttributeGenerationMethod.StandardArray
                    ? "Attributes must use Standard Array: 15, 14, 13, 12, 10 (each value used exactly once)"
                    : "Rolled attributes must be between 3-18"
            });
        }

        // Get session and validate prerequisites
        var session = await sessionStateManager.GetSessionStateAsync(sessionId);
        if (session?.Context.DraftCharacter == null || session.Context.DraftCharacter.Race == default)
        {
            return JsonSerializer.Serialize(new AttributeAllocationResult
            {
                Valid = false,
                Error = "Race and class must be selected before attributes"
            });
        }

        var raceName = session.Context.DraftCharacter.Race;

        // Apply racial bonuses
        var finalAttributes = AttributeCalculator.ApplyRacialBonuses(attributes, raceName);

        // Save to session
        session.Context.DraftCharacter.Attributes = finalAttributes;
        session.Context.CurrentStep = "skills";
        if (!session.Context.CompletedSteps.Contains("attributes"))
        {
            session.Context.CompletedSteps.Add("attributes");
        }
        session.LastUpdatedAt = DateTime.UtcNow;

        await sessionStateManager.UpdateSessionStateAsync(session);

        // Calculate modifiers
        var modifiers = finalAttributes.ToDictionary(
            kvp => kvp.Key,
            kvp => AttributeCalculator.CalculateModifier(kvp.Value)
        );

        return JsonSerializer.Serialize(new AttributeAllocationResult
        {
            Valid = true,
            BaseAttributes = attributes,
            FinalAttributes = finalAttributes,
            Modifiers = modifiers
        });
    }

    [Description("Initiates interactive dice rolling for attribute generation using the 4d6 drop lowest method. The player will physically roll dice via the animated UI. After all 6 rolls, prompt the player to assign results to attributes using SaveAttributeAllocation.")]
    public async Task<string> RollAttributeDice(
        [Description("The unique session ID for this character creation session.")] string sessionId)
    {
        // Get session to validate it exists
        var session = await sessionStateManager.GetSessionStateAsync(sessionId);
        if (session?.Context.DraftCharacter == null)
        {
            return JsonSerializer.Serialize(new AttributeDiceRollResult
            {
                Success = false,
                Error = "Character creation session not found"
            });
        }
        var windowOptions = new RollDiceWindowOptions() { Title = $"Roll {4}{DieType.D6} for an Attribute", Location = Location.Center, Style = "width:max-content;min-width:40vw;height:max-content" };
        var parameters = new RollDiceParameters() { ["DieType"] = DieType.D6, ["NumberOfRolls"] = 4 };
        //List<RollDiceResults> rollDiceResults = new();
        //for (var i = 1; i <= 6; i++)
        //{
        //    var intermediateResult = await rollDiceService.RequestDiceRoll(sessionId, parameters, windowOptions, 1);
        //    rollDiceResults.AddRange(intermediateResult);
        //}
        var result = await rollDiceService.RequestDiceRoll(sessionId, parameters, windowOptions, 6);
        // This tool provides guidance to use the RollPlayerDice tool
        // The actual dice rolling will be handled by RollPlayerDice from IRollDiceService
        return JsonSerializer.Serialize(result);
    }

    [Description("Saves the character name and selected skills (must choose 4 from 14 available skills) to the character in progress, calculates all derived stats (HP, MP, AC, Initiative), and returns the complete character sheet ready for final review. Requires sessionId.")]
    public async Task<string> SaveNameAndSkills(
        [Description("The unique session ID for this character creation session.")] string sessionId,
        [Description("The character's chosen name. This is the display name used throughout the game.")] string characterName,
        [Description($"Array of exactly 4 skill names chosen by the player. Valid skills: {SkillList}")] string[] selectedSkills)
    {
        var skills = Skill.GetAllSkillsFromFile().Where(x => selectedSkills.Contains(x.Name));
        // Validate skill count
        if (selectedSkills.Length != 4)
        {
            return JsonSerializer.Serialize(new CharacterSheetResult
            {
                Valid = false,
                Error = $"You must select exactly 4 skills. Available skills: {SkillList}"
            });
        }

        // Get session and validate prerequisites
        var session = await sessionStateManager.GetSessionStateAsync(sessionId);
        if (session?.Context.DraftCharacter == null)
        {
            return JsonSerializer.Serialize(new CharacterSheetResult
            {
                Valid = false,
                Error = "Character creation session not found"
            });
        }

        var character = session.Context.DraftCharacter;

        // Validate required fields are set
        if (character.Race == default || character.Class == default || character.Attributes.Count == 0)
        {
            return JsonSerializer.Serialize(new CharacterSheetResult
            {
                Valid = false,
                Error = "Race, class, and attributes must be set before finalizing character"
            });
        }

        // Save name and skills
        character.Name = characterName;
        character.Level = 1;
        character.Experience = 0;

        character.Skills.Clear();
        foreach (var skill in selectedSkills)
        {
            character.Skills[skill] = 1;
        }

        character.CharacterSkills = skills.ToList();
        character.CharacterSkills.ForEach(skill => skill.Rank = 1);
        // Calculate all derived stats
        character.CurrentHP = character.MaxHP;
        character.CurrentMP = character.MaxMP;
        character.Initiative = StatsCalculator.CalculateInitiative(character);
        character.UpdatedAt = DateTime.UtcNow;

        // Update session
        session.Context.CurrentStep = "review";
        if (!session.Context.CompletedSteps.Contains("skills"))
        {
            session.Context.CompletedSteps.Add("skills");
        }
        session.LastUpdatedAt = DateTime.UtcNow;

        await sessionStateManager.UpdateSessionStateAsync(session);

        return JsonSerializer.Serialize(new CharacterSheetResult
        {
            Valid = true,
            Character = character
        });
    }

    [Description("Saves character background story, personality traits, and description. This is optional but recommended. Parameters: sessionId, background (detailed character backstory, personality, motivations). Returns confirmation message.")]
    public async Task<string> SaveBackground(
        [Description("The unique session ID for this character creation session.")] string sessionId,
        [Description("The character's backstory, personality traits, motivations, and history. This narrative text enriches the character and provides roleplay context.")] string background)
    {
        var session = await sessionStateManager.GetSessionStateAsync(sessionId);
        if (session?.Context.DraftCharacter == null)
        {
            return JsonSerializer.Serialize(new CharacterSheetResult
            {
                Valid = false,
                Error = "Character creation session not found"
            });
        }

        var character = session.Context.DraftCharacter;
        character.Background = background;
        session.LastUpdatedAt = DateTime.UtcNow;

        await sessionStateManager.UpdateSessionStateAsync(session);

        return JsonSerializer.Serialize(new CharacterSheetResult
        {
            Valid = true,
            Character = character
        });
    }

    [Description("Saves known spells for spellcasting classes (Wizard, Cleric, Paladin, War Mage). Parameters: sessionId, spellNames (array of spell names the character knows). Level 1 characters typically start with 3-6 spells depending on class. Returns updated character with spells.")]
    public async Task<string> SaveKnownSpells(
        [Description("The unique session ID for this character creation session.")] string sessionId,
        [Description($"Array of spell names that the character knows. Should include 3-6 starting spells appropriate to the character's class (Wizard, Cleric, Paladin, or WarMage only). If the character is a Wizard or WarMage, available starting spells are {WizardSpellList}. If a Cleric or Paladin, available starting spells are {LightClericOrPaladinSpellList} (for light or neutral aligned faiths) or {DarkClericOrPaladinSpellList} for dark aligned faiths.")] string[] spellNames)
    {
        var session = await sessionStateManager.GetSessionStateAsync(sessionId);
        if (session?.Context.DraftCharacter == null)
        {
            return JsonSerializer.Serialize(new CharacterSheetResult
            {
                Valid = false,
                Error = "Character creation session not found"
            });
        }

        var character = session.Context.DraftCharacter;

        // Validate class is a spellcaster
        var spellcastingClasses = new[] { CharacterClass.Wizard, CharacterClass.Cleric, CharacterClass.Paladin, CharacterClass.WarMage };
        if (!spellcastingClasses.Contains(character.Class))
        {
            return JsonSerializer.Serialize(new CharacterSheetResult
            {
                Valid = false,
                Error = $"{character.Class} is not a spellcasting class. Only Wizard, Cleric, Paladin, and War Mage can learn spells."
            });
        }

        // Create spell objects from names
        character.KnownSpells.Clear();
        List<Spell> allSpells = [];
        if (character.Class is CharacterClass.Wizard or CharacterClass.WarMage)
            allSpells = Spell.GetAllWizardSpellsFromFile();
        else if (character.Class is CharacterClass.Cleric or CharacterClass.Paladin)
            allSpells = Spell.GetAllClericSpellsFromFile();
        foreach (var spellName in spellNames)
        {
            var spell = allSpells.Find(s => s.Name.Equals(spellName, StringComparison.OrdinalIgnoreCase));
            character.KnownSpells.Add(spell!);
        }

        // Set spell slots based on class at level 1
        character.MaxSpellSlots.Clear();
        character.CurrentSpellSlots.Clear();

        switch (character.Class)
        {
            case CharacterClass.Wizard:
            case CharacterClass.Cleric:
                character.MaxSpellSlots[1] = 4; // 4 first-level spell slots
                character.CurrentSpellSlots[1] = 4;
                break;
            case CharacterClass.WarMage:
                character.MaxSpellSlots[1] = 2;
                character.CurrentSpellSlots[1] = 2;
                break;
            case CharacterClass.Paladin:
                character.MaxSpellSlots[1] = 2;
                character.CurrentSpellSlots[1] = 2;
                break;
        }

        session.LastUpdatedAt = DateTime.UtcNow;
        await sessionStateManager.UpdateSessionStateAsync(session);

        return JsonSerializer.Serialize(new CharacterSheetResult
        {
            Valid = true,
            Character = character
        });
    }

    [Description("Creates a complete character from a brief description or concept. Use this when the player wants quick character creation. Parameters: sessionId, characterConcept (brief description like 'elven wizard scholar' or 'gruff dwarven warrior'), characterName (optional), campaignId. Returns fully created character ready to play.")]
    public async Task<string> CreateQuickCharacter(
        [Description("The unique session ID for this character creation session.")] string sessionId,
        [Description("A description of the desired character concept, based on the user's indicated preferences or description.")] string characterConcept,
        [Description("Optional character name. If not provided, a default name will be generated based on race and class.")] string? characterName,
        [Description("The unique ID of the campaign this character will join. (optional)")] string campaignId = "")
    {
        var client = new OpenAIClient(new ApiKeyCredential(configuration.OpenRouterApiKey), new OpenAIClientOptions() { Endpoint = new Uri(configuration.OpenRouterEndpoint) });
        var goalInstructions =
            $"""
             ## Goal

             Create a complete character using the concept as guidance.
             **Character Concept:** 
             {characterConcept}
             **Character Name:** {characterName} (if empty, generate a fitting name)
             
            
             """;
        // Get chat client for the specified deployment and convert to IChatClient
        var chatClient = client.GetChatClient("openai/gpt-oss-120b").AsIChatClient();
        var quickCreateAgent = chatClient.AsAIAgent(
            options: new ChatClientAgentOptions()
            {


                Name = "Quick Create Agent",
                ChatOptions = new ChatOptions()
                {
                    ResponseFormat = ChatResponseFormat.ForJsonSchema<Character>(),
                    Instructions = $"{QuickCreateInstructions}",
                    RawRepresentationFactory = _ => new OpenRouterChatCompletionOptions()
                    {
                        ReasoningEffortLevel = "high",
                        Provider = new Provider() { Sort = "throughput" },
                    }
                }

            });
        var response = await quickCreateAgent.RunAsync<Character>(goalInstructions);
        var character = response.Result;
        foreach (var skill in character.Skills)
        {
            character.Skills[skill.Key] = 1;
        }

        character.Skills = character.Skills.Take(4).ToDictionary();
        var session = await sessionStateManager.GetSessionStateAsync(sessionId);
        if (session == null)
        {
            return JsonSerializer.Serialize(new CharacterSheetResult
            {
                Valid = false,
                Error = "Session not found"
            });
        }


        // Set campaign ID
        character.CampaignId = campaignId;
        // Set Player ID
        character.PlayerId = session.PlayerId;
        // Generate Image
        var imageResponse = await ImageGenService.GenerateCharacterImage(character, "A detailed and creative character portrait.");
        character.PortraitUrl = imageResponse;
        // Save to repository
        await characterRepository.CreateAsync(character);

        // Update session
        session.Context.DraftCharacter = character;
        await sessionStateManager.CompleteSessionAsync(sessionId, character.Id);

        return JsonSerializer.Serialize(new CharacterSheetResult
        {
            Valid = true,
            Character = character
        });
    }
    [Description("Generate a scathing parody video that introduces the new character based on character details and additional instructions")]
    public async Task<string> GenerateParodyVideo([Description("The unique session ID for this character creation session (will be used to get character details).")] string sessionId, [Description("Additional instructions specific to the parody video.")] string parodyVideoInstructions)
    {
        // Get session and draft character
        var session = await sessionStateManager.GetSessionStateAsync(sessionId);
        if (session?.Context.DraftCharacter == null)
        {
            return JsonSerializer.Serialize(new
            {
                Success = false,
                Message = "Character creation session not found"
            });
        }

        var character = session.Context.DraftCharacter;
        var response = await VideoGenService.GenerateSoraFromPrompt(parodyVideoInstructions, sessionId);
        return JsonSerializer.Serialize(new
        {
            Success = true,
            Video = response
        });
    }
    [Description("Generates an image based on the character details and an optional set of instructions.")]
    public async Task<string> GenerateCharacterImage([Description("The unique session ID for this character creation session.")] string sessionId, [Description("Additional instructions for image generation.")] string? additionalInstructions = null)
    {
        // Get session and draft character
        var session = await sessionStateManager.GetSessionStateAsync(sessionId);
        if (session?.Context.DraftCharacter == null)
        {
            return JsonSerializer.Serialize(new
            {
                Success = false,
                Message = "Character creation session not found"
            });
        }

        var character = session.Context.DraftCharacter;
        var response = await ImageGenService.GenerateCharacterImage(character, additionalInstructions);
        character.PortraitUrl = response;
        return JsonSerializer.Serialize(new
        {
            Success = true,
            Image = $"Image generated successfully! Begins as {response.Split(';')[0]}"
        });
    }

    [Description("Retrieves a list of available starting equipment for the character to purchase.")]
    public string GetAvailableStartingEquipment()
    {
        var allItems = InventoryItem.GetAllItemsFromFile().Where(x => x is not MagicItem);
        return JsonSerializer.Serialize(new
        {
            Success = true,
            Items = allItems
        });
    }
    [Description("Have user select starting equipment, spending up to 150 gold.")]
    public async Task<string> SelectStartingEquipment(
        [Description("The unique session ID for this character creation session.")] string sessionId,
        [Description("List of item names selected as starting equipment.")] string[] selectedItemNames)
    {
        // Get session and draft character
        var session = await sessionStateManager.GetSessionStateAsync(sessionId);
        if (session?.Context.DraftCharacter == null)
        {
            return JsonSerializer.Serialize(new
            {
                Success = false,
                Message = "Character creation session not found"
            });
        }
        var character = session.Context.DraftCharacter;
        var allItems = InventoryItem.GetAllItemsFromFile();
        var selectedItems = allItems.Where(x => selectedItemNames.Contains(x.Name)).ToList();
        var spentGold = selectedItems.Sum(x => x.Value);
        if (spentGold > 150)
        {
            return JsonSerializer.Serialize(new
            {
                Success = false,
                Message = "Selected items exceed starting budget of 150 gold."
            });
        }
        character.Gold = 150 - spentGold;
        foreach (var item in selectedItems)
        {
            character.Inventory.Add(item);
            if (character.Equipment.MainHand == null && item is WeaponItem weapon)
            {
                character.Equipment.MainHand = weapon;
            }
            else if (character.Equipment.Armor == null && item is ArmorItem armor)
            {
                character.Equipment.Armor = armor;
            }
        }

        await sessionStateManager.UpdateSessionStateAsync(session);
        return JsonSerializer.Serialize(new
        {
            Success = true,
            Message = "Starting equipment selected successfully!"
        });
    }
    [Description("Finalizes and saves the completed character from the session to the repository and campaign. This is the final step in character creation.")]
    public async Task<string> FinalizeCharacter(
        [Description("The unique session ID for this character creation session.")] string sessionId)
    {
        // Get session and draft character
        var session = await sessionStateManager.GetSessionStateAsync(sessionId);
        if (session?.Context.DraftCharacter == null)
        {
            return JsonSerializer.Serialize(new SaveCharacterResult
            {
                Success = false,
                Message = "Character creation session not found"
            });
        }

        var character = session.Context.DraftCharacter;
        //var allInventory = InventoryItem.GetAllItemsFromFile();
        var allWeapons = WeaponItem.GetAllWeaponsFromFile();
        var allArmor = ArmorItem.GetAllArmorFromFile();
        if (character.Class is CharacterClass.Warrior or CharacterClass.WarMage && character.Equipment.MainHand is null)
        {
            character.Inventory.AddRange([allWeapons.Find(x => x.Name.Equals("Shortsword", StringComparison.OrdinalIgnoreCase))!, allArmor.Find(x => x.Name.Contains("Leather", StringComparison.OrdinalIgnoreCase))!]);
        }

        if (character.Class is CharacterClass.Cleric or CharacterClass.Paladin && character.Equipment.MainHand is null)
        {
            character.Inventory.AddRange([allWeapons.Find(x => x.Name.Equals("Mace", StringComparison.OrdinalIgnoreCase))!, allArmor.Find(x => x.Name.Contains("Leather", StringComparison.OrdinalIgnoreCase))!]);
        }

        if (character.Class is CharacterClass.Wizard && character.Equipment.MainHand is null)
        {
            character.Inventory.AddRange([allWeapons.Find(x => x.Name.Equals("Quarterstaff", StringComparison.OrdinalIgnoreCase))!, allArmor.Find(x => x.Name.Contains("Leather", StringComparison.OrdinalIgnoreCase))!]);
        }

        if (character.Class is CharacterClass.Rogue && character.Equipment.MainHand is null)
        {
            character.Inventory.AddRange([allWeapons.Find(x => x.Name.Equals("Dagger", StringComparison.OrdinalIgnoreCase))!, allArmor.Find(x => x.Name.Contains("Leather", StringComparison.OrdinalIgnoreCase))!]);
        }
        character.Equipment.Armor = character.Inventory.FirstOrDefault(x => x.ItemType == ItemType.Armor) as ArmorItem;
        character.Equipment.MainHand = character.Inventory.FirstOrDefault(x => x.ItemType == ItemType.Weapon) as WeaponItem;
        //// Validate character is complete
        if (string.IsNullOrEmpty(character.Name) ||
            character.Race == default ||
            character.Class == default ||
            character.Attributes.Count == 0 ||
            character.MaxHP == 0)
        {
            return JsonSerializer.Serialize(new SaveCharacterResult
            {
                Success = false,
                Message = "Character is incomplete. Please ensure all steps are completed."
            });
        }


        character.CreatedAt = DateTime.UtcNow;
        character.UpdatedAt = DateTime.UtcNow;

        character.PlayerId = session.PlayerId;

        await characterRepository.CreateAsync(character);

        await sessionStateManager.CompleteSessionAsync(sessionId, character.Id);

        return JsonSerializer.Serialize(new SaveCharacterResult
        {
            Success = true,
            CharacterId = character.Id,
            Message = $"Character '{character.Name}' created successfully!"
        });
    }


    private const string SkillList =
        """
        **Available Skills:**
        - **Athletics**: Climbing, swimming, jumping, and feats of raw strength in rugged environments.
        - **Melee Combat**: Proficiency with close-range weapons such as swords, axes, maces, and hammers. Recommended for Warriors and Paladins.
        - **Ranged Combat**: Accuracy with bows, crossbows, thrown knives, and other distance weapons.
        - **Stealth**: Moving silently, hiding, and slipping past alert foes or traps. Recommended for Rogues.
        - **Acrobatics**: Balance, tumbling, flips, and evasive maneuvers in combat or exploration.
        - **Endurance**: Resisting fatigue, poison, disease, harsh weather, and prolonged strain.
        - **Survival**: Tracking, foraging, navigation, and wilderness know-how.
        - **Perception**: Spotting ambushes, noticing hidden details, and reading the environment.
        - **Crafting**: Designing, repairing, and improving weapons, armor, tools, or alchemical devices.
        - **Investigation**: Analyzing clues, solving puzzles, and piecing together mysteries.
        - **Persuasion**: Diplomacy, negotiation, and inspiring cooperation through sincerity.
        - **Intimidation**: Commanding fear or obedience through force of personality.
        - **Deception**: Lies, disguises, bluffing, and sleight-of-hand misdirection.
        - **Leadership**: Guiding allies, issuing battlefield commands, and bolstering morale.
        """;

    private const string WizardSpellList =
        """
        **Wizard Spells:**
        - **Magic Missile**: Launch multiple bolts of pure force that never miss.
        - **Shield**: Form an invisible barrier that intercepts harm.
        - **Burning Hands**: Project a fan of roaring flame.
        - **Mage Armor**: Wrap a target in spectral plates of force.
        - **Arcane Sight**: Open your senses to magical auras.
        - **Illusory Disguise**: Layer a believable glamour over a creature.
        - **Feather Fall**: Slow falling creatures before impact.
        """;

    private const string LightClericOrPaladinSpellList =
        """
        
        **Cleric/Paladin Spells (Light Domain):**
        - **Healing Light**: Channel warm radiance to close wounds and steady an ally.
        - **Divine Shield**: Call down a shimmering ward of light that deflects incoming blows.
        - **Bless**: Bolster the resolve of nearby allies with gentle radiance.
        - **Turn Undead**: Radiant command that drives undead away.
        - **Sacred Flame**: Summon a column of brilliant fire to scour foes.
       
        """;
    private const string DarkClericOrPaladinSpellList =
        """
        
        **Cleric/Paladin Spells (Shadow Domain):**
        - **Soul Rot**: Inflict necrotic decay that feeds on vitality.
        - **Umbral Armor**: Sheathe an ally in solidified gloom.
        - **Despair**: Siphon courage from nearby foes.
        - **Invoke Terror**: Unleash nightmarish whispers that shatter morale.
        - **Void Lash**: Manifest a whip of hungry darkness.
        
        """;
}
