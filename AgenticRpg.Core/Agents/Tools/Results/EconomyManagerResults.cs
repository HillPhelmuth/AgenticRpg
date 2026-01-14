using System;
using System.Collections.Generic;
using AgenticRpg.Core.Models.Game;
using AgenticRpg.Core.Models.Enums;

namespace AgenticRpg.Core.Agents.Tools.Results;

/// <summary>
/// Result of getting shop inventory
/// </summary>
public class GetShopInventoryResult
{
    public bool Success { get; set; }
    public string ShopType { get; set; } = string.Empty;
    public string ShopName { get; set; } = string.Empty;
    public string MerchantName { get; set; } = string.Empty;
    public string LocationId { get; set; } = string.Empty;
    public List<InventoryItem> Inventory { get; set; } = [];
    public string Message { get; set; } = string.Empty;
    public string? Error { get; set; }
}

/// <summary>
/// Result of getting item details
/// </summary>
public class GetItemDetailsResult
{
    public bool Success { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public ItemType ItemType { get; set; } = ItemType.Miscellaneous;
    public string Description { get; set; } = string.Empty;
    public int BasePrice { get; set; }
    public int Weight { get; set; }
    public Rarity Rarity { get; set; } = Rarity.Common;
    public Dictionary<string, object> Stats { get; set; } = [];
    public List<string> Requirements { get; set; } = [];
    public string Message { get; set; } = string.Empty;
    public string? Error { get; set; }
}

/// <summary>
/// Result of processing a purchase transaction
/// </summary>
public class ProcessPurchaseResult
{
    public bool Success { get; set; }
    public string CharacterName { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int PricePerItem { get; set; }
    public int TotalCost { get; set; }
    public int PreviousGold { get; set; }
    public int RemainingGold { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Error { get; set; }
}

/// <summary>
/// Result of processing a sale transaction
/// </summary>
public class ProcessSaleResult
{
    public bool Success { get; set; }
    public string CharacterName { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int PricePerItem { get; set; }
    public int TotalValue { get; set; }
    public int PreviousGold { get; set; }
    public int NewGold { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Error { get; set; }
}

/// <summary>
/// Result of calculating price with modifiers
/// </summary>
public class ApplyPriceModifierResult
{
    public bool Success { get; set; }
    public int BasePrice { get; set; }
    public int FinalPrice { get; set; }
    public double PresenceModifier { get; set; }
    public double DispositionModifier { get; set; }
    public double QuantityModifier { get; set; }
    public double TotalModifier { get; set; }
    public string ModifierExplanation { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Error { get; set; }
}

/// <summary>
/// Result of checking if character can afford purchase
/// </summary>
public class CheckAffordabilityResult
{
    public bool Success { get; set; }
    public bool CanAfford { get; set; }
    public string CharacterName { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int TotalCost { get; set; }
    public int CharacterGold { get; set; }
    public int ShortfallAmount { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Error { get; set; }
}

/// <summary>
/// Result of equipping an item
/// </summary>
public class EquipItemResult
{
    public bool Success { get; set; }
    public string CharacterName { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string EquipSlot { get; set; } = string.Empty;
    public string? UnequippedItem { get; set; }
    public Dictionary<string, int> StatChanges { get; set; } = [];
    public int PreviousArmorClass { get; set; }
    public int NewArmorClass { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Error { get; set; }
}
