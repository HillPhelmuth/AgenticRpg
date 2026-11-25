using AgenticRpg.Core.Agents.Threads;
using AgenticRpg.Core.Hubs;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Models.Game;
using AgenticRpg.Core.Repositories;
using AgenticRpg.Core.State;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace AgenticRpg.Core.Services;

public interface ISessionCompletionService
{
    Task ReconcileAsync(string sessionId) => Task.CompletedTask;
    Task<string> FinalizeCharacterAsync(string sessionId, bool requireIdle = false);
    Task<string> FinalizeWorldAsync(string sessionId, bool requireIdle = false);
    Task<string> FinalizeCampaignAsync(string sessionId, bool requireIdle = false);
}

public sealed class SessionCompletionService(ISessionStateManager sessions, ICharacterRepository characters,
    IWorldRepository worlds, ICampaignRepository campaigns, IAgentSessionStore threads,
    ScopeOperationLocks locks, IHubContext<GameHub> hub, ILogger<SessionCompletionService> logger, ICampaignQuestPreparation questPreparation, RuntimeProgress? progress = null) : ISessionCompletionService
{
    public Task<string> FinalizeCharacterAsync(string sessionId, bool requireIdle = false) => CompleteAsync(sessionId, requireIdle, SessionType.CharacterCreation, async session =>
    {
        var character = session.Context.DraftCharacter ?? throw new InvalidOperationException("No draft character found.");
        var missing = CharacterMissingRequirements(character);
        if (missing.Count > 0) throw new InvalidOperationException("Complete: " + string.Join(", ", missing) + ".");
        var progressionError = Rules.CharacterCreationRules.ValidateDraft(character, session.Context);
        if (progressionError is not null) throw new InvalidOperationException(progressionError);
        if (!session.Context.IsCharacterModification) Rules.CharacterCreationRules.InitializeStats(character);
        character.Id = session.Context.PersistenceEntityId ??= Guid.NewGuid().ToString();
        PrepareEquipment(character);
        character.PlayerId = session.PlayerId;
        character.UpdatedAt = DateTime.UtcNow;
        if (!string.IsNullOrEmpty(session.Context.CampaignId)) character.CampaignId = session.Context.CampaignId;
        if (await characters.GetByIdAsync(character.Id) is null) await characters.CreateAsync(character);
        return character.Id;
    });

    public Task<string> FinalizeWorldAsync(string sessionId, bool requireIdle = false) => CompleteAsync(sessionId, requireIdle, SessionType.WorldBuilding, async session =>
    {
        var world = session.Context.DraftWorld ?? throw new InvalidOperationException("No draft world found.");
        if (string.IsNullOrWhiteSpace(world.Name)) throw new InvalidOperationException("World must have a name.");
        world.UpdatedAt = DateTime.UtcNow;
        world.CompletionSessionId = session.SessionId;
        if (!string.IsNullOrEmpty(session.Context.OriginalWorldId))
        {
            var original = await worlds.GetByIdAsync(session.Context.OriginalWorldId)
                ?? throw new InvalidOperationException("The original world no longer exists.");
            world.Id = original.Id;
            world.CreatedAt = original.CreatedAt;
            world.CreatorId = original.CreatorId;
            await worlds.UpdateAsync(world);
        }
        else
        {
            world.Id = session.Context.PersistenceEntityId ??= Guid.NewGuid().ToString();
            world.CreatorId = session.PlayerId;
            if (await worlds.GetByIdAsync(world.Id) is null) await worlds.CreateAsync(world);
        }
        return world.Id;
    });

    public Task<string> FinalizeCampaignAsync(string sessionId, bool requireIdle = false) => CompleteAsync(sessionId, requireIdle, SessionType.CampaignSetup, async session =>
    {
        var campaign = session.Context.DraftCampaign ?? throw new InvalidOperationException("No draft campaign found.");
        if (string.IsNullOrWhiteSpace(campaign.Name)) throw new InvalidOperationException("Campaign must have a name.");
        campaign.Id = session.Context.PersistenceEntityId ??= Guid.NewGuid().ToString();
        campaign.OwnerId = session.PlayerId;
        var world = await worlds.GetByIdAsync(campaign.WorldId) ?? throw new InvalidOperationException("World not found for campaign.");
        if (await campaigns.GetByIdAsync(campaign.Id) is null)
        {
            await questPreparation.PrepareAsync(campaign, world);
            await campaigns.CreateAsync(campaign);
        }
        return campaign.Id;
    });

    public async Task ReconcileAsync(string sessionId)
    {
        using var lease = await locks.AcquireCompletionAsync(sessionId);
        var session = await sessions.GetSessionStateAsync(sessionId);
        if (session is null || session.IsCompleted || !session.FinalizationPending || session.Context.PersistenceEntityId is not { } id) return;
        var exists = session.SessionType switch
        {
            SessionType.CharacterCreation => (await characters.GetByIdAsync(id))?.PlayerId == session.PlayerId,
            SessionType.CampaignSetup => (await campaigns.GetByIdAsync(id))?.OwnerId == session.PlayerId,
            SessionType.WorldBuilding => (await worlds.GetByIdAsync(id))?.CompletionSessionId == session.SessionId,
            _ => false
        };
        if (!exists) return;
        session.FinalizationPending = false;
        try { await sessions.CompleteSessionAsync(sessionId, id); }
        catch { session.IsCompleted = false; session.ResultEntityId = null; session.FinalizationPending = true; throw; }
    }

    private async Task<string> CompleteAsync(string id, bool requireIdle, SessionType type, Func<SessionState, Task<string>> save)
    {
        var gate = locks.Completion(id);
        await gate.WaitAsync();
        try
        {
            var session = await sessions.GetSessionStateAsync(id) ?? throw new InvalidOperationException("Session not found or expired.");
            if (session.SessionType != type) throw new InvalidOperationException("Incorrect session type.");
            if (session.IsCompleted && !string.IsNullOrEmpty(session.ResultEntityId)) return session.ResultEntityId;
            if (requireIdle && locks.HasActiveRequests(id)) throw new InvalidOperationException("Wait for generation to finish before saving.");
            if (progress is not null) await progress.ReportAsync(id, ProcessingPhase.Saving);
            session.Context.PersistenceEntityId ??= session.Context.OriginalWorldId ?? Guid.NewGuid().ToString();
            session.FinalizationPending = true;
            await sessions.UpdateSessionStateAsync(session);
            var result = await save(session);
            // Record completion before attempting delivery, so retrying cannot save twice.
            session.ResultEntityId = result;
            session.IsCompleted = true;
            session.FinalizationPending = false;
            session.ExpiresAt = DateTime.UtcNow.AddHours(24);
            try { await sessions.CompleteSessionAsync(id, result); }
            catch
            {
                session.IsCompleted = false;
                session.ResultEntityId = null;
                session.FinalizationPending = true;
                throw;
            }
            threads.TryRemoveScope(id);
            try { await hub.Clients.Group(GameHub.GetCampaignGroupName(id)).SendAsync("SessionCompleted", id, result); }
            catch (Exception ex) { logger.LogWarning(ex, "Saved session {SessionId}; completion is available on reconnect", id); }
            return result;
        }
        finally { gate.Release(); }
    }

    public static List<string> CharacterMissingRequirements(Character? character)
    {
        if (character is null) return ["character draft"];
        List<string> missing = [];
        if (string.IsNullOrWhiteSpace(character.Name)) missing.Add("name");
        if (character.Race == CharacterRace.None) missing.Add("race");
        if (character.Class == CharacterClass.None) missing.Add("class");
        if (character.Attributes.Count == 0) missing.Add("attributes");
        if (character.MaxHP <= 0) missing.Add("hit points");
        return missing;
    }

    private static void PrepareEquipment(Character character)
    {
        if (character.Equipment.MainHand is null)
        {
            var weaponName = character.Class switch
            {
                CharacterClass.Warrior or CharacterClass.WarMage => "Shortsword",
                CharacterClass.Cleric or CharacterClass.Paladin => "Mace",
                CharacterClass.Wizard => "Quarterstaff", _ => "Dagger"
            };
            var weapon = character.Inventory.OfType<WeaponItem>().FirstOrDefault();
            if (weapon is null)
            {
                weapon = WeaponItem.GetAllWeaponsFromFile().Find(w => w.Name.Equals(weaponName, StringComparison.OrdinalIgnoreCase));
                if (weapon is not null && character.Inventory.All(i => i.Id != weapon.Id)) character.Inventory.Add(weapon);
            }
            character.Equipment.MainHand = weapon;
        }
        if (character.Equipment.Armor is null)
        {
            var armor = character.Inventory.OfType<ArmorItem>().FirstOrDefault(a => a.ItemType == ItemType.Armor)
                ?? ArmorItem.GetAllArmorFromFile().Find(a => a.Name.Contains("Leather", StringComparison.OrdinalIgnoreCase));
            if (armor is not null && character.Inventory.All(i => i.Id != armor.Id)) character.Inventory.Add(armor);
            character.Equipment.Armor = armor;
        }
    }
}
