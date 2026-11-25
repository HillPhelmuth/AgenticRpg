using System.Text.Json;
using AgenticRpg.Core.Agents;
using AgenticRpg.Core.Agents.Tools;
using AgenticRpg.Core.Agents.Tools.Results;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Models.Game;
using AgenticRpg.Core.Rules;
using AgenticRpg.DiceRoller.Models;
using Moq;

namespace AgenticRpg.PlayerExperienceTests;

public class CharacterCreationProgressionTests
{
    public static IEnumerable<object[]> LevelsAndClasses() =>
        from level in Enumerable.Range(1, 7)
        from characterClass in Enum.GetValues<CharacterClass>().Where(c => c != CharacterClass.None)
        select new object[] { level, characterClass };

    private static List<string> InitialSkills() => Skill.GetAllSkillsFromFile().Take(4).Select(s => s.Name).ToList();
    private static CharacterCreationTools Tools(TestRuntime runtime) => new(runtime.Characters.Object, runtime.Sessions,
        new AgentConfiguration(), Mock.Of<IRollDiceService>(), runtime.Completion);

    private static Character Generated(int level, CharacterClass characterClass)
    {
        var character = TestRuntime.Character();
        character.Class = characterClass;
        character.Level = 99; // The model's level must never override the player's choice.
        character.Skills = InitialSkills().ToDictionary(s => s, _ => 1);
        character.Skills[character.Skills.Keys.First()] += 2 * (level - 1);
        var limits = CharacterCreationRules.SpellLimits(characterClass, level);
        var available = CharacterProgression.GetSpellsForClass(characterClass, 9);
        foreach (var limit in limits)
            character.KnownSpells.Add(available.First(s => s.Level <= limit && character.KnownSpells.All(k => k.Name != s.Name)));
        return character;
    }

    [Theory]
    [MemberData(nameof(LevelsAndClasses))]
    public async Task EveryStartingLevelAndClassHasFullProgression(int level, CharacterClass characterClass)
    {
        var character = Generated(level, characterClass);
        Assert.Null(CharacterCreationRules.NormalizeQuickCharacter(character, level, out var initial));
        Assert.Equal(level, character.Level);
        Assert.Equal((level - 1) * 1000, character.Experience);
        Assert.False(character.RequiresLevelUp);
        Assert.Equal(character.MaxHP, character.CurrentHP);
        Assert.Equal(character.MaxMP, character.CurrentMP);
        Assert.Equal(4 + 2 * (level - 1), character.Skills.Values.Sum());
        Assert.All(character.CharacterSkills, s => Assert.Equal(character.Skills[s.Name], s.Rank));
        var starterSpells = characterClass switch { CharacterClass.Wizard or CharacterClass.Cleric => 4, CharacterClass.WarMage => 3, CharacterClass.Paladin => 2, _ => 0 };
        var earnedSpells = characterClass switch { CharacterClass.Wizard or CharacterClass.Cleric => level / 2, CharacterClass.WarMage or CharacterClass.Paladin => level / 3, _ => 0 };
        Assert.Equal(starterSpells + earnedSpells, character.KnownSpells.Count);
        Assert.Equal(character.MaxSpellSlots, character.CurrentSpellSlots);
        Assert.Equal(starterSpells == 0 ? 0 : characterClass is CharacterClass.Wizard or CharacterClass.Cleric ? 4 : 2,
            character.MaxSpellSlots.Values.Sum());

        var runtime = new TestRuntime();
        var session = await runtime.Sessions.CreateSessionAsync(SessionType.CharacterCreation, "alice");
        session.Context.DraftCharacter = character;
        session.Context.InitialCharacterSkills = initial;
        await runtime.Completion.FinalizeCharacterAsync(session.SessionId, requireIdle: true);
        Assert.True(session.IsCompleted);
    }

