using System.ComponentModel;
using System.Text;
using AgenticRpg.Core.Helpers;
using AgenticRpg.Core.Models.Enums;

namespace AgenticRpg.Core.Models.Game;

public class Skill
{
    [Description("Name of the skill")] public string Name { get; set; } = string.Empty;

    [Description("Rank or level of proficiency in the skill")]
    public int Rank { get; set; } = 0;

    [Description("Associated attribute for the skill")]
    public AttributeType AssociatedAttribute { get; set; } = AttributeType.Wits;

    [Description("Detailed description of the skill")]
    public string Description { get; set; } = string.Empty;

    [Description("Effects or bonuses provided by the skill")]
    public string Effect { get; set; } = string.Empty;

    public static List<Skill> GetAllSkillsFromFile()
    {
        return FileHelper.ExtractFromAssembly<List<Skill>>("SkillsList.json");
    }

    public static Skill GetSkillFromName(string name)
    {
        var skills = GetAllSkillsFromFile();
        return skills.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))!;
    }

    public string AsMarkdown()
    {
        var markdownBuilder = new StringBuilder();
        markdownBuilder.AppendLine($"**{Name}** (Rank: {Rank})");
        markdownBuilder.AppendLine($"**Associated Attribute:** {AssociatedAttribute}");
        markdownBuilder.AppendLine($"**Description:** {Description}");
        markdownBuilder.AppendLine($"**Effects:** {Effect}");
        return markdownBuilder.ToString();
    }
}