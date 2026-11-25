using AgenticRpg.Core.Agents;
using AgenticRpg.Core.Agents.Llms;
using AgenticRpg.Core.Helpers;
using AgenticRpg.Core.Models.Game;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel;
using OpenAI;
using System;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using ChatResponseFormat = Microsoft.Extensions.AI.ChatResponseFormat;
#pragma warning disable OPENAI001

namespace AgenticRpg.Core.Services;

public class DndApiService
{
    private static List<DndMonster>? _allMonsters;
    private static List<DndMonster> AllDndMonsters => _allMonsters ??= FileHelper.ExtractFromAssembly<List<DndMonster>>("MonsterDndCache.json");
    private const string ApiBaseUrl = "https://www.dnd5eapi.co";
    public static DndMonster? GetMonsterByName(string name)
    {
        var monster = AllDndMonsters.FirstOrDefault(m => m.Index?.Equals(name, StringComparison.CurrentCultureIgnoreCase) == true);
        return monster;
    }
    private static HttpClient _httpClient = new() { BaseAddress = new Uri(ApiBaseUrl) };
    public static async Task<RpgMonster?> CreateMonsterByNameAsync(string name)
    {
        var monster = GetMonsterByName(name);
        if (monster == null)
        {
            return null;
        }

        return await ConvertMonsterAsync(monster);
    }
    public static string GetAvailableMonsterNames(params double[] challengeRatings)
    {
        var monsters = GetMonstersByChallengeRating(challengeRatings);
        return string.Join(", ", monsters.Select(m => m.Index!).ToList());
    }
    private static IEnumerable<DndMonster> GetMonstersByChallengeRating(params double[] challengeRatings)
    {
        if (challengeRatings.Length == 0) return AllDndMonsters;
        var validChallengeRatings = challengeRatings.ToList();
        if (challengeRatings.Any(x => x <= 1))
        {
            validChallengeRatings.AddRange([0.125, 0.25, 0.5]);
        }
        return AllDndMonsters.Where(m => validChallengeRatings.Contains(m.ChallengeRating));
    }

    public static IEnumerable<string> GetMonstersBasicInfo(string challengeRating = "")
    {
        var challengeRatings = ParseChallengeRatings(challengeRating);
        return GetMonstersByChallengeRating(challengeRatings).Select(m => m.ToBasicInfoMarkdown());
    }
    public static async Task<MonsterEncounter> CreateRandomMonsterEncounter(int count, string challengeRating = "")
    {
        var challengeRatings = ParseChallengeRatings(challengeRating);
        return await CreateRandomMonsterEncounter(count, challengeRatings);
    }
    public static async Task<MonsterEncounter> CreateRandomMonsterEncounter(int count, params double[] challengeRatings)
    {
        var monsters = GetMonstersByChallengeRating(challengeRatings);
        var selectedMonsters = monsters.OrderBy(x => Guid.NewGuid()).Take(count).ToList();

        var finalList = new List<RpgMonster?>();
        var taskList = new List<Task<RpgMonster?>>();
        foreach (var monster in selectedMonsters)
        {
            taskList.Add(ConvertMonsterAsync(monster));
        }
        finalList.AddRange(await Task.WhenAll(taskList));
        var monsterEncounter = new MonsterEncounter(finalList.Where(x => x is not null).ToList()!);
        return monsterEncounter;
    }

    public static async Task<DndSpells> GetSpellData()
    {
        var response = await _httpClient.GetStringAsync("/api/2014/spells?school=divination");
        return JsonSerializer.Deserialize<DndSpells>(response);
    }
    public static async Task<List<JsonElement>> RetrieveSpells(DndSpells? spellMetadata = null)
    {
        spellMetadata ??= DndSpells.FromFile();
        var result = new List<JsonElement>();
        foreach (var spellData in spellMetadata.Results)
        {
            var response = await _httpClient.GetStringAsync(spellData.Url);
            var spellJson = JsonSerializer.Deserialize<JsonElement>(response);
            result.Add(spellJson);
        }
        return result;
    }
    public static async Task<List<DndSpell>> RetrieveSpellsAsDndSpells()
    {
        return DndSpell.FromFile();
        var spellMetadata = DndSpells.FromFile();
        var result = new List<DndSpell>();
        foreach (var spellData in spellMetadata.Results)
        {
            var response = await _httpClient.GetStringAsync(spellData.Url);
            var spellJson = JsonSerializer.Deserialize<JsonElement>(response);
            var spell = JsonSerializer.Deserialize<DndSpell>(spellJson.GetRawText());
            if (spell != null)
            {
                result.Add(spell);
            }
        }
        return result;
    }

