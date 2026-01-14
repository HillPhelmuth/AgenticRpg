using System.ComponentModel;
using AgenticRpg.Core.Helpers;

namespace AgenticRpg.Core.Models.Game;

/// <summary>
/// Represents a spell in the game
/// </summary>
public class Spell 
{
    [Description("Unique identifier for the spell")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Description("Spell's display name")] public string Name { get; set; } = string.Empty;

    [Description("Spell's description")] public string Description { get; set; } = string.Empty;

    [Description("Spell level (0-9, where 0 is cantrip)")]
    public int Level { get; set; } = 0;

    [Description("School of magic")] public string School { get; set; } = string.Empty;

    [Description("Time required to cast the spell")]
    public string CastingTime { get; set; } = string.Empty;

    [Description("Range of the spell")] public string Range { get; set; } = string.Empty;

    [Description("Duration of the spell effect")]
    public string Duration { get; set; } = string.Empty;

    [Description("Components required to cast the spell")]
    public List<string> Components { get; set; } = [];

    [Description("Human-readable description of the spell effect")]
    public string EffectText { get; set; } = string.Empty;

    [Description("Effect of the spell")]
    public MagicEffect Effect { get; set; } = new();

    public static List<Spell> GetAllWizardSpellsFromFile()
    {
        return FileHelper.ExtractFromAssembly<List<Spell>>("WizardSpellsList.json") ?? [];
    }

    public static List<Spell> GetAllClericSpellsFromFile()
    {
        return FileHelper.ExtractFromAssembly<List<Spell>>("ClericSpellList.json") ?? [];
    }
}