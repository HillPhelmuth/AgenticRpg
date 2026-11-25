using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Models.Game;

namespace AgenticRpg.Core.Rules;

/// <summary>Deterministic creation rules shared by tools and finalization.</summary>
public static class CharacterCreationRules
{
    public static bool IsValidLevel(int level) => level is >= 1 and <= 7;
    public static int AdditionalSkillRanks(int level) => 2 * (level - 1);

    // Each entry represents one known spell, in acquisition order.
    public static List<int> SpellLimits(CharacterClass characterClass, int level)
    {
        var count = characterClass switch
        {
            CharacterClass.Wizard or CharacterClass.Cleric => 4,
            CharacterClass.WarMage => 3,
            CharacterClass.Paladin => 2,
            _ => 0
        };
        var limits = Enumerable.Repeat(1, count).ToList();
        for (var earnedLevel = 2; earnedLevel <= level && count > 0; earnedLevel++)
            if (CharacterProgression.IsSpellGrantLevel(characterClass, earnedLevel))
                limits.Add(CharacterProgression.GetMaxSpellLevelForCharacterLevel(earnedLevel));
        return limits;
    }

    public static string? ValidateSkills(IReadOnlyDictionary<string, int> ranks, IReadOnlyCollection<string> initialSkills, int level)
    {
        var validNames = Skill.GetAllSkillsFromFile().Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (initialSkills.Count != 4 || initialSkills.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 4 ||
            initialSkills.Any(s => !validNames.Contains(s))) return "Select four distinct valid initial skills first.";
        if (ranks.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != ranks.Count ||
            ranks.Any(s => !validNames.Contains(s.Key) || s.Value < 1)) return "Skill names must be valid and distinct, with positive ranks.";
        if (initialSkills.Any(s => !ranks.Any(r => r.Key.Equals(s, StringComparison.OrdinalIgnoreCase) && r.Value >= 1)))
            return "Preserve all four initial skills at rank 1 or higher.";
        if (ranks.Values.Sum(r => (long)r) != 4L + AdditionalSkillRanks(level))
            return $"Allocate exactly {AdditionalSkillRanks(level)} additional skill ranks ({4 + AdditionalSkillRanks(level)} total).";
        return null;
    }

    public static string? ResolveSpells(CharacterClass characterClass, int level, IReadOnlyList<string> names, out List<Spell> spells)
    {
        spells = [];
        var limits = SpellLimits(characterClass, level);
        if (names.Count != limits.Count) return $"Choose exactly {limits.Count} spells, starting spells first and earned spells in level order.";
        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count) return "Choose distinct spells.";
        var available = CharacterProgression.GetSpellsForClass(characterClass, 9);
        for (var i = 0; i < names.Count; i++)
        {
            var spell = available.FirstOrDefault(s => s.Name.Equals(names[i], StringComparison.OrdinalIgnoreCase));
            if (spell is null || spell.Level > limits[i]) return $"Spell '{names[i]}' is invalid for choice {i + 1}; choose a class spell of level 0–{limits[i]}.";
            spells.Add(spell);
        }
        return null;
    }

    public static void ApplySkills(Character character, IReadOnlyDictionary<string, int> ranks)
    {
        var definitions = Skill.GetAllSkillsFromFile();
        character.Skills = ranks.ToDictionary(r => definitions.First(s => s.Name.Equals(r.Key, StringComparison.OrdinalIgnoreCase)).Name, r => r.Value);
        character.CharacterSkills = definitions.Where(s => character.Skills.ContainsKey(s.Name)).ToList();
        character.CharacterSkills.ForEach(s => s.Rank = character.Skills[s.Name]);
    }

    public static void InitializeStats(Character character)
    {
        character.Experience = (character.Level - 1) * 1000;
        character.CurrentHP = character.MaxHP;
        character.CurrentMP = character.MaxMP;
        character.Initiative = StatsCalculator.CalculateInitiative(character);
    }

    public static void InitializeSpellSlots(Character character)
    {
        character.MaxSpellSlots.Clear();
        character.CurrentSpellSlots.Clear();
        var slots = character.Class switch
        {
            CharacterClass.Wizard or CharacterClass.Cleric => 4,
            CharacterClass.WarMage or CharacterClass.Paladin => 2,
            _ => 0
        };
        if (slots > 0) character.MaxSpellSlots[1] = character.CurrentSpellSlots[1] = slots;
    }

    public static string? ValidateDraft(Character character, SessionContext context)
    {
        if (context.IsCharacterModification) return null;
        if (!IsValidLevel(character.Level)) return "Starting level must be between 1 and 7.";
        // Level-one drafts can recover their four base choices without new metadata.
        var initialSkills = character.Level == 1 && context.InitialCharacterSkills.Count == 0
            ? character.Skills.Keys.ToList() : context.InitialCharacterSkills;
        var error = ValidateSkills(character.Skills, initialSkills, character.Level);
        if (error is not null) return error;
        if (character.KnownSpells.Any(s => s is null)) return "Replace invalid known spells.";
        return ResolveSpells(character.Class, character.Level, character.KnownSpells.Select(s => s.Name).ToList(), out _);
    }

    /// <summary>Validate model output before accepting it into the session or invoking media services.</summary>
    public static string? NormalizeQuickCharacter(Character character, int level, out List<string> initialSkills)
    {
        initialSkills = [];
        if (!IsValidLevel(level)) return "Starting level must be between 1 and 7.";
        if (string.IsNullOrWhiteSpace(character.Name) || character.Race == CharacterRace.None || character.Class == CharacterClass.None ||
            !Enum.IsDefined(character.Race) || !Enum.IsDefined(character.Class) ||
            !new[] { AttributeType.Might, AttributeType.Agility, AttributeType.Vitality, AttributeType.Wits, AttributeType.Presence }.All(a => character.Attributes.ContainsKey(a))) return "Generate a name, valid race/class, and all five attributes.";
        initialSkills = character.Skills.Keys.Take(4).ToList();
        var error = ValidateSkills(character.Skills, initialSkills, level);
        if (error is not null) return error;
        if (character.KnownSpells.Any(s => s is null)) return "Generate valid known spells.";
        error = ResolveSpells(character.Class, level, character.KnownSpells.Select(s => s.Name).ToList(), out var spells);
        if (error is not null) return error;
        character.Level = level;
        ApplySkills(character, character.Skills);
        character.KnownSpells = spells;
        InitializeStats(character);
        InitializeSpellSlots(character);
        return character.MaxHP > 0 ? null : "Generate attributes yielding positive hit points.";
    }
}