    // # Reason: Keep challenge rating parsing consistent across entry points.
    private static double[] ParseChallengeRatings(string challengeRating)
    {
        if (string.IsNullOrWhiteSpace(challengeRating))
        {
            return [];
        }

        return challengeRating
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => double.TryParse(x, out _))
            .Select(double.Parse)
            .ToArray();
    }

    public static async Task<Spell> ConvertSpellAsync(JsonElement spellData)
    {
        var prompt =
            $"""
             Convert the spell below from the D&D format provided to the proper json schema format. Find the json fields that most closely match the D&D fields and map them accordingly.

             **Spell**
             ```
             {JsonSerializer.Serialize(spellData, new JsonSerializerOptions() { WriteIndented = true })}
             ```
             """;
        var config = AgentStaticConfiguration.Default;
        var client = new OpenAIClient(new ApiKeyCredential(config.OpenRouterApiKey!), new OpenAIClientOptions() { Endpoint = new Uri(config.OpenRouterEndpoint), ClientLoggingOptions = new ClientLoggingOptions() { LoggerFactory = LoggerFactory.Create(builder => builder.AddConsole()), EnableMessageLogging = true, EnableLogging = true, EnableMessageContentLogging = true} });
        var chatClient = client.GetChatClient("openai/gpt-oss-120b").AsIChatClient();
        var agent = chatClient.AsAIAgent(new ChatClientAgentOptions()
        {
            Name = "Spell Converter",
            Description = "Converts D&D spells to RPG spells in JSON format.",
            ChatOptions = new ChatOptions()
            {
                Instructions = prompt,
                ResponseFormat = ChatResponseFormat.ForJsonSchema<Spell>(),
                RawRepresentationFactory = _ => new OpenRouterChatCompletionOptions()
                { Provider = new Provider() { Sort = "throughput" } }
            }
        });
        var response = await agent.RunAsync<Spell>();
        var rpgSpell = response.Result;
        return rpgSpell;
    }

    public static async Task<Spell?> ConvertSpellAsync(DndSpell spellData)
    {
        try
        {
            var prompt =
                $"""
                 Convert spells described in D&D format into the target JSON schema (ResponseFormat) by matching D&D fields to their closest corresponding JSON fields.  
                 Carefully reason through the mapping of fields step by step before producing the final output.  
                 Think explicitly about:  
                 - Which D&D fields map directly and which require adaptation or supplementation.  
                 - D&D components (V,S,M), classes, and levels mapping to schema enumerations or integer levels.  
                 - Handling spell effects (healing, damage, buffs, debuffs, utility), area of effect, dice expressions, and duration conversions.  
                 - Ensuring all required fields in the schema are completed, assigning null or best-fit values where D&D leaves data out.  
                 - For nested fields like Effect, reason through each substype's mapping before output.  
                 Do not output your final JSON answer until all fields and mappings are planned and checked.
                 
                 For every request, persist until all objectives are fully met.  
                 Chain-of-thought reason internally about the mapping and any necessary rewrites or assumptions.
                 
                 Output Format:  
                 The complete answer MUST be valid JSON matching the ResponseFormat schema, and must NOT be wrapped in a code block.
                 
                 **Example Conversion Step-by-Step Reasoning:**  
                 - D&D field: Name -> JSON.Name  
                 - D&D field: Level -> JSON.Level (0 = cantrip)  
                 - D&D field: Classes -> JSON.SpellClass ("WizardAndWarMage" for wizard-like, "ClericAndPaladin" for any spells that contain cleric in "classes" array)  
                 - D&D field: School -> JSON.School  
                 - D&D field: Casting Time -> JSON.CastingTime  
                 - D&D field: Range -> JSON.Range  
                 - D&D field: Duration -> JSON.Duration  
                 - D&D field: Components V,S,M -> JSON.Components (e.g., ["V","S","M"])  
                 - D&D spell description/effect -> JSON.Description, JSON.EffectText  
                 - For effect: If healing/damage, parse dice (1d8 etc), assign EffectType, EffectDice, AreaOfEffect; assign magnitude for static effects such as buffs.  
                 - Fill any missing required fields with null or most reasonable default, using schema enums.

                 **Spell**
                 ```
                 {JsonSerializer.Serialize(spellData, new JsonSerializerOptions() { WriteIndented = true })}
                 ```
                 """;
            var config = AgentStaticConfiguration.Default;
            var client = new OpenAIClient(new ApiKeyCredential(config.OpenRouterApiKey!),
                new OpenAIClientOptions()
                {
                    Endpoint = new Uri(config.OpenRouterEndpoint),
                    ClientLoggingOptions = new ClientLoggingOptions()
                    {
                        LoggerFactory = LoggerFactory.Create(builder => builder.AddConsole()),
                        EnableMessageLogging = true, EnableLogging = true, EnableMessageContentLogging = true
                    }
                });
            var chatClient = client.GetChatClient("openai/gpt-oss-120b").AsIChatClient();
            var agent = chatClient.AsAIAgent(new ChatClientAgentOptions()
            {
                Name = "Spell Converter",
                Description = "Converts D&D spells to RPG spells in JSON format.",
                ChatOptions = new ChatOptions()
                {
                    Instructions = prompt,
                    ResponseFormat = ChatResponseFormat.ForJsonSchema<Spell>(),
                    RawRepresentationFactory = _ => new OpenRouterChatCompletionOptions()
                    {
                        Provider = new Provider() { Sort = "throughput" },
                        ReasoningEffortLevel = ChatReasoningEffortLevel.High
                    }
                }
            });
            var response = await agent.RunAsync<Spell>();
            var rpgSpell = response.Result;
            return rpgSpell;
        }
        catch
        {
            return null;
        }
    }
    private static async Task<RpgMonster?> ConvertMonsterAsync(DndMonster monster)
    {
        try
        {
            var prompt =
                $"""
                Convert the monster below from the D&D format provided to the proper json schema format. Find the json fields that most closely match the D&D fields and map them accordingly.
                
                **Monster**
                ```
                {JsonSerializer.Serialize(monster, new JsonSerializerOptions() { WriteIndented = true })}
                ```
                """;
            var config = AgentStaticConfiguration.Default;
            var client = new OpenAIClient(new ApiKeyCredential(config.OpenRouterApiKey!), new OpenAIClientOptions() { Endpoint = new Uri(config.OpenRouterEndpoint), ClientLoggingOptions = new ClientLoggingOptions() { LoggerFactory = LoggerFactory.Create(builder => builder.AddConsole()) } });
            var chatClient = client.GetChatClient("openai/gpt-oss-120b").AsIChatClient();
            var agent = chatClient.AsAIAgent(new ChatClientAgentOptions()
            {
                Name = "Monster Converter",
                Description = "Converts D&D monsters to RPG monsters in JSON format.",
                ChatOptions = new ChatOptions()
                {
                    Instructions = prompt,
                    ResponseFormat = ChatResponseFormat.ForJsonSchema<RpgMonster>(),
                    RawRepresentationFactory = _ => new OpenRouterChatCompletionOptions()
                    { Provider = new Provider() { Sort = "throughput" } }
                }
            });
            var response = await agent.RunAsync<RpgMonster>();
            var rpgMonster = response.Result;
            if (monster.ChallengeRating <= 10 && rpgMonster.ArmorClass >= 10)
            {
                rpgMonster.ArmorClass -= (Math.Min(3, rpgMonster.ArmorClass - (int)monster.ChallengeRating));
            }
#if DEBUG

#endif
            return rpgMonster;
        }
        catch
        {
            return null;
        }
    }
}

