using System.ComponentModel;
using System.Text;
using System.Text.Json.Serialization;
using AgenticRpg.Core.Helpers;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Rules;

namespace AgenticRpg.Core.Models.Game;

/// <summary>
/// Represents a player character in the RPG system
/// </summary>
public class Character
{

    /// <summary>
    /// Unique identifier for the character
    /// </summary>
    [JsonPropertyName("id")]
    [Description("Unique identifier for the character")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Character's display name
    /// </summary>
    [Description("Character's display name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// ID of the player who owns this character
    /// </summary>
    [Description("ID of the player who owns this character")]
    public string PlayerId { get; set; } = string.Empty;

    /// <summary>
    /// ID of the campaign this character belongs to
    /// </summary>
    [Description("ID of the campaign this character belongs to")]
    public string CampaignId { get; set; } = string.Empty;

    /// <summary>
    /// Character's race (e.g., Human, Duskborn, Wildkin)
    /// </summary>
    [Description("Character's race (e.g., Human, Duskborn, Wildkin)")]
    public CharacterRace Race { get; set; } = CharacterRace.None;

    /// <summary>
    /// Character's class (e.g., Cleric, Wizard, Rogue)
    /// </summary>
    [Description("Character's class (e.g., Cleric, Wizard, Rogue)")]
    public CharacterClass Class { get; set; } = CharacterClass.None;

    /// <summary>
    /// Character's current level
    /// </summary>
    [Description("Character's current level")]
    public int Level { get; set; } = 1;

    /// <summary>
    /// Current experience points
    /// </summary>
    [Description("Current experience points")]
    public int Experience { get; set; } = 0;

    /// <summary>
    /// Core attribute scores
    /// </summary>
    [Description("Core attribute scores")]
    public Dictionary<AttributeType, int> Attributes { get; set; } = new()
    {
        { AttributeType.Might, 10 },
        { AttributeType.Agility, 10 },
        { AttributeType.Vitality, 10 },
        { AttributeType.Wits, 10 },
        { AttributeType.Presence, 10 }
    };
    [JsonIgnore]
    public int Might { get => Attributes[AttributeType.Might]; set => Attributes[AttributeType.Might] = value; }
    [JsonIgnore]
    public int Agility { get => Attributes[AttributeType.Agility]; set => Attributes[AttributeType.Agility] = value; }
    [JsonIgnore]
    public int Vitality { get => Attributes[AttributeType.Vitality]; set => Attributes[AttributeType.Vitality] = value; }
    [JsonIgnore]
    public int Wits { get => Attributes[AttributeType.Wits]; set => Attributes[AttributeType.Wits] = value; }
    [JsonIgnore]
    public int Presence { get => Attributes[AttributeType.Presence]; set => Attributes[AttributeType.Presence] = value; }

    /// <summary>
    /// Maximum hit points
    /// </summary>
    [Description("Maximum hit points")]
    public int MaxHP => StatsCalculator.CalculateMaxHP(this);

    /// <summary>
    ///  Current hit points
    /// </summary>
    [Description("Current hit points")]
    public int CurrentHP { get; set; } = 0;

    /// <summary>
    /// Current magic/mana points
    /// </summary>
    [Description("Current magic/mana points")]
    public int CurrentMP { get; set; }

    /// <summary>
    /// Maximum magic/mana points
    /// </summary>
    [Description("Maximum magic/mana points")]
    public int MaxMP => StatsCalculator.CalculateMaxMP(this);

    /// <summary>
    /// Armor class (defense rating)
    /// </summary>
    [Description("Armor class (defense rating)")]
    public int ArmorClass => StatsCalculator.CalculateArmorClass(this);

    /// <summary>
    /// Initiative modifier for combat
    /// </summary>
    [Description("Initiative modifier for combat")]
    public int Initiative { get; set; }
    [Description("Temporary attribute buffs")]
    public List<MagicEffect> TemporaryAttributeBuffs { get; set; } = [];
    /// <summary>
    /// Movement speed in feet
    /// </summary>
    [Description("Movement speed in feet")]
    public int Speed { get; set; } = 30;

    /// <summary>
    /// Character's skill ranks
    /// </summary>
    [Description("Character's skill ranks")]

    public Dictionary<string, int> Skills { get; set; } = [];

    private List<Skill>? _characterSkills;

    [Description("Character's skills")]
    public List<Skill> CharacterSkills
    {
        get
        {
            if (_characterSkills != null) return _characterSkills;
            var skills = Skill.GetAllSkillsFromFile();
            _characterSkills = skills.Where(x => Skills.ContainsKey(x.Name)).ToList();
            return _characterSkills;
        }
        set => _characterSkills = value;
    }

    /// <summary>
    /// Character's inventory
    /// </summary>
    [Description("Character's full inventory")]
    public List<InventoryItem> Inventory { get; set; } = [];

    /// <summary>
    /// Currently equipped items
    /// </summary>
    [Description("Currently equipped items")]
    public EquippedItems Equipment { get; set; } = new();

    /// <summary>
    /// Known spells (for spellcasters)
    /// </summary>
    [Description("Known spells (for spellcasters)")]
    public List<Spell> KnownSpells { get; set; } = [];

    /// <summary>
    /// Current spell slots by level
    /// </summary>
    [Description("Current spell slots by level")]
    public Dictionary<int, int> CurrentSpellSlots { get; set; } = [];

    /// <summary>
    /// Maximum spell slots by level
    /// </summary>
    [Description("Maximum spell slots by level")]
    public Dictionary<int, int> MaxSpellSlots { get; set; } = [];

    /// <summary>
    /// Character's gold/currency
    /// </summary>
    [Description("Character's gold/currency")]
    public int Gold { get; set; } = 0;

    /// <summary>
    /// Character background/description
    /// </summary>
    [Description("Character background/description")]
    public string Background { get; set; } = string.Empty;

    /// <summary>
    /// Character portrait URL (optional)
    /// </summary>
    [Description("Character portrait URL (optional)")]
    public string? PortraitUrl { get; set; }
    public string? IntroVidUrl { get; set; }
    /// <summary>
    /// Death save successes (for unconscious characters)
    /// </summary>
    [Description("Death save successes (for unconscious characters)")]
    public int DeathSaveSuccesses { get; set; } = 0;

    /// <summary>
    /// Death save failures (for unconscious characters)
    /// </summary>
    [Description("Death save failures (for unconscious characters)")]
    public int DeathSaveFailures { get; set; } = 0;
    public bool RequiresLevelUp => Experience >= Level * 1000;
    /// <summary>
    /// Timestamp when character was created
    /// </summary>
    [Description("Timestamp when character was created")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when character was last updated
    /// </summary>
    [Description("Timestamp when character was last updated")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Calculates the attribute modifier for a given attribute
    /// </summary>
    public int GetAttributeModifier(AttributeType attribute)
    {
        if (!Attributes.TryGetValue(attribute, out var score))
            return 0;

        return (score - 10) / 2;
    }

    /// <summary>
    /// Checks if character is alive
    /// </summary>
    [Description("Checks if character is alive")]
    public bool IsAlive => CurrentHP > 0;

    /// <summary>
    /// Checks if character is unconscious (0 HP but not dead)
    /// </summary>
    [Description("Checks if character is unconscious (0 HP but not dead)")]
    public bool IsUnconscious => CurrentHP == 0 && DeathSaveFailures < 3;

    /// <summary>
    /// Topline markdown representation of basic character data
    /// </summary>
    /// <returns></returns>
    public string AsBasicDataMarkdown()
    {
        var markdownBuilder = new StringBuilder();
        markdownBuilder.AppendLine($"### Character {Name} (Level {Level})");
        markdownBuilder.AppendLine($"**Race:** {Race}");
        markdownBuilder.AppendLine($"**Class:** {Class}");
        if (this.IsSpellcaster())
        {
            markdownBuilder.AppendLine($"**Known Spells:** {string.Join(", ", KnownSpells.Select(s => s.Name))}");
        }

        // Equipped items
        markdownBuilder.AppendLine("#### Equipped Items");
        markdownBuilder.AppendLine(Equipment.AsMarkdown());
        // Skills
        markdownBuilder.AppendLine("#### Skills");
        foreach (var skill in CharacterSkills)
        {
            markdownBuilder.AppendLine(skill.AsMarkdown());
        }
        // Background
        markdownBuilder.AppendLine("#### Background");
        markdownBuilder.AppendLine(Background);
        return markdownBuilder.ToString();

    }

    public void ApplyRestRestore()
    {
        CurrentHP = MaxHP;
        CurrentMP = MaxMP;
    }
}