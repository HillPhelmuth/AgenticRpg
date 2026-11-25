using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Enums;

namespace AgenticRpg.Core.Services;

public interface IPlayerStateProjector
{
    GameState Project(GameState state, string? characterId);
    bool CanSee(Narrative narrative, string? characterId);
}

public sealed class PlayerStateProjector : IPlayerStateProjector
{
    public bool CanSee(Narrative narrative, string? characterId) =>
        narrative.Visibility == NarrativeVisibility.Global ||
        (narrative.Visibility == NarrativeVisibility.CharacterSpecific &&
         !string.IsNullOrWhiteSpace(characterId) && narrative.TargetCharacterId == characterId);

    // Build a new projection; never remove secrets from the cached state used by agents.
    public GameState Project(GameState state, string? characterId) => new()
    {
        Campaign = state.Campaign,
        World = state.World,
        Characters = state.Characters.ToList(),
        CurrentCombat = state.CurrentCombat,
        PartyCombatRewards = state.PartyCombatRewards.ToList(),
        CurrentLocationId = state.CurrentLocationId,
        RecentNarratives = state.RecentNarratives.Where(n => CanSee(n, characterId)).DistinctBy(n => n.Id).ToList(),
        SelectedModel = state.SelectedModel,
        IsProcessing = state.IsProcessing,
        LastUpdated = state.LastUpdated,
        Version = state.Version,
        Metadata = []
    };
}