public class DndMonster
{
    public string ToBasicInfoMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## {Name}");
        sb.AppendLine($"**Challenge Rating:** {ChallengeRating}");
        sb.AppendLine($"**Size:** {Size}");
        sb.AppendLine($"**Armor Class:** {ArmorClass.First().Value}");
        sb.AppendLine($"**Hit Points:** {HitPoints}");
        sb.AppendLine($"**XP:** {Xp}");
        sb.AppendLine();
        return sb.ToString();
    }
    [JsonPropertyName("index")]
    public string? Index { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("size")]
    public string? Size { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("alignment")]
    public string? Alignment { get; set; }

    [JsonPropertyName("armor_class")]
    public List<ArmorClass>? ArmorClass { get; set; }

    [JsonPropertyName("hit_points")]
    public int HitPoints { get; set; }

    [JsonPropertyName("hit_dice")]
    public string? HitDice { get; set; }

    [JsonPropertyName("hit_points_roll")]
    public string? HitPointsRoll { get; set; }

    [JsonPropertyName("speed")]
    public Speed? Speed { get; set; }

    [JsonPropertyName("strength")]
    public int Strength { get; set; }

    [JsonPropertyName("dexterity")]
    public int Dexterity { get; set; }

    [JsonPropertyName("constitution")]
    public int Constitution { get; set; }

    [JsonPropertyName("intelligence")]
    public int Intelligence { get; set; }

    [JsonPropertyName("wisdom")]
    public int Wisdom { get; set; }

    [JsonPropertyName("charisma")]
    public int Charisma { get; set; }

    [JsonPropertyName("proficiencies")]
    public List<ProficiencyElement>? Proficiencies { get; set; }

    [JsonPropertyName("damage_vulnerabilities")]
    public List<object>? DamageVulnerabilities { get; set; }

    [JsonPropertyName("damage_resistances")]
    public List<object>? DamageResistances { get; set; }

    [JsonPropertyName("damage_immunities")]
    public List<object>? DamageImmunities { get; set; }

    [JsonPropertyName("condition_immunities")]
    public List<object>? ConditionImmunities { get; set; }

    [JsonPropertyName("senses")]
    public Senses? Senses { get; set; }

    [JsonPropertyName("languages")]
    public string? Languages { get; set; }

    [JsonPropertyName("challenge_rating")]
    public double ChallengeRating { get; set; }

    [JsonPropertyName("proficiency_bonus")]
    public int ProficiencyBonus { get; set; }

    [JsonPropertyName("xp")]
    public int Xp { get; set; }

    [JsonPropertyName("special_abilities")]
    public List<SpecialAbility>? SpecialAbilities { get; set; }

    [JsonPropertyName("actions")]
    public List<DndMonsterAction>? Actions { get; set; }

    [JsonPropertyName("legendary_actions")]
    public List<LegendaryAction>? LegendaryActions { get; set; }

    [JsonPropertyName("image")]
    public string? ImageUrl { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

public class DndMonsterAction
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("multiattack_type")]
    public string? MultiattackType { get; set; }

    [JsonPropertyName("desc")]
    public string? Desc { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("actions")]
    public List<ActionAction>? Actions { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("attack_bonus")]
    public int? AttackBonus { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("dc")]
    public Dc? Dc { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("damage")]
    public List<Damage>? Damage { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("usage")]
    public Usage? Usage { get; set; }
}

