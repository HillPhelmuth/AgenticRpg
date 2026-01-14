using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using AgenticRpg.Core.Agents.Tools.Results;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Models.Game;

namespace AgenticRpg.Core.Agents.Tools;

public partial class GameMasterTools
{
    [Description("Returns a detailed snapshot of a character so the AI can reason over current stats and inventory.")]
    public async Task<string> GetCharacterDetails(
        [Description("The unique ID of the campaign containing the character.")] string campaignId,
        [Description("The ID of the character to inspect.")] string characterId,
        [Description("Include inventory and equipment details.")] bool includeInventory = true,
        [Description("Include spellbook and spell slot data.")] bool includeSpells = true)
    {
        var gameState = await stateManager.GetCampaignStateAsync(campaignId);
        if (gameState == null)
        {
            return JsonSerializer.Serialize(new CharacterDetailsResult
            {
                Success = false,
                Message = "Campaign not found",
                Error = "Campaign not found"
            });
        }

        var character = gameState.GetCharacter(characterId);
        if (character == null)
        {
            return JsonSerializer.Serialize(new CharacterDetailsResult
            {
                Success = false,
                Message = "Character not found",
                Error = "Character not found"
            });
        }

        var snapshot = BuildCharacterSnapshot(character, includeInventory, includeSpells);

        return JsonSerializer.Serialize(new CharacterDetailsResult
        {
            Success = true,
            Character = snapshot,
            Message = "Character snapshot retrieved successfully"
        });
    }

    [Description("Returns a detailed snapshot of the current world, including locations, NPCs, quests, and environmental metadata.")]
    public async Task<string> GetWorldDetails(
        [Description("The unique ID of the campaign whose world should be inspected.")] string campaignId,
        [Description("Whether to include the full list of locations. Defaults to true.")] bool includeLocations = true,
        [Description("Whether to include the list of NPCs. Defaults to true.")] bool includeNpcs = true,
        [Description("Whether to include the list of quests. Defaults to true.")] bool includeQuests = true,
        [Description("Whether to include the list of world events. Defaults to true.")] bool includeEvents = true)
    {
        var gameState = await stateManager.GetCampaignStateAsync(campaignId);
        if (gameState == null)
        {
            return JsonSerializer.Serialize(new WorldDetailsResult
            {
                Success = false,
                Message = "Campaign not found",
                Error = "Campaign not found"
            });
        }

        var snapshot = BuildWorldSnapshot(gameState, includeLocations, includeNpcs, includeQuests, includeEvents);

        return JsonSerializer.Serialize(new WorldDetailsResult
        {
            Success = true,
            World = snapshot,
            Message = "World snapshot retrieved successfully"
        });
    }

