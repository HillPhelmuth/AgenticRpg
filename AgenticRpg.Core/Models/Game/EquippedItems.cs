using System.ComponentModel;
using System.Text;
using System.Text.Json.Serialization;

namespace AgenticRpg.Core.Models.Game;

/// <summary>
/// Represents equipped items on a character
/// </summary>
public class EquippedItems
{
    [Description("Main hand weapon or item")]
    public WeaponItem? MainHand { get; set; }

    [Description("Off hand weapon, shield, or item")]
    public InventoryItem? OffHand { get; set; }

    [Description("Body armor")] public ArmorItem? Armor { get; set; }

    [Description("Head slot item")] public InventoryItem? Head { get; set; }

    [Description("Hand slot item")] public InventoryItem? Hands { get; set; }

    [Description("Feet slot item")] public InventoryItem? Feet { get; set; }

    [Description("First ring slot")] public InventoryItem? Ring1 { get; set; }

    [Description("Second ring slot")] public InventoryItem? Ring2 { get; set; }

    [Description("Neck slot item")] public InventoryItem? Neck { get; set; }

    [JsonIgnore]
    public Dictionary<string, InventoryItem?> Items => new()
        {
            { nameof(MainHand), MainHand },
            { nameof(OffHand), OffHand },
            { nameof(Armor), Armor },
            { nameof(Head), Head },
            { nameof(Hands), Hands },
            { nameof(Feet), Feet },
            { nameof(Ring1), Ring1 },
            { nameof(Ring2), Ring2 },
            { nameof(Neck), Neck }
        };



    /// <summary>
    /// Uses reflection to generate a markdown representation of the equipped items (only non-null)
    /// </summary>
    /// <returns></returns>
    public string AsMarkdown()
    {
        var markdownBuilder = new StringBuilder();
        var properties = GetType().GetProperties()
            .Where(prop => prop.GetValue(this) != null && prop.Name != "Items")
            .ToList();

        foreach (var prop in properties)
        {
            var item = (InventoryItem?)prop.GetValue(this);
            if (item != null)
            {
                markdownBuilder.AppendLine($"**{prop.Name}:** {item.Name} - {item.Description}");
            }
        }

        return markdownBuilder.ToString();

    }
}