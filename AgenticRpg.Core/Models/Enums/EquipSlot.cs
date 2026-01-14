using System.Text.Json.Serialization;

namespace AgenticRpg.Core.Models.Enums;

/// <summary>
/// Equipment slots for equipping items
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EquipSlot
{
    MainHand,
    OffHand,
    Armor,
    Head,
    Hands,
    Feet,
    Ring1,
    Ring2,
    Neck
}