public class ActionAction
{
    [JsonPropertyName("action_name")]
    public string? ActionName { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }
}

public class Damage
{
    [JsonPropertyName("damage_type")]
    public DcTypeClass? DamageType { get; set; }

    [JsonPropertyName("damage_dice")]
    public string? DamageDice { get; set; }
}

public class DcTypeClass
{
    [JsonPropertyName("index")]
    public string? Index { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

public class Dc
{
    [JsonPropertyName("dc_type")]
    public DcTypeClass? DcType { get; set; }

    [JsonPropertyName("dc_value")]
    public int DcValue { get; set; }

    [JsonPropertyName("success_type")]
    public string? SuccessType { get; set; }
}

public class Usage
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("times")]
    public int Times { get; set; }
}

public class ArmorClass
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("value")]
    public int Value { get; set; }
}

public class LegendaryAction
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("desc")]
    public string? Desc { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("attack_bonus")]
    public int? AttackBonus { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("damage")]
    public List<DndSpellDamage>? Damage { get; set; }
}

public class ProficiencyElement
{
    [JsonPropertyName("value")]
    public int Value { get; set; }

    [JsonPropertyName("proficiency")]
    public DcTypeClass? Proficiency { get; set; }
}

public class Senses
{
    [JsonPropertyName("darkvision")]
    public string? Darkvision { get; set; }

    [JsonPropertyName("passive_perception")]
    public int PassivePerception { get; set; }
}

public class SpecialAbility
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("desc")]
    public string? Desc { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("dc")]
    public Dc? Dc { get; set; }
}

public class Speed
{
    [JsonPropertyName("walk")]
    public string? Walk { get; set; }

    [JsonPropertyName("swim")]
    public string? Swim { get; set; }
}
public class MonsterEncounter(List<RpgMonster> monsters)
{
    public string Id => $"{string.Join("-", Monsters.Select(x => x.Name))}";
    public List<RpgMonster> Monsters { get; set; } = monsters;
    public DateTime EncounterStart { get; set; } = DateTime.Now;
    public DateTime EncounterEnd { get; set; }

}
public class DndSpells
{
    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("results")]
    public List<SpellMetadata> Results { get; set; }

    public static DndSpells FromFile()
    {
        return FileHelper.ExtractFromAssembly<DndSpells>("5e-SRD-Spells.json");
    }
}

public class SpellMetadata
{
    [JsonPropertyName("index")]
    public string Index { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("level")]
    public int Level { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; }
}

