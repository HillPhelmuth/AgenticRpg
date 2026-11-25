using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Models.Game;

namespace AgenticRpg.Core.Models;

public enum PlayerActionKind { Attack, Cast, UseItem, Equip, Rest, Talk }

public sealed record PlayerActionIntent
{
    public PlayerActionKind Kind { get; init; }
    public string? SelectionId { get; init; }
    public string? TargetId { get; init; }
    public EquipSlot? Slot { get; init; }
    public string? Text { get; init; }
}

/// <summary>Shared costs and eligibility for UI previews, hub acceptance and agent tools.</summary>
public static class PlayerActionRules
{
    public static int SpellCost(Spell spell) => spell.Level + 1;
    public static bool CanEquip(InventoryItem item, EquipSlot slot) => Enum.IsDefined(slot) &&
        (slot != EquipSlot.MainHand || item is WeaponItem || item.ItemType == ItemType.Weapon) &&
        (slot != EquipSlot.Armor || item is ArmorItem || item.ItemType == ItemType.Armor);
    public static string TurnContext(GameState state) => state.IsInCombat && state.CurrentCombat is { } combat
        ? $"{combat.Id}:{combat.Round}:{combat.CurrentTurnIndex}"
        : $"{state.CampaignId}:{state.Campaign.CurrentTurnCharacterId}:{state.CurrentLocationId}";

    public static string Describe(GameState state, Character actor, PlayerActionIntent intent)
    {
        if (!Enum.IsDefined(intent.Kind)) throw new InvalidOperationException("Choose an action, genius.");
        if (actor.PlayerId.Length == 0 || actor.CampaignId != state.CampaignId)
            throw new InvalidOperationException("That character isn't in this adventure.");
        var turn = state.IsInCombat ? state.CurrentCombat?.GetCurrentTurnId() : state.Campaign.CurrentTurnCharacterId;
        if (!string.IsNullOrEmpty(turn) && turn != actor.Id && turn != actor.Name)
            throw new InvalidOperationException("Wait your turn, main character.");
        if (!actor.IsAlive) throw new InvalidOperationException("You're unconscious. Ambition won't fix that.");
        var item = actor.Inventory.FirstOrDefault(i => i.Id == intent.SelectionId && i.Quantity > 0);
        var spell = actor.KnownSpells.FirstOrDefault(s => s.Id == intent.SelectionId);
        var enemy = state.CurrentCombat?.EnemyMonsters.FirstOrDefault(m => m.Id == intent.TargetId && m.CurrentHP > 0);
        var ally = state.Characters.FirstOrDefault(c => c.Id == intent.TargetId);
        var npc = state.World.NPCs.FirstOrDefault(n => n.Id == intent.TargetId && (state.CurrentLocationId is null || n.CurrentLocationId == state.CurrentLocationId));
        var target = enemy?.Name ?? ally?.Name ?? npc?.Name;
        if (intent.TargetId is not null && target is null) throw new InvalidOperationException("That target is no longer available.");
        return intent.Kind switch
        {
            PlayerActionKind.Attack when state.IsInCombat && enemy is not null &&
                actor.Equipment.MainHand is WeaponItem weapon && (intent.SelectionId is null || weapon.Id == intent.SelectionId)
                => $"Attack {enemy.Name} (target ID {enemy.Id}) with equipped {weapon.Name} (item ID {weapon.Id}).",
            PlayerActionKind.Cast when spell is not null && actor.CurrentMP >= SpellCost(spell) && target is not null
                => $"Cast {spell.Name} (spell ID {spell.Id}) on {target} (target ID {intent.TargetId}); cost {SpellCost(spell)} MP.",
            PlayerActionKind.UseItem when item is not null && (item is not MagicItem magic || magic.ChargesRemaining > 0)
                => $"Use {item.Name} (item ID {item.Id}; quantity {item.Quantity}){(target is null ? "" : $" on {target} (target ID {intent.TargetId})")}. Adjudicate effect and consumption using server tools.",
            PlayerActionKind.Equip when item is not null && intent.Slot is { } slot && CanEquip(item, slot)
                => $"Equip {item.Name} (item ID {item.Id}) in {slot}, using EquipItem and its slot validation.",
            PlayerActionKind.Rest when !state.IsInCombat => "Request a party rest. If the situation permits it, use ApplyRest to restore party HP and MP.",
            PlayerActionKind.Talk when npc is not null => $"Talk to {npc.Name} (NPC ID {npc.Id}): {intent.Text ?? "Ask what they know about our objective."}",
            _ => throw new InvalidOperationException("That action isn't available. Check your target, equipment, turn, and resources.")
        };
    }
}
