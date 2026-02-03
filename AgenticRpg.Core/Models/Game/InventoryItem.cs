using System.ComponentModel;
using System.Text.Json.Serialization;
using AgenticRpg.Core.Helpers;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.DiceRoller.Models;

namespace AgenticRpg.Core.Models.Game;

/// <summary>
/// Represents an item in the game
/// </summary>
[JsonPolymorphic(
    TypeDiscriminatorPropertyName = "$type",
    UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FallBackToBaseType,
    IgnoreUnrecognizedTypeDiscriminators = true)]
[JsonDerivedType(typeof(WeaponItem), typeDiscriminator: "weapon")]
[JsonDerivedType(typeof(ArmorItem), typeDiscriminator: "armor")]
[JsonDerivedType(typeof(MagicItem), typeDiscriminator: "magic")]
public class InventoryItem
{
    [Description("Unique identifier for the item")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Description("Item's display name")] public string Name { get; set; } = string.Empty;

    [Description("Item's description")] public string Description { get; set; } = string.Empty;

    [Description("Number of items in stack")]
    public int Quantity { get; set; } = 1;

    [Description("Item's weight")] public double Weight { get; set; } = 0;

    [Description("Item's value in gold")] public int Value { get; set; } = 0;

    [Description("Type of item")] public ItemType ItemType { get; set; } = ItemType.Miscellaneous;

    [Description("Item's rarity level")] public Rarity Rarity { get; set; } = Rarity.Common;

    [Description("Additional item properties")]
    public Dictionary<string, object> Properties { get; set; } = [];

    public static List<InventoryItem> GetAllItemsFromFile()
    {
        var items = new List<InventoryItem>();
        items.AddRange(FileHelper.ExtractFromAssembly<List<InventoryItem>>("ItemList.json") ?? []);
        items.AddRange(FileHelper.ExtractFromAssembly<List<WeaponItem>>("WeaponList.json") ?? []);
        items.AddRange(FileHelper.ExtractFromAssembly<List<ArmorItem>>("ArmorList.json") ?? []);
        items.AddRange(FileHelper.ExtractFromAssembly<List<MagicItem>>("ItemListMagic.json") ?? []);
        return items;
    }
}
public class WeaponItem : InventoryItem
{
    [Description("Damage dice (e.g. 1d8, 2d6, etc.)")]
    public string DamageDice { get; set; } = "1d6";
    [Description("Damage type (e.g., slashing, piercing)")]
    public string DamageType { get; set; } = "bludgeoning";
    [Description("Is the weapon ranged only (bow), melee only (sword) or both (throwing dagger)?")]
    public WeaponType WeaponType { get; set; } = WeaponType.Melee;
    [Description("Weapon properties (e.g., finesse, heavy)")]
    public List<string> WeaponProperties { get; set; } = [];

    public int GetDieRollCount()
    {
        if (!DamageDice.Contains('d', StringComparison.OrdinalIgnoreCase)) return 0;
        var roll = DamageDice.Split('d', 'D')[0];
        return int.TryParse(roll, out var count) ? count : 1;
    }

    public DieType GetDieType()
    {
        if (!DamageDice.Contains('d', StringComparison.OrdinalIgnoreCase)) return DieType.D6;
        var die = DamageDice.Split('d', 'D', '+', '-')[1];
        return Enum.TryParse<DieType>($"D{die}", out var dieType) ? dieType : DieType.D6;
    }

    public int GetDieRollBonus()
    {
        if (!DamageDice.Contains('d', StringComparison.OrdinalIgnoreCase)) return 0;
        var parts = DamageDice.Split('d', 'D');

        if (parts.Length < 2) return 0;

        var typeAndBonusPart = parts[1];
        var isNegative = typeAndBonusPart.Contains('-', StringComparison.OrdinalIgnoreCase);
        var bonusPart = typeAndBonusPart.Split('+', '-').Last();
        var dieRollBonusAbsValue = int.TryParse(bonusPart, out var bonus) ? bonus : 0;
        return isNegative ? -dieRollBonusAbsValue : dieRollBonusAbsValue;
    }

    public static List<WeaponItem> GetAllWeaponsFromFile()
    {
        return FileHelper.ExtractFromAssembly<List<WeaponItem>>("WeaponList.json") ?? [];
    }
}
public class ArmorItem : InventoryItem
{
    [Description($"Armor class bonus provided by the armor")]
    public int ArmorClassBonus { get; set; } = 1;
    [Description("Does the armor allow the use of agility bonus, or is it too heavy/cumbersome?")]
    public bool AllowAgilityBonus { get; set; } = true;

    public static List<ArmorItem> GetAllArmorFromFile()
    {
        return FileHelper.ExtractFromAssembly<List<ArmorItem>>("ArmorList.json") ?? [];
    }
}
public class MagicItem : InventoryItem
{
    [Description("Magical effects provided by the item. Only very rare items have more than one.")]
    public List<MagicEffect> Effects { get; set; } = [];
    [Description("Number of charges remaining for the magic item. Always 1 for scrolls or other consumed items.")]
    public int ChargesRemaining { get; set; }

    public static List<MagicItem> GetAllMagicItemsFromFile()
    {
        return FileHelper.ExtractFromAssembly<List<MagicItem>>("MagicItemList.json") ?? [];
    }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WeaponType
{
    Melee,
    Ranged,
    Both
}
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MagicEffectType
{
    Healing,
    Damage,
    Buff,
    Debuff,
    Utility
}
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MagicEffectAreaOfEffect
{
    SingleTarget,
    Area,
    Cone,
    Line,
    Self
}