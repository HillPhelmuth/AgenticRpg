using System.ComponentModel;
using System.Text.Json;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Models.Game;
using AgenticRpg.Core.Services;

namespace AgenticRpg.Core.Agents.Tools;

public partial class GameMasterTools
{
    [Description("Resolve an inventory item use. Adjudicate its documented effect first; provide justification, HP/MP changes, and whether one consumable is used. Unknown effects are narrative adjudication, not invented bonuses.")]
    public async Task<string> UseInventoryItem(string campaignId, string characterId, string itemId,
        string justification, string? targetId = null, int hpChange = 0, int mpChange = 0, bool consume = false)
    {
        var state = await stateManager.GetCampaignStateAsync(campaignId) ?? throw new InvalidOperationException("Campaign not found.");
        var actor = state.Characters.FirstOrDefault(c => c.Id == characterId) ?? throw new InvalidOperationException("Character not found.");
        var intent = new PlayerActionIntent { Kind = PlayerActionKind.UseItem, SelectionId = itemId, TargetId = targetId };
        _ = PlayerActionRules.Describe(state, actor, intent);
        var item = actor.Inventory.First(i => i.Id == itemId && i.Quantity > 0);
        var target = state.Characters.FirstOrDefault(c => c.Id == (targetId ?? characterId));
        var enemy = state.CurrentCombat?.EnemyMonsters.FirstOrDefault(c => c.Id == targetId);
        if (string.IsNullOrWhiteSpace(justification)) throw new InvalidOperationException("Explain the item's effect.");
        if ((hpChange != 0 || mpChange != 0) && target is null && enemy is null) throw new InvalidOperationException("That target has no HP or MP to change.");
        if (consume && item.ItemType != ItemType.Consumable) throw new InvalidOperationException("This item is not a consumable.");
        if (item is MagicItem magic && magic.ChargesRemaining <= 0) throw new InvalidOperationException("The item has no charges remaining.");
        if (target is not null)
        {
            target.CurrentHP = (int)Math.Clamp((long)target.CurrentHP + hpChange, 0, target.MaxHP);
            target.CurrentMP = (int)Math.Clamp((long)target.CurrentMP + mpChange, 0, target.MaxMP);
        }
        if (enemy is not null) enemy.CurrentHP = (int)Math.Clamp((long)enemy.CurrentHP + hpChange, 0, enemy.MaxHP);
        if (consume) item.Quantity--;
        if (item is MagicItem charged) charged.ChargesRemaining--;
        if (!await stateManager.UpdateCampaignStateAsync(state)) throw new InvalidOperationException("The item effect could not be saved. Review current state before retrying.");
        return JsonSerializer.Serialize(new { Success = true, Item = item.Name, item.Quantity, Target = target?.Name ?? enemy?.Name, hpChange, mpChange, justification });
    }
}
