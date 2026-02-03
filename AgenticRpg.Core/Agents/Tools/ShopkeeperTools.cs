using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AgenticRpg.Core.Agents.Tools.Results;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Models.Game;
using AgenticRpg.Core.Repositories;
using AgenticRpg.Core.Rules;
using AgenticRpg.Core.State;

namespace AgenticRpg.Core.Agents.Tools;

/// <summary>
/// Tools for managing economy, shops, and item transactions
/// </summary>
public class ShopkeeperTools(
    IGameStateManager gameStateManager,
    ICharacterRepository characterRepository)
{
    /// <summary>
    /// Gets the inventory of a shop with available items and prices
    /// </summary>
    [Description("Returns the available items in a shop with their prices, quantities, and descriptions.")]
    public async Task<string> GetShopInventory(
        [Description("The type of shop to display inventory for. Valid types: Weapon, Armor, General, Magic, Tavern. Determines what items are available.")] ShopType shopType,
        [Description("The unique ID of the location where this shop is situated.")] string locationId,
        [Description("The name of the merchant who runs this shop.")] string merchantName,
        [Description("Maximum rarity of items to display in the shop. Only items with a rarity less than or equal to this value will be shown. Most shops will not contain `Rare` or `VeryRare` items")] Rarity maxRarity,
        [Description("The unique ID of the campaign where this shop transaction is occurring.")] string campaignId)
    {
        try
        {
            var gameState = await gameStateManager.GetCampaignStateAsync(campaignId);
            if (gameState == null)
            {
                return JsonSerializer.Serialize(new GetShopInventoryResult
                {
                    Success = false,
                    Error = $"Campaign with ID {campaignId} not found."
                });
            }



            // Find the location
            var location = gameState.World.Locations.FirstOrDefault(l => l.Id == locationId);
            var locationName = location?.Name ?? "Unknown Location";

            // Generate shop inventory based on shop type
            var inventory = GenerateShopInventory(shopType, maxRarity);

            var result = new GetShopInventoryResult
            {
                Success = true,
                ShopType = shopType.ToString(),
                ShopName = $"{merchantName}'s {shopType} Shop",
                MerchantName = merchantName,
                LocationId = locationId,
                Inventory = inventory,
                Message = $"Welcome to {merchantName}'s shop in {locationName}! Browse {inventory.Count} items."
            };

            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new GetShopInventoryResult
            {
                Success = false,
                Error = $"Error getting shop inventory: {ex.Message}"
            });
        }
    }

    [Description("Returns detailed stats, description, and properties for a specific item.")]
    public async Task<string> GetItemDetails(
        [Description("The exact name of the item to get detailed information about (e.g., 'Iron Sword', 'Health Potion', 'Chainmail').")] string itemName,
        [Description("The category or shop type where this item is found. Values: Weapon, Armor, General, Magic, Tavern.")] ShopType shopType,
        [Description("The unique ID of the campaign where this item exists.")] string campaignId)
    {
        try
        {
            var gameState = await gameStateManager.GetCampaignStateAsync(campaignId);
            if (gameState == null)
            {
                return JsonSerializer.Serialize(new GetItemDetailsResult
                {
                    Success = false,
                    Error = $"Campaign with ID {campaignId} not found."
                });
            }

            // Get item details from database/inventory
            var itemDetails = InventoryItem.GetAllItemsFromFile().FirstOrDefault(x => x.Name.Equals(itemName, StringComparison.OrdinalIgnoreCase));

            if (itemDetails == null)
            {
                return JsonSerializer.Serialize(new GetItemDetailsResult
                {
                    Success = false,
                    Error = $"Item '{itemName}' not found in {shopType} category."
                });
            }

            var result = new GetItemDetailsResult
            {
                Success = true,
                Item = itemDetails,
                Message = $"Details for item '{itemName}': {itemDetails.Description}, Value: {itemDetails.Value} gold, Rarity: {itemDetails.Rarity}."
            };

            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new GetItemDetailsResult
            {
                Success = false,
                Error = $"Error getting item details: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Processes a purchase transaction, deducting gold and adding item to inventory
    /// </summary>
    [Description("Completes a purchase by deducting gold from character and adding item to inventory.")]
    public async Task<string> ProcessPurchase(
        [Description("The unique ID of the character making the purchase.")] string characterId,
        [Description("The exact name of the item being purchased.")] string itemName,
        [Description("The number of items to purchase. Must be a positive integer.")] int quantity,
        [Description("The final negotiated price per item in gold pieces. This should reflect any discounts or markups from haggling.")] int negotiatedPrice,
        [Description("If item can be worn, check if player wants to equip it.")] bool equipItem,
        [Description("The unique ID of the campaign where this transaction is occurring.")] string campaignId)
    {
        try
        {
            var gameState = await gameStateManager.GetCampaignStateAsync(campaignId);
            if (gameState == null)
            {
                return JsonSerializer.Serialize(new ProcessPurchaseResult
                {
                    Success = false,
                    Error = $"Campaign with ID {campaignId} not found."
                });
            }

            var character = gameState.Characters.FirstOrDefault(c => c.Id == characterId);
            if (character == null)
            {
                return JsonSerializer.Serialize(new ProcessPurchaseResult
                {
                    Success = false,
                    Error = $"Character with ID {characterId} not found."
                });
            }

            var totalCost = negotiatedPrice * quantity;
            var previousGold = character.Gold;

            // Check affordability
            if (character.Gold < totalCost)
            {
                return JsonSerializer.Serialize(new ProcessPurchaseResult
                {
                    Success = false,
                    Error = $"{character.Name} cannot afford this purchase. Needs {totalCost} gold but has {character.Gold} gold."
                });
            }

            // Deduct gold
            character.Gold -= totalCost;

            // Add item to inventory
            var existingItem = character.Inventory.FirstOrDefault(i => i.Name == itemName);
            if (existingItem != null)
            {
                existingItem.Quantity += quantity;
            }
            else
            {
                var itemDetails = InventoryItem.GetAllItemsFromFile().FirstOrDefault(x => x.Name.Equals(itemName, StringComparison.OrdinalIgnoreCase));
                character.Inventory.Add(itemDetails);
            }

            character.UpdatedAt = DateTime.UtcNow;
            await characterRepository.UpdateAsync(character);
            await gameStateManager.UpdateCampaignStateAsync(gameState);
            var result = new ProcessPurchaseResult
            {
                Success = true,
                CharacterName = character.Name,
                ItemName = itemName,
                Quantity = quantity,
                PricePerItem = negotiatedPrice,
                TotalCost = totalCost,
                PreviousGold = previousGold,
                RemainingGold = character.Gold,
                Message = $"{character.Name} purchased {quantity}x {itemName} for {totalCost} gold. Remaining gold: {character.Gold}."
            };

            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new ProcessPurchaseResult
            {
                Success = false,
                Error = $"Error processing purchase: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Processes a sale transaction, adding gold and removing item from inventory
    /// </summary>
    [Description("Completes a sale by adding gold to character and removing item from inventory.")]
    public async Task<string> ProcessSale(
        [Description("The unique ID of the character selling the item.")] string characterId,
        [Description("The exact name of the item being sold from the character's inventory.")] string itemName,
        [Description("The number of items to sell. Must not exceed the quantity the character owns.")] int quantity,
        [Description("The price per item that the merchant is offering to pay, in gold pieces.")] int offeredPrice,
        [Description("The unique ID of the campaign where this transaction is occurring.")] string campaignId)
    {
        try
        {
            var gameState = await gameStateManager.GetCampaignStateAsync(campaignId);
            if (gameState == null)
            {
                return JsonSerializer.Serialize(new ProcessSaleResult
                {
                    Success = false,
                    Error = $"Campaign with ID {campaignId} not found."
                });
            }

            var character = gameState.Characters.FirstOrDefault(c => c.Id == characterId);
            if (character == null)
            {
                return JsonSerializer.Serialize(new ProcessSaleResult
                {
                    Success = false,
                    Error = $"Character with ID {characterId} not found."
                });
            }

            // Find item in inventory
            var item = character.Inventory.FirstOrDefault(i => i.Name == itemName);
            if (item == null)
            {
                return JsonSerializer.Serialize(new ProcessSaleResult
                {
                    Success = false,
                    Error = $"{character.Name} does not have {itemName} in inventory."
                });
            }

            if (item.Quantity < quantity)
            {
                return JsonSerializer.Serialize(new ProcessSaleResult
                {
                    Success = false,
                    Error = $"{character.Name} only has {item.Quantity}x {itemName}, cannot sell {quantity}."
                });
            }

            var totalValue = offeredPrice * quantity;
            var previousGold = character.Gold;

            // Remove item from inventory
            item.Quantity -= quantity;
            if (item.Quantity <= 0)
            {
                character.Inventory.Remove(item);
            }

            // Add gold
            character.Gold += totalValue;

            character.UpdatedAt = DateTime.UtcNow;
            await characterRepository.UpdateAsync(character);

            var result = new ProcessSaleResult
            {
                Success = true,
                CharacterName = character.Name,
                ItemName = itemName,
                Quantity = quantity,
                PricePerItem = offeredPrice,
                TotalValue = totalValue,
                PreviousGold = previousGold,
                NewGold = character.Gold,
                Message = $"{character.Name} sold {quantity}x {itemName} for {totalValue} gold. New gold total: {character.Gold}."
            };

            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new ProcessSaleResult
            {
                Success = false,
                Error = $"Error processing sale: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Calculates the final price with modifiers based on character stats and merchant disposition
    /// </summary>
    [Description("Applies price modifiers based on character Presence, merchant disposition, and quantity purchased.")]
    public async Task<string> ApplyPriceModifier(
        [Description("The base/list price of the item in gold pieces, before any modifiers are applied.")] int basePrice,
        [Description("The character's Presence attribute score (typically 3-20). Higher Presence reduces prices.")] int characterPresence,
        [Description("The merchant's attitude toward the character, ranging from -100 (hostile) to +100 (friendly). 0 is neutral.")] int merchantDisposition,
        [Description("The quantity being purchased. Larger quantities may receive bulk discounts (10+ items = 10% off, 50+ = 20% off, 100+ = 30% off).")] int quantity,
        [Description("The unique ID of the campaign where this calculation is being performed.")] string campaignId)
    {
        try
        {
            var gameState = await gameStateManager.GetCampaignStateAsync(campaignId);
            if (gameState == null)
            {
                return JsonSerializer.Serialize(new ApplyPriceModifierResult
                {
                    Success = false,
                    Error = $"Campaign with ID {campaignId} not found."
                });
            }

            // Calculate Presence modifier (higher Presence = lower prices)
            var presenceModifier = 1.0 - ((characterPresence - 10) * 0.02); // Each point above 10 = 2% discount
            presenceModifier = Math.Max(0.7, Math.Min(1.3, presenceModifier)); // Clamp between 70% and 130%

            // Calculate disposition modifier
            var dispositionModifier = 1.0 - (merchantDisposition / 200.0); // -100 = 150%, 0 = 100%, 100 = 50%
            dispositionModifier = Math.Max(0.5, Math.Min(1.5, dispositionModifier)); // Clamp between 50% and 150%

            // Calculate quantity modifier (bulk discount)
            var quantityModifier = 1.0;
            if (quantity >= 10) quantityModifier = 0.9; // 10% discount for 10+ items
            if (quantity >= 50) quantityModifier = 0.8; // 20% discount for 50+ items
            if (quantity >= 100) quantityModifier = 0.7; // 30% discount for 100+ items

            // Apply all modifiers
            var totalModifier = presenceModifier * dispositionModifier * quantityModifier;
            var finalPrice = (int)Math.Ceiling(basePrice * totalModifier);

            var explanation = $"Base price: {basePrice}g. ";
            explanation += $"Presence modifier: {presenceModifier:P0} (Presence {characterPresence}). ";
            explanation += $"Merchant disposition: {dispositionModifier:P0} ({merchantDisposition}). ";
            explanation += $"Quantity discount: {quantityModifier:P0} ({quantity} items). ";
            explanation += $"Total modifier: {totalModifier:P0}.";

            var result = new ApplyPriceModifierResult
            {
                Success = true,
                BasePrice = basePrice,
                FinalPrice = finalPrice,
                PresenceModifier = presenceModifier,
                DispositionModifier = dispositionModifier,
                QuantityModifier = quantityModifier,
                TotalModifier = totalModifier,
                ModifierExplanation = explanation,
                Message = $"Final price: {finalPrice} gold (from base {basePrice} gold, {totalModifier:P0} modifier)."
            };

            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new ApplyPriceModifierResult
            {
                Success = false,
                Error = $"Error calculating price modifier: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Checks if a character can afford a purchase
    /// </summary>
    [Description("Validates that the character has enough gold to complete a purchase.")]
    public async Task<string> CheckAffordability(
        [Description("The unique ID of the character attempting to make a purchase.")] string characterId,
        [Description("The name of the item the character wants to purchase.")] string itemName,
        [Description("The number of items the character wants to purchase.")] int quantity,
        [Description("The unique ID of the campaign where this check is being performed.")] string campaignId)
    {
        try
        {
            var gameState = await gameStateManager.GetCampaignStateAsync(campaignId);
            if (gameState == null)
            {
                return JsonSerializer.Serialize(new CheckAffordabilityResult
                {
                    Success = false,
                    Error = $"Campaign with ID {campaignId} not found."
                });
            }

            var character = gameState.Characters.FirstOrDefault(c => c.Id == characterId);
            if (character == null)
            {
                return JsonSerializer.Serialize(new CheckAffordabilityResult
                {
                    Success = false,
                    Error = $"Character with ID {characterId} not found."
                });
            }

            // Get item details to determine cost (using simplified base price)
            var itemDetails = GetItemDetailsFromDatabase(itemName, ShopType.General);
            var basePrice = itemDetails?.Item.Value ?? 10; // Default to 10 gold if not found
            var totalCost = basePrice * quantity;

            var canAfford = character.Gold >= totalCost;
            var shortfall = canAfford ? 0 : totalCost - character.Gold;

            string message;
            if (canAfford)
            {
                message = $"{character.Name} can afford {quantity}x {itemName} ({totalCost} gold). Current gold: {character.Gold}.";
            }
            else
            {
                message = $"{character.Name} cannot afford {quantity}x {itemName}. Needs {totalCost} gold but has {character.Gold} gold (short {shortfall} gold).";
            }

            var result = new CheckAffordabilityResult
            {
                Success = true,
                CanAfford = canAfford,
                CharacterName = character.Name,
                ItemName = itemName,
                Quantity = quantity,
                TotalCost = totalCost,
                CharacterGold = character.Gold,
                ShortfallAmount = shortfall,
                Message = message
            };

            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new CheckAffordabilityResult
            {
                Success = false,
                Error = $"Error checking affordability: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Equips an item to a character's equipment slot
    /// </summary>
    [Description("Equips an item from inventory to an equipment slot and recalculates character stats.")]
    public async Task<string> EquipItem(
        [Description("The unique ID of the character equipping the item.")] string characterId,
        [Description("The exact name of the item in the character's inventory to equip.")] string itemName,
        [Description("The equipment slot to place the item in. Valid slots: MainHand, OffHand, Armor, Head, Hands, Feet, Ring1, Ring2, Neck.")] EquipSlot equipSlot,
        [Description("The unique ID of the campaign where this action is occurring.")] string campaignId)
    {
        try
        {
            var gameState = await gameStateManager.GetCampaignStateAsync(campaignId);
            if (gameState == null)
            {
                return JsonSerializer.Serialize(new EquipItemResult
                {
                    Success = false,
                    Error = $"Campaign with ID {campaignId} not found."
                });
            }

            var character = gameState.Characters.FirstOrDefault(c => c.Id == characterId);
            if (character == null)
            {
                return JsonSerializer.Serialize(new EquipItemResult
                {
                    Success = false,
                    Error = $"Character with ID {characterId} not found."
                });
            }

            // Find item in inventory
            var itemIndex = character.Inventory.FindIndex(i => i.Name == itemName);
            var item = itemIndex >= 0 ? character.Inventory[itemIndex] : null;
            if (item == null)
            {
                return JsonSerializer.Serialize(new EquipItemResult
                {
                    Success = false,
                    Error = $"{character.Name} does not have {itemName} in inventory."
                });
            }

            var previousAC = character.ArmorClass;
            string? unequippedItem = null;
            var statChanges = new Dictionary<string, int>();

            // Unequip existing item in slot
            var equipment = character.Equipment;
            switch (equipSlot)
            {
                case EquipSlot.MainHand:
                    if (equipment.MainHand != null) unequippedItem = equipment.MainHand.Name;
                    if (item is WeaponItem weaponItem)
                    {
                        equipment.MainHand = weaponItem;
                    }
                    else if (item.ItemType == ItemType.Weapon)
                    {
                        var converted = ConvertToWeaponItem(item);
                        character.Inventory[itemIndex] = converted;
                        equipment.MainHand = converted;
                    }
                    else
                    {
                        return JsonSerializer.Serialize(new EquipItemResult
                        {
                            Success = false,
                            Error = $"{itemName} is not a weapon and cannot be equipped in MainHand."
                        });
                    }
                    break;
                case EquipSlot.OffHand:
                    if (equipment.OffHand != null) unequippedItem = equipment.OffHand.Name;
                    equipment.OffHand = item;
                    break;
                case EquipSlot.Armor:
                    if (equipment.Armor != null) unequippedItem = equipment.Armor.Name;
                    if (item is ArmorItem armorItem)
                    {
                        equipment.Armor = armorItem;
                    }
                    else if (item.ItemType == ItemType.Armor)
                    {
                        var converted = ConvertToArmorItem(item);
                        character.Inventory[itemIndex] = converted;
                        equipment.Armor = converted;
                    }
                    else
                    {
                        return JsonSerializer.Serialize(new EquipItemResult
                        {
                            Success = false,
                            Error = $"{itemName} is not armor and cannot be equipped in Armor slot."
                        });
                    }

                    var newAC = StatsCalculator.CalculateArmorClass(character);
                    statChanges["ArmorClass"] = newAC - previousAC;
                    break;
                case EquipSlot.Head:
                    if (equipment.Head != null) unequippedItem = equipment.Head.Name;
                    equipment.Head = item;
                    break;
                case EquipSlot.Hands:
                    if (equipment.Hands != null) unequippedItem = equipment.Hands.Name;
                    equipment.Hands = item;
                    break;
                case EquipSlot.Feet:
                    if (equipment.Feet != null) unequippedItem = equipment.Feet.Name;
                    equipment.Feet = item;
                    break;
                case EquipSlot.Ring1:
                    if (equipment.Ring1 != null) unequippedItem = equipment.Ring1.Name;
                    equipment.Ring1 = item;
                    break;
                case EquipSlot.Ring2:
                    if (equipment.Ring2 != null) unequippedItem = equipment.Ring2.Name;
                    equipment.Ring2 = item;
                    break;
                case EquipSlot.Neck:
                    if (equipment.Neck != null) unequippedItem = equipment.Neck.Name;
                    equipment.Neck = item;
                    break;
                default:
                    return JsonSerializer.Serialize(new EquipItemResult
                    {
                        Success = false,
                        Error = $"Invalid equipment slot: {equipSlot}"
                    });
            }

            character.UpdatedAt = DateTime.UtcNow;
            await characterRepository.UpdateAsync(character);

            var result = new EquipItemResult
            {
                Success = true,
                CharacterName = character.Name,
                ItemName = itemName,
                EquipSlot = equipSlot.ToString(),
                UnequippedItem = unequippedItem,
                StatChanges = statChanges,
                PreviousArmorClass = previousAC,
                NewArmorClass = StatsCalculator.CalculateArmorClass(character),
                Message = $"{character.Name} equipped {itemName} to {equipSlot}." +
                          (unequippedItem != null ? $" Unequipped {unequippedItem}." : "")
            };

            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new EquipItemResult
            {
                Success = false,
                Error = $"Error equipping item: {ex.Message}"
            });
        }
    }

    #region Helper Methods

    private static WeaponItem ConvertToWeaponItem(InventoryItem item)
    {
        var weapon = new WeaponItem
        {
            Id = item.Id,
            Name = item.Name,
            Description = item.Description,
            Quantity = item.Quantity,
            Weight = item.Weight,
            Value = item.Value,
            ItemType = item.ItemType,
            Rarity = item.Rarity,
            Properties = item.Properties
        };

        if (TryGetString(item.Properties, "DamageDice", out var damageDice))
        {
            weapon.DamageDice = damageDice;
        }
        else if (TryGetString(item.Properties, "Damage", out var legacyDamageDice))
        {
            weapon.DamageDice = legacyDamageDice;
        }

        if (TryGetString(item.Properties, "DamageType", out var damageType))
        {
            weapon.DamageType = damageType;
        }

        if (TryGetString(item.Properties, "WeaponType", out var weaponTypeString)
            && Enum.TryParse<WeaponType>(weaponTypeString, ignoreCase: true, out var weaponType))
        {
            weapon.WeaponType = weaponType;
        }

        if (item.Properties.TryGetValue("WeaponProperties", out var weaponPropertiesRaw)
            && TryGetStringList(weaponPropertiesRaw, out var weaponProperties))
        {
            weapon.WeaponProperties = weaponProperties;
        }

        return weapon;
    }

    private static ArmorItem ConvertToArmorItem(InventoryItem item)
    {
        var armor = new ArmorItem
        {
            Id = item.Id,
            Name = item.Name,
            Description = item.Description,
            Quantity = item.Quantity,
            Weight = item.Weight,
            Value = item.Value,
            ItemType = item.ItemType,
            Rarity = item.Rarity,
            Properties = item.Properties
        };

        if (TryGetInt(item.Properties, "ArmorClassBonus", out var armorClassBonus))
        {
            armor.ArmorClassBonus = armorClassBonus;
        }
        else if (TryGetInt(item.Properties, "ArmorBonus", out var armorBonus))
        {
            armor.ArmorClassBonus = armorBonus;
        }
        else if (TryGetInt(item.Properties, "ArmorClass", out var armorClass))
        {
            armor.ArmorClassBonus = armorClass > 10 ? armorClass - 10 : armorClass;
        }

        if (TryGetBool(item.Properties, "AllowAgilityBonus", out var allowAgilityBonus))
        {
            armor.AllowAgilityBonus = allowAgilityBonus;
        }

        return armor;
    }

    private static bool TryGetString(Dictionary<string, object> properties, string key, out string value)
    {
        value = string.Empty;
        if (!properties.TryGetValue(key, out var raw) || raw is null) return false;

        switch (raw)
        {
            case string s:
                value = s;
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.String:
                value = element.GetString() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(value);
            default:
                return false;
        }
    }

    private static bool TryGetInt(Dictionary<string, object> properties, string key, out int value)
    {
        value = 0;
        if (!properties.TryGetValue(key, out var raw) || raw is null) return false;

        switch (raw)
        {
            case int i:
                value = i;
                return true;
            case long l:
                value = (int)l;
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.Number:
                return element.TryGetInt32(out value);
            case string s when int.TryParse(s, out var parsed):
                value = parsed;
                return true;
            default:
                return false;
        }
    }

    private static bool TryGetBool(Dictionary<string, object> properties, string key, out bool value)
    {
        value = false;
        if (!properties.TryGetValue(key, out var raw) || raw is null) return false;

        switch (raw)
        {
            case bool b:
                value = b;
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False:
                value = element.GetBoolean();
                return true;
            case string s when bool.TryParse(s, out var parsed):
                value = parsed;
                return true;
            default:
                return false;
        }
    }

    private static bool TryGetStringList(object raw, out List<string> values)
    {
        values = [];

        if (raw is JsonElement element && element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var s = item.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) values.Add(s);
                }
            }

            return values.Count > 0;
        }

        if (raw is IEnumerable<object> objEnumerable)
        {
            foreach (var item in objEnumerable)
            {
                if (item is string s && !string.IsNullOrWhiteSpace(s))
                {
                    values.Add(s);
                }
            }

            return values.Count > 0;
        }

        if (raw is IEnumerable<string> stringEnumerable)
        {
            values = stringEnumerable.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            return values.Count > 0;
        }

        return false;
    }

    private List<InventoryItem> GenerateShopInventory(ShopType shopType, Rarity maxRarity)
    {
        var inventory = new List<InventoryItem>();

        switch (shopType)
        {
            case ShopType.Weapon:
                var allWeapon = WeaponItem.GetAllWeaponsFromFile().Where(x => x.Rarity <= maxRarity);
                inventory.AddRange(allWeapon);
                break;

            case ShopType.Armor:
                var allArmor = ArmorItem.GetAllArmorFromFile().Where(x => x.Rarity <= maxRarity);
                inventory.AddRange(allArmor);
                break;

            case ShopType.General:
                var allItems = InventoryItem.GetAllItemsFromFile().Where(x => x.Rarity <= maxRarity);
                inventory.AddRange(allItems);
                break;

            case ShopType.Magic:
                var allMagicItems = MagicItem.GetAllMagicItemsFromFile().Where(x => x.Rarity <= maxRarity);
                inventory.AddRange(allMagicItems);
                break;

            case ShopType.Tavern:
                inventory.Add(new InventoryItem { Name = "Ale (Mug)", ItemType = ItemType.Drink, Description = "Refreshing local brew", Value = 2, Quantity = 100 });
                inventory.Add(new InventoryItem { Name = "Wine (Bottle)", ItemType = ItemType.Drink, Description = "Fine vintage wine", Value = 10, Quantity = 20 });
                inventory.Add(new InventoryItem { Name = "Hot Meal", ItemType = ItemType.Food, Description = "Hearty stew and bread", Value = 5, Quantity = 50 });
                inventory.Add(new InventoryItem { Name = "Room (1 night)", ItemType = ItemType.Service, Description = "Comfortable lodging", Value = 20, Quantity = 5 });
                break;

            default:
                inventory.Add(new InventoryItem { Name = "Mystery Item", ItemType = ItemType.Miscellaneous, Description = "An interesting curiosity", Value = 25, Quantity = 1 });
                break;
        }

        return inventory;
    }

    private (InventoryItem Item, Dictionary<string, object> Stats, List<string> Requirements)? GetItemDetailsFromDatabase(string itemName, ShopType category)
    {
        // Simplified item database - would be loaded from actual database or JSON files
        var itemDatabase = new Dictionary<string, (InventoryItem Item, Dictionary<string, object> Stats, List<string> Requirements)>(StringComparer.OrdinalIgnoreCase)
        {
            {
                "Iron Sword",
                (
                    new WeaponItem
                    {
                        Name = "Iron Sword",
                        ItemType = ItemType.Weapon,
                        Description = "A sturdy iron blade suitable for combat",
                        Value = 50,
                        Weight = 3,
                        Rarity = Rarity.Common,
                        DamageDice = "1d8",
                        DamageType = "slashing"
                    },
                    new Dictionary<string, object> { { "DamageDice", "1d8" }, { "DamageType", "Slashing" } },
                    ["Might 10"]
                )
            },
            {
                "Health Potion",
                (
                    new InventoryItem
                    {
                        Name = "Health Potion",
                        ItemType = ItemType.Consumable,
                        Description = "A magical potion that restores health when consumed",
                        Value = 50,
                        Weight = 0,
                        Rarity = Rarity.Common,
                        Properties = new Dictionary<string, object> { { "Healing", "2d4+2" } }
                    },
                    new Dictionary<string, object> { { "Healing", "2d4+2" } },
                    []
                )
            },
            {
                "Leather Armor",
                (
                    new ArmorItem
                    {
                        Name = "Leather Armor",
                        ItemType = ItemType.Armor,
                        Description = "Protective leather armor offering decent protection",
                        Value = 50,
                        Weight = 10,
                        Rarity = Rarity.Common,
                        ArmorClassBonus = 1,
                        AllowAgilityBonus = true
                    },
                    new Dictionary<string, object> { { "ArmorClassBonus", 1 }, { "AllowAgilityBonus", true } },
                    []
                )
            },
            {
                "Chainmail",
                (
                    new ArmorItem
                    {
                        Name = "Chainmail",
                        ItemType = ItemType.Armor,
                        Description = "Heavy armor made of interlocking metal rings",
                        Value = 200,
                        Weight = 55,
                        Rarity = Rarity.Uncommon,
                        ArmorClassBonus = 6,
                        AllowAgilityBonus = false
                    },
                    new Dictionary<string, object> { { "ArmorClassBonus", 6 }, { "AllowAgilityBonus", false }, { "Disadvantage", "Stealth" } },
                    ["Might 13"]
                )
            }
        };

        return itemDatabase.TryGetValue(itemName, out var value) ? value : null;
    }

    #endregion
}
