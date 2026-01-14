using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Rules;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using AgenticRpg.Core.Helpers;

namespace AgenticRpg.Core.Models.Game;

/// <summary>
/// Represents a monster entity within the RPG game system.
/// Monsters serve as adversaries, allies, or neutral entities that characters can interact with.
/// </summary>
public class RpgMonster : InteractiveEntity
{
    
    // Basic identification
    /// <summary>
    /// Gets the unique identifier for the monster, using Name plus a new GUID.
    /// </summary>
    [Description("Unique identifier of the monster (get only)")]

    public string Id => Name;

    /// <summary>
    /// Gets or sets the name of the monster.
    /// </summary>
    [Description("Name of the monster")]
    
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the descriptive text about the monster's appearance, behavior, and other notable characteristics.
    /// </summary>
    [Description("Detailed description of the monster")]
    
    public required string Description { get; set; }

    // Challenge information
    /// <summary>
    /// Gets or sets the challenge rating that represents the monster's difficulty.
    /// Higher values indicate more challenging monsters.
    /// </summary>
    [Description("Difficulty rating of the monster")]
    
    public int ChallengeRating { get; set; } = 1;

    /// <summary>
    /// Gets or sets the monster's type category (e.g., Humanoid, Beast, Undead).
    /// </summary>
    [Description("Category or species of the monster (e.g., Humanoid, Beast, Undead)")]
    
    public string Type { get; set; } = "Humanoid";

    
    // Combat stats

    /// <summary>
    /// Gets the monster's initiative score, calculated from Wits and Agility.
    /// Determines turn order in combat.
    /// </summary>
    [Description("Combat turn order roll bonus value based on Wits and Agility")]
    
    public int InitiativeBonus => AttributeCalculator.CalculateModifier(Wits) + AttributeCalculator.CalculateModifier(Agility);

    // Display information
    /// <summary>
    /// Gets or sets the URL to the monster's image for visual representation.
    /// </summary>
    [Description("URL to the monster's visual representation")]
    
    public string? ImageUrl { get; set; }

    // Combat abilities
    /// <summary>
    /// Gets or sets the list of attacks this monster can perform in combat.
    /// </summary>
    [Description("List of special attacks the monster can perform")]
    
    public List<WeaponItem> SpecialAttacks { get; set; } = [];
    
    public required WeaponItem DefaultAttack { get; set; }
    // State tracking
    /// <summary>
    /// Gets or sets the monster's disposition toward players (Friendly, Neutral, or Hostile).
    /// </summary>
    [Description("Attitude of the monster toward players (Friendly, Neutral, or Hostile).")]
    
    public NPCDisposition Disposition { get; set; } = NPCDisposition.Hostile;

    public static List<RpgMonster> GetAllRpgMonsters(params int[] monsterChallengeRatings)
    {
        var allMonsters = FileHelper.ExtractFromAssembly<List<RpgMonster>>("RpgMonsters.json");
        if (monsterChallengeRatings == null || monsterChallengeRatings.Length == 0)
        {
            return allMonsters;
        }
        return allMonsters.Where(m => monsterChallengeRatings.Contains(m.ChallengeRating)).ToList();
    }

    public static RpgMonster? GetRpgMonsterByName(string name)
    {
        var allMonsters = FileHelper.ExtractFromAssembly<List<RpgMonster>>("RpgMonsters.json");
        return allMonsters.FirstOrDefault(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
    public string ToMarkdown()
    {
        var markdown = new StringBuilder();
        // Monster title and descriptions
        markdown.AppendLine($"# Monster Id: {Id}");
        markdown.AppendLine($"## {Name ?? "Unnamed Monster"}");
        if (!string.IsNullOrEmpty(Description))
        {
            markdown.AppendLine();
            markdown.AppendLine(Description);
        }
        markdown.AppendLine();
        //markdown.AppendLine($"![{Name} Image]({ImageUrl})");
        markdown.AppendLine($"<img src='{ImageUrl}' alt='{Name} Image' />");
        // Monster attributes section as table
        markdown.AppendLine();
        markdown.AppendLine("### Attributes");
        markdown.AppendLine("| Attribute | Value |");
        markdown.AppendLine("|-----------|-------|");
        markdown.AppendLine($"| **Might** | {Might} |");
        markdown.AppendLine($"| **Agility** | {Agility} |");
        markdown.AppendLine($"| **Wits** | {Wits} |");
        markdown.AppendLine($"| **Vitality** | {Vitality} |");
        markdown.AppendLine($"| **Presence** | {Presence} |");
        
        // Monster combat stats section as table
        markdown.AppendLine();
        markdown.AppendLine("### Combat Stats");
        markdown.AppendLine("| Stat | Value |");
        markdown.AppendLine("|------|-------|");
        markdown.AppendLine($"| **HP** | {CurrentHP}/{MaxHP} |");
        markdown.AppendLine($"| **Armor Class** | {ArmorClass} |");
        markdown.AppendLine($"| **Initiative** | {InitiativeBonus} |");
        markdown.AppendLine($"| **Speed** | {Speed} ft/turn |");
        
        // Monster attacks section as table
        if (!(SpecialAttacks?.Count > 0)) return markdown.ToString();
        markdown.AppendLine();
        markdown.AppendLine("### Attacks");
        markdown.AppendLine("| Name | Attack Bonus | Damage | Range | Description |");
        markdown.AppendLine("|------|--------------|--------|-------|-------------|");
        foreach (var attack in SpecialAttacks)
        {
            var description = !string.IsNullOrEmpty(attack.Description) ? attack.Description : "none";
            markdown.AppendLine($"| **{attack.Name}** | +{attack.Description} | {attack.DamageDice} {attack.DamageType} | {attack.WeaponType} | {description} |");
        }
        return markdown.ToString();
    }
}