    [Theory]
    [MemberData(nameof(LevelsAndClasses))]
    public async Task StepByStepUsesTheChosenLevel(int level, CharacterClass characterClass)
    {
        var runtime = new TestRuntime();
        var session = await runtime.Sessions.CreateSessionAsync(SessionType.CharacterCreation, "alice");
        var tools = Tools(runtime);
        await tools.SaveStartingLevel(session.SessionId, level);
        await tools.SaveRaceChoice(session.SessionId, CharacterRace.Human);
        await tools.SaveClassChoice(session.SessionId, characterClass);
        await tools.SaveNameAndSkills(session.SessionId, "Alice", InitialSkills().ToArray());
        var generated = Generated(level, characterClass);
        await tools.SaveStartingSkillRanks(session.SessionId, generated.Skills);
        if (generated.KnownSpells.Count > 0)
            await tools.SaveKnownSpells(session.SessionId, generated.KnownSpells.Select(s => s.Name).ToArray());
        Assert.Equal(level, session.Context.DraftCharacter!.Level);
        var result = JsonSerializer.Deserialize<SaveCharacterResult>(await tools.FinalizeCharacter(session.SessionId));
        Assert.True(result!.Success, result.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BothFinalizationPathsRejectInvalidLevelsAndProgression(bool requireIdle)
    {
        var runtime = new TestRuntime();
        var session = await runtime.Sessions.CreateSessionAsync(SessionType.CharacterCreation, "alice");
        var character = Generated(3, CharacterClass.Wizard);
        session.Context.DraftCharacter = character;
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.Completion.FinalizeCharacterAsync(session.SessionId, requireIdle));
        character.Level = 3;
        session.Context.InitialCharacterSkills = InitialSkills();
        character.KnownSpells[0] = new Spell { Name = "Invented" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.Completion.FinalizeCharacterAsync(session.SessionId, requireIdle));
        Assert.False(JsonSerializer.Deserialize<SaveCharacterResult>(await Tools(runtime).FinalizeCharacter(session.SessionId))!.Success);
        Assert.False(session.IsCompleted);
        runtime.Characters.Verify(r => r.CreateAsync(It.IsAny<Character>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void EarnedSpellsMustRespectEachGrantLevelRatherThanTheFinalLevel()
    {
        var names = Generated(7, CharacterClass.Wizard).KnownSpells.Select(s => s.Name).ToArray();
        names[6] = CharacterProgression.GetSpellsForClass(CharacterClass.Wizard, 3).First(s => s.Level == 3).Name;
        Assert.Null(CharacterCreationRules.ResolveSpells(CharacterClass.Wizard, 7, names, out _));
        (names[4], names[6]) = (names[6], names[4]);
        Assert.NotNull(CharacterCreationRules.ResolveSpells(CharacterClass.Wizard, 7, names, out _));
        names = Generated(7, CharacterClass.Wizard).KnownSpells.Select(s => s.Name).ToArray();
        names[6] = CharacterProgression.GetSpellsForClass(CharacterClass.Wizard, 4).First(s => s.Level == 4).Name;
        Assert.NotNull(CharacterCreationRules.ResolveSpells(CharacterClass.Wizard, 7, names, out _));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(8)]
    public async Task InvalidLevelsAndQuickCreationRejectBeforeExternalCalls(int level)
    {
        var runtime = new TestRuntime();
        var session = await runtime.Sessions.CreateSessionAsync(SessionType.CharacterCreation, "alice");
        session.Context.DraftCharacter = TestRuntime.Character();
        var before = JsonSerializer.Serialize(session);
        var tools = Tools(runtime);
        Assert.False(JsonSerializer.Deserialize<CharacterSheetResult>(await tools.SaveStartingLevel(session.SessionId, level))!.Valid);
        Assert.False(JsonSerializer.Deserialize<CharacterSheetResult>(await tools.CreateQuickCharacter(session.SessionId, "warrior", "Alice", startingLevel: level))!.Valid);
        Assert.Equal(before, JsonSerializer.Serialize(session));
    }

    [Fact]
    public async Task LevelChangesResetProgressionAndRepeatedCallsAreIdempotent()
    {
        var runtime = new TestRuntime();
        var session = await runtime.Sessions.CreateSessionAsync(SessionType.CharacterCreation, "alice");
        var tools = Tools(runtime);
        await tools.SaveStartingLevel(session.SessionId, 7);
        await tools.SaveRaceChoice(session.SessionId, CharacterRace.Human);
        await tools.SaveClassChoice(session.SessionId, CharacterClass.Wizard);
        await tools.SaveNameAndSkills(session.SessionId, "Alice", InitialSkills().ToArray());
        Assert.Equal(7, session.Context.DraftCharacter!.Level);
        var generated = Generated(7, CharacterClass.Wizard);
        await tools.SaveStartingSkillRanks(session.SessionId, generated.Skills);
        await tools.SaveKnownSpells(session.SessionId, generated.KnownSpells.Select(s => s.Name).ToArray());
        var before = JsonSerializer.Serialize(session.Context.DraftCharacter);
        var updatedAt = session.Context.DraftCharacter.UpdatedAt;
        await tools.SaveStartingSkillRanks(session.SessionId, generated.Skills);
        await tools.SaveKnownSpells(session.SessionId, generated.KnownSpells.Select(s => s.Name).ToArray());
        session.Context.DraftCharacter.UpdatedAt = updatedAt;
        Assert.Equal(before, JsonSerializer.Serialize(session.Context.DraftCharacter));
        await tools.SaveStartingLevel(session.SessionId, 3);
        Assert.Equal(4, session.Context.DraftCharacter.Skills.Values.Sum());
        Assert.Empty(session.Context.DraftCharacter.KnownSpells);
        Assert.Equal(2000, session.Context.DraftCharacter.Experience);
        Assert.Equal(session.Context.DraftCharacter.MaxHP, session.Context.DraftCharacter.CurrentHP);
        var restored = JsonSerializer.Deserialize<SessionState>(JsonSerializer.Serialize(session))!;
        Assert.Equal(InitialSkills(), restored.Context.InitialCharacterSkills);
        Assert.NotNull(CharacterCreationRules.ValidateDraft(restored.Context.DraftCharacter!, restored.Context));
        Assert.Contains("additional skill ranks", (await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.Completion.FinalizeCharacterAsync(session.SessionId))).Message);
        Assert.False(session.IsCompleted);
    }

    [Fact]
    public async Task BadSkillAndSpellSelectionsLeaveTheDraftUntouched()
    {
        var runtime = new TestRuntime();
        var session = await runtime.Sessions.CreateSessionAsync(SessionType.CharacterCreation, "alice");
        var character = Generated(7, CharacterClass.Wizard);
        Assert.Null(CharacterCreationRules.NormalizeQuickCharacter(character, 7, out var initial));
        session.Context.DraftCharacter = character;
        session.Context.InitialCharacterSkills = initial;
        var tools = Tools(runtime);
        var before = JsonSerializer.Serialize(character);
        var badRanks = new Dictionary<string, int>(character.Skills) { ["Invented skill"] = 1 };
        Assert.False(JsonSerializer.Deserialize<CharacterSheetResult>(await tools.SaveStartingSkillRanks(session.SessionId, badRanks))!.Valid);
        var names = character.KnownSpells.Select(s => s.Name).ToArray();
        names[0] = "Invented spell";
        Assert.False(JsonSerializer.Deserialize<CharacterSheetResult>(await tools.SaveKnownSpells(session.SessionId, names))!.Valid);
        names[0] = names[1];
        Assert.False(JsonSerializer.Deserialize<CharacterSheetResult>(await tools.SaveKnownSpells(session.SessionId, names))!.Valid);
        names[0] = CharacterProgression.GetSpellsForClass(CharacterClass.Wizard, 9).First(s => s.Level > 1).Name;
        Assert.False(JsonSerializer.Deserialize<CharacterSheetResult>(await tools.SaveKnownSpells(session.SessionId, names))!.Valid);
        Assert.Equal(before, JsonSerializer.Serialize(character));
        Assert.False(JsonSerializer.Deserialize<CharacterSheetResult>(await tools.SaveStartingLevel("missing", 3))!.Valid);
        Assert.False(JsonSerializer.Deserialize<CharacterSheetResult>(await tools.CreateQuickCharacter("missing", "warrior", null))!.Valid);
    }

    [Fact]
    public void InvalidGeneratedProgressionIsRejectedWithoutNormalizing()
    {
        var character = Generated(7, CharacterClass.Wizard);
        character.Skills[character.Skills.Keys.First()] = 1;
        var before = JsonSerializer.Serialize(character);
        Assert.NotNull(CharacterCreationRules.NormalizeQuickCharacter(character, 7, out _));
        Assert.Equal(before, JsonSerializer.Serialize(character));
        character = Generated(7, CharacterClass.Wizard);
        character.KnownSpells[0] = new Spell { Name = "Invented" };
        Assert.NotNull(CharacterCreationRules.NormalizeQuickCharacter(character, 7, out _));
    }

    [Fact]
    public async Task ModificationAboveLevelSevenPreservesLevelAndExperience()
    {
        var runtime = new TestRuntime();
        var character = TestRuntime.Character();
        character.Level = 12;
        character.Experience = 11500;
        character.Skills[character.Skills.Keys.First()] = 9;
        runtime.Characters.Setup(r => r.GetByPlayerIdAsync("alice", It.IsAny<CancellationToken>())).ReturnsAsync(new List<Character> { character });
        var session = await runtime.Sessions.CreateSessionAsync(SessionType.CharacterCreation, "alice");
        var tools = Tools(runtime);
        await tools.LoadCharacterToModify(session.SessionId, "alice", character.Name);
        Assert.True(session.Context.IsCharacterModification);
        Assert.False(JsonSerializer.Deserialize<CharacterSheetResult>(await tools.SaveStartingLevel(session.SessionId, 7))!.Valid);
        await tools.SaveNameAndSkills(session.SessionId, "Renamed", InitialSkills().ToArray());
        Assert.Equal(12, character.Level);
        Assert.Equal(11500, character.Experience);
        Assert.Equal(9, character.Skills[InitialSkills()[0]]);
        Assert.Null(CharacterCreationRules.ValidateDraft(character, session.Context));
        var result = JsonSerializer.Deserialize<SaveCharacterResult>(await tools.FinalizeCharacter(session.SessionId));
        Assert.True(result!.Success);
        Assert.Equal(11500, character.Experience);
    }
}