    [Description("Gets recent narrative entries for the campaign, optionally filtering by type, visibility, or character.")]
    public async Task<string> GetNarrativeSummary(
        [Description("The unique ID of the campaign to inspect.")] string campaignId,
        [Description("How many recent entries to return. Defaults to 10.")] int takeLast = 10,
        [Description("Optional filter for narrative type (e.g., Story, Combat, Dialog).")] string? typeFilter = null,
        [Description("Optional filter for visibility (Global, GMOnly, CharacterSpecific).")] NarrativeVisibility? visibilityFilter = null,
        [Description("Optional character ID. If provided, returns entries visible to that character.")] string? characterId = null)
    {
        if (takeLast <= 0)
        {
            takeLast = 5;
        }

        var gameState = await stateManager.GetCampaignStateAsync(campaignId);
        if (gameState == null)
        {
            return JsonSerializer.Serialize(new NarrativeSummaryResult
            {
                Success = false,
                Message = "Campaign not found",
                Error = "Campaign not found"
            });
        }

        var narratives = !string.IsNullOrEmpty(characterId)
            ? await narrativeRepository.GetVisibleToCharacterAsync(campaignId, characterId, 0, takeLast)
            : await narrativeRepository.GetByCampaignIdAsync(campaignId, takeLast);

        var filtered = narratives;

        if (!string.IsNullOrWhiteSpace(typeFilter))
        {
            filtered = filtered.Where(n => string.Equals(n.Type, typeFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (visibilityFilter.HasValue)
        {
            filtered = filtered.Where(n => n.Visibility == visibilityFilter.Value);
        }

        var summary = filtered
            .OrderByDescending(n => n.Timestamp)
            .Take(takeLast)
            .Select(n => new NarrativeSummaryItem
            {
                NarrativeId = n.Id,
                Type = n.Type,
                Visibility = n.Visibility,
                Content = n.Content,
                AgentType = n.AgentType,
                TargetCharacterId = n.TargetCharacterId,
                Timestamp = n.Timestamp
            })
            .ToList();

        return JsonSerializer.Serialize(new NarrativeSummaryResult
        {
            Success = true,
            Narratives = summary,
            Message = $"Returned {summary.Count} narrative entries"
        });
    }

    private static CharacterDetailSnapshot BuildCharacterSnapshot(Character character, bool includeInventory, bool includeSpells)
    {
        return new CharacterDetailSnapshot
        {
            CharacterId = character.Id,
            Name = character.Name,
            PlayerId = character.PlayerId,
            Race = character.Race.ToString(),
            Class = character.Class.ToString(),
            Level = character.Level,
            Experience = character.Experience,
            CurrentHP = character.CurrentHP,
            MaxHP = character.MaxHP,
            CurrentMP = character.CurrentMP,
            MaxMP = character.MaxMP,
            TempHP = character.CurrentHP,
            ArmorClass = character.ArmorClass,
            Initiative = character.Initiative,
            Speed = character.Speed,
            Attributes = character.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            Skills = character.Skills != null ? new Dictionary<string, int>(character.Skills) : null,
            Inventory = includeInventory ? character.Inventory?.ToList() : null,
            Equipment = includeInventory ? character.Equipment : null,
            KnownSpells = includeSpells ? character.KnownSpells?.ToList() : null,
            CurrentSpellSlots = includeSpells && character.CurrentSpellSlots != null ? new Dictionary<int, int>(character.CurrentSpellSlots) : null,
            MaxSpellSlots = includeSpells && character.MaxSpellSlots != null ? new Dictionary<int, int>(character.MaxSpellSlots) : null,
            Gold = character.Gold,
            Background = character.Background,
            PortraitUrl = character.PortraitUrl,
            IsAlive = character.IsAlive,
            IsUnconscious = character.IsUnconscious
        };
    }

    private static WorldDetailSnapshot BuildWorldSnapshot(
        GameState gameState,
        bool includeLocations,
        bool includeNpcs,
        bool includeQuests,
        bool includeEvents)
    {
        var world = gameState.World ?? new World();
        var snapshot = new WorldDetailSnapshot
        {
            WorldId = world.Id,
            Name = world.Name,
            Description = world.Description,
            Theme = world.Theme,
            Geography = world.Geography,
            Politics = world.Politics,
            CurrentLocationId = gameState.CurrentLocationId,
            Weather = gameState.Metadata.TryGetValue("Weather", out var weatherObj) ? weatherObj?.ToString() : null,
            TimeOfDay = gameState.Metadata.TryGetValue("TimeOfDay", out var timeObj) ? timeObj?.ToString() : null
        };

        var currentLocation = world.Locations.FirstOrDefault(l => l.Id == gameState.CurrentLocationId);
        snapshot.CurrentLocation = currentLocation;
        snapshot.CurrentLocationName = currentLocation?.Name;

        if (includeLocations)
        {
            snapshot.Locations = world.Locations.ToList();
        }

        if (includeNpcs)
        {
            snapshot.NPCs = world.NPCs.ToList();
        }

        if (includeQuests)
        {
            snapshot.Quests = world.Quests.ToList();
        }

        if (includeEvents)
        {
            snapshot.Events = world.Events.ToList();
        }

        return snapshot;
    }
}
