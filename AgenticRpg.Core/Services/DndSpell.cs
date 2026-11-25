using AgenticRpg.Core.Helpers;
using AgenticRpg.Core.Models.Game;
using System.Text.Json.Serialization;

namespace AgenticRpg.Core.Services;

public class DndSpell
{
    [JsonPropertyName("index")]
    public string Index { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("desc")]
    public List<string> Desc { get; set; }

    [JsonPropertyName("higher_level")]
    public List<string> HigherLevel { get; set; }

    [JsonPropertyName("range")]
    public string Range { get; set; }

    [JsonPropertyName("components")]
    public List<string> Components { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("material")]
    public string Material { get; set; }

    [JsonPropertyName("ritual")]
    public bool Ritual { get; set; }

    [JsonPropertyName("duration")]
    public string Duration { get; set; }

    [JsonPropertyName("concentration")]
    public bool Concentration { get; set; }

    [JsonPropertyName("casting_time")]
    public string CastingTime { get; set; }

    [JsonPropertyName("level")]
    public long Level { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("attack_type")]
    public string AttackType { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("damage")]
    public DndSpellDamage Damage { get; set; }

    [JsonPropertyName("school")]
    public School School { get; set; }

    [JsonPropertyName("classes")]
    public List<School> Classes { get; set; }

    [JsonPropertyName("subclasses")]
    public List<School> Subclasses { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("dc")]
    public DnDDc Dc { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("area_of_effect")]
    public AreaOfEffect AreaOfEffect { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("heal_at_slot_level")]
    public Dictionary<string, string> HealAtSlotLevel { get; set; }

    public static List<DndSpell> FromFile()
    {
        return FileHelper.ExtractFromAssembly<List<DndSpell>>("5e-SRD-Spells.json");
    }
}
public class AreaOfEffect
{
    [JsonPropertyName("type")]
    public string Type { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

public class School
{
    [JsonPropertyName("index")]
    public string Index { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; }
}

public class DndSpellDamage
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("damage_type")]
    public School DamageType { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("damage_at_slot_level")]
    public Dictionary<string, string> DamageAtSlotLevel { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("damage_at_character_level")]
    public Dictionary<string, string> DamageAtCharacterLevel { get; set; }
}

public class DnDDc
{
    [JsonPropertyName("dc_type")]
    public School DcType { get; set; }

    [JsonPropertyName("dc_success")]
    public string DcSuccess { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("desc")]
    public string Desc { get; set; }
}